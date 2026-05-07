use std::{
    collections::HashSet,
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum ImageSearchOcrState {
    #[default]
    Pending = 0,
    Indexed = 1,
    RetryableEmpty = 2,
    RetryableError = 3,
    Failed = 4,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ImageSearchIndexRecord {
    pub file_path: PathBuf,
    pub file_length_bytes: u64,
    pub last_write_time_unix_ms: u64,
    pub ocr_language_tag: String,
    pub ocr_engine_id: String,
    pub ocr_completed: bool,
    pub ocr_state: ImageSearchOcrState,
    pub ocr_retry_count: u32,
    pub next_ocr_retry_unix_ms: u64,
    pub ocr_text: String,
    pub indexed_at_unix_ms: u64,
    pub last_error: String,
}

impl ImageSearchIndexRecord {
    pub fn search_text(&self) -> String {
        let file_name = self
            .file_path
            .file_stem()
            .and_then(|name| name.to_str())
            .unwrap_or_default();
        [file_name, self.ocr_text.as_str()]
            .into_iter()
            .filter(|part| !part.trim().is_empty())
            .collect::<Vec<_>>()
            .join(" ")
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImageSearchRecordDiagnostics {
    pub file_path: PathBuf,
    pub status_text: String,
    pub details_text: String,
    pub match_text: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ImageSearchSources(u32);

impl ImageSearchSources {
    pub const NONE: Self = Self(0);
    pub const FILE_NAME: Self = Self(1 << 0);
    pub const OCR: Self = Self(1 << 1);
    pub const ALL: Self = Self(Self::FILE_NAME.0 | Self::OCR.0);

    pub fn from_bits(bits: u32) -> Self {
        Self(bits & Self::ALL.0)
    }

    pub fn bits(self) -> u32 {
        self.0
    }

    pub fn contains(self, source: Self) -> bool {
        self.0 & source.0 == source.0
    }

    pub fn is_empty(self) -> bool {
        self.0 == 0
    }
}

impl Default for ImageSearchSources {
    fn default() -> Self {
        Self::ALL
    }
}

pub fn rank_image_search_items<T, SearchText, FileName, CapturedAt>(
    items: impl IntoIterator<Item = T>,
    query: &str,
    searchable_text: SearchText,
    file_name: FileName,
    captured_at_unix_ms: CapturedAt,
    sources: ImageSearchSources,
    exact_match: bool,
) -> Vec<T>
where
    SearchText: Fn(&T) -> &str,
    FileName: Fn(&T) -> &str,
    CapturedAt: Fn(&T) -> u64,
{
    let mut items: Vec<T> = items.into_iter().collect();
    let normalized_query = normalize_image_search_text(query);
    if normalized_query.is_empty() || sources.is_empty() {
        items.sort_by_key(|item| std::cmp::Reverse(captured_at_unix_ms(item)));
        return items;
    }

    let mut ranked = items
        .into_iter()
        .filter_map(|item| {
            let search_text = if sources.contains(ImageSearchSources::OCR) {
                searchable_text(&item)
            } else {
                ""
            };
            let file_text = if sources.contains(ImageSearchSources::FILE_NAME) {
                file_name(&item)
            } else {
                ""
            };
            let score = score_normalized_image_search(
                &normalized_query,
                search_text,
                file_text,
                exact_match,
            );
            (score > 0).then_some((item, score))
        })
        .collect::<Vec<_>>();

    ranked.sort_by(|(left_item, left_score), (right_item, right_score)| {
        right_score
            .cmp(left_score)
            .then_with(|| captured_at_unix_ms(right_item).cmp(&captured_at_unix_ms(left_item)))
    });

    ranked.into_iter().map(|(item, _)| item).collect()
}

pub fn build_image_search_text(
    record: Option<&ImageSearchIndexRecord>,
    fallback_file_name: &str,
) -> String {
    record.map_or_else(
        || {
            Path::new(fallback_file_name)
                .file_stem()
                .and_then(|name| name.to_str())
                .unwrap_or(fallback_file_name)
                .to_string()
        },
        ImageSearchIndexRecord::search_text,
    )
}

pub fn image_search_record_diagnostics(
    file_path: impl Into<PathBuf>,
    record: Option<&ImageSearchIndexRecord>,
    fallback_file_name: &str,
    query: Option<&str>,
    sources: ImageSearchSources,
    exact_match: bool,
) -> ImageSearchRecordDiagnostics {
    ImageSearchRecordDiagnostics {
        file_path: file_path.into(),
        status_text: record.map_or_else(
            || "Pending index".to_string(),
            |record| image_search_status_text(record).to_string(),
        ),
        details_text: image_search_diagnostics_text(record, fallback_file_name),
        match_text: query.map_or_else(String::new, |query| {
            describe_image_search_match(record, fallback_file_name, query, sources, exact_match)
        }),
    }
}

pub fn image_search_status_text(record: &ImageSearchIndexRecord) -> &'static str {
    match record.ocr_state {
        ImageSearchOcrState::Pending => "Pending index",
        ImageSearchOcrState::Indexed if record.ocr_text.trim().is_empty() => "No text",
        ImageSearchOcrState::Indexed => "OCR ready",
        ImageSearchOcrState::RetryableEmpty => "Indexing OCR",
        ImageSearchOcrState::RetryableError => "OCR error",
        ImageSearchOcrState::Failed => "OCR failed",
    }
}

pub fn image_search_diagnostics_text(
    record: Option<&ImageSearchIndexRecord>,
    fallback_file_name: &str,
) -> String {
    let Some(record) = record else {
        return format!("Status: Pending index\nFile: {fallback_file_name}");
    };

    let mut parts = vec![
        format!("Status: {}", image_search_status_text(record)),
        format!("Indexed: {}", record.indexed_at_unix_ms),
    ];

    if record.ocr_retry_count > 0 {
        parts.push(format!("OCR retries: {}", record.ocr_retry_count));
    }
    if record.next_ocr_retry_unix_ms > 0 {
        parts.push(format!("Next retry: {}", record.next_ocr_retry_unix_ms));
    }
    if !record.ocr_text.trim().is_empty() {
        parts.push(format!(
            "OCR: {}",
            trim_for_image_search_diagnostics(&record.ocr_text)
        ));
    }
    if !record.last_error.trim().is_empty() {
        parts.push(format!(
            "Last error: {}",
            trim_for_image_search_diagnostics(&record.last_error)
        ));
    }

    parts.join("\n")
}

pub fn describe_image_search_match(
    record: Option<&ImageSearchIndexRecord>,
    fallback_file_name: &str,
    query: &str,
    sources: ImageSearchSources,
    exact_match: bool,
) -> String {
    let normalized_query = normalize_image_search_text(query);
    if normalized_query.is_empty() {
        return String::new();
    }

    let normalized_file_name = normalize_image_search_text(
        Path::new(fallback_file_name)
            .file_stem()
            .and_then(|name| name.to_str())
            .unwrap_or(fallback_file_name),
    );
    let normalized_ocr =
        normalize_image_search_text(record.map_or("", |record| record.ocr_text.as_str()));
    let mut matched_sources = Vec::with_capacity(2);

    if sources.contains(ImageSearchSources::FILE_NAME)
        && score_pre_normalized_image_search(
            &normalized_query,
            "",
            &normalized_file_name,
            exact_match,
        ) > 0
    {
        matched_sources.push("file name");
    }

    if sources.contains(ImageSearchSources::OCR)
        && score_pre_normalized_image_search(&normalized_query, &normalized_ocr, "", exact_match)
            > 0
    {
        matched_sources.push("OCR");
    }

    if matched_sources.is_empty() {
        String::new()
    } else {
        format!("Match: {}", matched_sources.join(" + "))
    }
}

pub fn score_image_search(
    query: &str,
    searchable_text: &str,
    file_name: &str,
    exact_match: bool,
) -> i32 {
    let normalized_query = normalize_image_search_text(query);
    score_normalized_image_search(&normalized_query, searchable_text, file_name, exact_match)
}

pub fn score_normalized_image_search(
    normalized_query: &str,
    searchable_text: &str,
    file_name: &str,
    exact_match: bool,
) -> i32 {
    if normalized_query.trim().is_empty() {
        return 1;
    }

    let normalized_text = normalize_image_search_text(searchable_text);
    let normalized_file = normalize_image_search_text(file_name);
    score_pre_normalized_image_search(
        normalized_query,
        &normalized_text,
        &normalized_file,
        exact_match,
    )
}

pub fn score_pre_normalized_image_search(
    normalized_query: &str,
    normalized_search_text: &str,
    normalized_file_name: &str,
    exact_match: bool,
) -> i32 {
    if normalized_query.trim().is_empty() {
        return 1;
    }

    let query_tokens = tokenize(normalized_query);
    if query_tokens.is_empty() {
        return 1;
    }

    let search_tokens = tokenize(normalized_search_text);
    let file_tokens = tokenize(normalized_file_name);
    let search_token_set = search_tokens.iter().copied().collect::<HashSet<_>>();
    let file_token_set = file_tokens.iter().copied().collect::<HashSet<_>>();

    let mut score = 0;

    if normalized_file_name == normalized_query {
        score += 1000;
    }
    if normalized_search_text == normalized_query {
        score += 900;
    }

    if contains_token_sequence(&file_tokens, &query_tokens) {
        score += 700;
    }
    if contains_token_sequence(&search_tokens, &query_tokens) {
        score += 650;
    }
    if contains_token_prefix_sequence(&file_tokens, &query_tokens) {
        score += 600;
    }
    if contains_token_prefix_sequence(&search_tokens, &query_tokens) {
        score += 560;
    }

    if exact_match {
        if query_tokens.len() == 1 {
            let token = query_tokens[0];
            if file_token_set.contains(token) {
                score += 120;
            }
            if search_token_set.contains(token) {
                score += 100;
            }
        }

        return score;
    }

    for token in &query_tokens {
        if file_token_set.contains(token)
            || file_tokens.iter().any(|value| value.starts_with(token))
        {
            score += 20;
        }
        if search_token_set.contains(token)
            || search_tokens.iter().any(|value| value.starts_with(token))
        {
            score += 12;
        }
    }

    let matched_tokens = query_tokens
        .iter()
        .filter(|token| {
            file_token_set.contains(**token)
                || search_token_set.contains(**token)
                || file_tokens.iter().any(|value| value.starts_with(**token))
                || search_tokens.iter().any(|value| value.starts_with(**token))
        })
        .count();
    let minimum_matched_tokens = match query_tokens.len() {
        0 | 1 => 1,
        2 | 3 => 2,
        length => length - 1,
    };
    if matched_tokens < minimum_matched_tokens {
        return 0;
    }

    if matched_tokens == query_tokens.len() {
        score += 50;
    } else {
        score += (matched_tokens * 8) as i32;
    }

    score
}

pub fn normalize_image_search_text(input: &str) -> String {
    if input.trim().is_empty() {
        return String::new();
    }

    let mut normalized = String::with_capacity(input.len());
    let mut last_was_space = false;
    for ch in input.chars().flat_map(char::to_lowercase) {
        let value = if ch.is_alphanumeric() { ch } else { ' ' };
        if value == ' ' {
            if last_was_space {
                continue;
            }
            last_was_space = true;
            normalized.push(' ');
        } else {
            last_was_space = false;
            normalized.push(value);
        }
    }

    normalized.trim().to_string()
}

fn tokenize(input: &str) -> Vec<&str> {
    input.split_whitespace().collect()
}

fn contains_token_sequence(haystack: &[&str], needle: &[&str]) -> bool {
    !needle.is_empty()
        && haystack.len() >= needle.len()
        && haystack
            .windows(needle.len())
            .any(|window| window.iter().zip(needle).all(|(left, right)| left == right))
}

fn contains_token_prefix_sequence(haystack: &[&str], needle: &[&str]) -> bool {
    !needle.is_empty()
        && haystack.len() >= needle.len()
        && haystack.windows(needle.len()).any(|window| {
            window
                .iter()
                .zip(needle)
                .all(|(left, right)| left.starts_with(right))
        })
}

fn trim_for_image_search_diagnostics(value: &str) -> String {
    let trimmed = value.trim();
    if trimmed.len() <= 220 {
        trimmed.to_string()
    } else {
        format!("{}...", &trimmed[..217])
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Clone, Debug, PartialEq, Eq)]
    struct SearchItem {
        id: &'static str,
        text: &'static str,
        file: &'static str,
        captured_at: u64,
    }

    fn indexed_record() -> ImageSearchIndexRecord {
        ImageSearchIndexRecord {
            file_path: PathBuf::from("C:/captures/invoice.png"),
            file_length_bytes: 123,
            last_write_time_unix_ms: 10,
            ocr_language_tag: "en-US".into(),
            ocr_engine_id: "winocr-v1".into(),
            ocr_completed: true,
            ocr_state: ImageSearchOcrState::Indexed,
            ocr_retry_count: 0,
            next_ocr_retry_unix_ms: 0,
            ocr_text: "Total due tomorrow".into(),
            indexed_at_unix_ms: 20,
            last_error: String::new(),
        }
    }

    #[test]
    fn image_search_normalizes_like_legacy_query_matcher() {
        assert_eq!(
            normalize_image_search_text("  Setup: OCR_Result-01.PNG  "),
            "setup ocr result 01 png"
        );
        assert_eq!(normalize_image_search_text("CAFÉ/menu"), "café menu");
    }

    #[test]
    fn image_search_scores_exact_file_matches_above_ocr_text() {
        let exact_file = score_image_search("invoice", "invoice due soon", "invoice", false);
        let text_only = score_image_search("invoice", "invoice", "capture", false);

        assert!(exact_file > text_only);
    }

    #[test]
    fn image_search_filters_items_that_do_not_match_enough_tokens() {
        assert_eq!(
            score_image_search("alpha beta gamma", "alpha only", "capture", false),
            0
        );
    }

    #[test]
    fn image_search_exact_match_disables_loose_token_completion() {
        let loose = score_image_search("screen cap", "screenshot capture", "image", false);
        let exact = score_image_search("screen cap", "screenshot capture", "image", true);

        assert!(loose > 0);
        assert_eq!(exact, 560);
    }

    #[test]
    fn image_search_ranks_by_score_then_newest_capture() {
        let items = vec![
            SearchItem {
                id: "old-text",
                text: "invoice",
                file: "capture",
                captured_at: 10,
            },
            SearchItem {
                id: "new-text",
                text: "invoice",
                file: "capture",
                captured_at: 20,
            },
            SearchItem {
                id: "file",
                text: "",
                file: "invoice",
                captured_at: 5,
            },
        ];

        let ranked = rank_image_search_items(
            items,
            "invoice",
            |item| item.text,
            |item| item.file,
            |item| item.captured_at,
            ImageSearchSources::ALL,
            false,
        );

        assert_eq!(
            ranked.iter().map(|item| item.id).collect::<Vec<_>>(),
            vec!["file", "new-text", "old-text"]
        );
    }

    #[test]
    fn image_search_sources_can_limit_matches_to_file_names() {
        let items = vec![
            SearchItem {
                id: "ocr-only",
                text: "invoice",
                file: "capture",
                captured_at: 20,
            },
            SearchItem {
                id: "file",
                text: "",
                file: "invoice",
                captured_at: 10,
            },
        ];

        let ranked = rank_image_search_items(
            items,
            "invoice",
            |item| item.text,
            |item| item.file,
            |item| item.captured_at,
            ImageSearchSources::FILE_NAME,
            false,
        );

        assert_eq!(
            ranked.iter().map(|item| item.id).collect::<Vec<_>>(),
            vec!["file"]
        );
    }

    #[test]
    fn image_search_record_builds_combined_search_text() {
        assert_eq!(indexed_record().search_text(), "invoice Total due tomorrow");
        assert_eq!(
            build_image_search_text(None, "capture-file.png"),
            "capture-file"
        );
    }

    #[test]
    fn image_search_record_statuses_match_legacy_labels() {
        let mut record = indexed_record();
        assert_eq!(image_search_status_text(&record), "OCR ready");

        record.ocr_text.clear();
        assert_eq!(image_search_status_text(&record), "No text");

        record.ocr_state = ImageSearchOcrState::RetryableError;
        assert_eq!(image_search_status_text(&record), "OCR error");

        record.ocr_state = ImageSearchOcrState::Failed;
        assert_eq!(image_search_status_text(&record), "OCR failed");
    }

    #[test]
    fn image_search_diagnostics_include_retry_error_and_trimmed_ocr() {
        let mut record = indexed_record();
        record.ocr_retry_count = 2;
        record.next_ocr_retry_unix_ms = 300;
        record.ocr_text = "a".repeat(230);
        record.last_error = "bad OCR".into();

        let diagnostics = image_search_diagnostics_text(Some(&record), "invoice.png");

        assert!(diagnostics.contains("Status: OCR ready"));
        assert!(diagnostics.contains("Indexed: 20"));
        assert!(diagnostics.contains("OCR retries: 2"));
        assert!(diagnostics.contains("Next retry: 300"));
        assert!(diagnostics.contains("OCR: "));
        assert!(diagnostics.contains("..."));
        assert!(diagnostics.contains("Last error: bad OCR"));
    }

    #[test]
    fn image_search_match_description_reports_file_and_ocr_sources() {
        let record = indexed_record();

        assert_eq!(
            describe_image_search_match(
                Some(&record),
                "invoice.png",
                "invoice",
                ImageSearchSources::ALL,
                false
            ),
            "Match: file name"
        );
        assert_eq!(
            describe_image_search_match(
                Some(&record),
                "invoice.png",
                "tomorrow",
                ImageSearchSources::ALL,
                false
            ),
            "Match: OCR"
        );
        assert_eq!(
            describe_image_search_match(
                Some(&record),
                "invoice.png",
                "invoice tomorrow",
                ImageSearchSources::ALL,
                false
            ),
            ""
        );
    }

    #[test]
    fn image_search_record_diagnostics_wraps_status_details_and_match() {
        let record = indexed_record();
        let diagnostics = image_search_record_diagnostics(
            "C:/captures/invoice.png",
            Some(&record),
            "invoice.png",
            Some("tomorrow"),
            ImageSearchSources::OCR,
            false,
        );

        assert_eq!(diagnostics.status_text, "OCR ready");
        assert_eq!(diagnostics.match_text, "Match: OCR");
        assert!(diagnostics.details_text.contains("Status: OCR ready"));
    }
}
