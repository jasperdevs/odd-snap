use std::path::PathBuf;

use gpui::KeyDownEvent;
use oddsnap_core::{
    image_search_record_diagnostics, rank_image_search_items, AppSettings, ImageSearchIndexRecord,
    ImageSearchRecordDiagnostics, ImageSearchSources,
};

#[derive(Debug, Clone)]
pub(crate) struct ImageSearchUiState {
    pub(crate) query: String,
    status: String,
    pub(crate) input_active: bool,
}

impl ImageSearchUiState {
    pub(crate) fn new() -> Self {
        Self {
            query: String::new(),
            status: "Image search ready.".into(),
            input_active: false,
        }
    }

    pub(crate) fn activate(&mut self) {
        self.input_active = true;
        self.refresh_status();
    }

    pub(crate) fn hide(&mut self) {
        self.query.clear();
        self.input_active = false;
        self.refresh_status();
    }

    pub(crate) fn refresh_status(&mut self) {
        self.status = if self.input_active {
            "Image search active.".into()
        } else {
            "Image search ready.".into()
        };
    }

    pub(crate) fn status_text(&self, settings: &AppSettings, match_count: usize) -> String {
        if !settings.show_image_search_bar {
            return "Image search hidden.".into();
        }

        let sources = sources(settings);
        if sources.is_empty() {
            return "Image search off: no sources enabled.".into();
        }

        let query = self.query.trim();
        if query.is_empty() {
            return self.status.clone();
        }

        let exact = if settings.image_search_exact_match {
            "exact"
        } else {
            "loose"
        };
        format!(
            "{match_count} matches for '{query}' via {}/{}.",
            sources_label(sources),
            exact
        )
    }

    pub(crate) fn handle_key_down(&mut self, event: &KeyDownEvent) -> bool {
        if !self.input_active {
            return false;
        }
        if event.keystroke.modifiers.control
            || event.keystroke.modifiers.platform
            || event.keystroke.modifiers.alt
        {
            return false;
        }

        let key = event.keystroke.key.to_ascii_lowercase();
        match key.as_str() {
            "escape" => {
                if self.query.is_empty() {
                    self.input_active = false;
                } else {
                    self.query.clear();
                }
            }
            "backspace" => {
                self.query.pop();
            }
            "delete" => {
                self.query.clear();
            }
            "space" => {
                self.query.push(' ');
            }
            "enter" | "tab" | "shift" | "control" | "alt" | "cmd" | "super" => return false,
            _ => {
                let Some(text) = event.keystroke.key_char.as_deref() else {
                    return false;
                };
                if text.chars().any(char::is_control) {
                    return false;
                }
                self.query.push_str(text);
            }
        }

        self.refresh_status();
        true
    }
}

pub(crate) trait ImageSearchItem: Clone {
    fn file_path(&self) -> &str;
    fn file_name(&self) -> &str;
    fn captured_at_unix_ms(&self) -> u64;
    fn image_search_ocr_text(&self) -> &str;
    fn image_search_record(&self) -> Option<&ImageSearchIndexRecord>;
}

pub(crate) fn sources(settings: &AppSettings) -> ImageSearchSources {
    ImageSearchSources::from_bits(settings.image_search_sources)
}

pub(crate) fn sources_label(sources: ImageSearchSources) -> &'static str {
    match (
        sources.contains(ImageSearchSources::FILE_NAME),
        sources.contains(ImageSearchSources::OCR),
    ) {
        (true, true) => "file+OCR",
        (true, false) => "file",
        (false, true) => "OCR",
        (false, false) => "off",
    }
}

pub(crate) fn settings_summary(settings: &AppSettings) -> String {
    if !settings.show_image_search_bar {
        return "image search hidden".into();
    }

    let exact = if settings.image_search_exact_match {
        "exact"
    } else {
        "loose"
    };

    format!(
        "image search {}/{}",
        sources_label(sources(settings)),
        exact
    )
}

pub(crate) fn is_active(settings: &AppSettings, state: &ImageSearchUiState) -> bool {
    settings.show_image_search_bar
        && !state.query.trim().is_empty()
        && !sources(settings).is_empty()
}

pub(crate) fn visible_items<T>(
    settings: &AppSettings,
    state: &ImageSearchUiState,
    items: &[T],
    idle_limit: usize,
    active_limit: usize,
) -> Vec<T>
where
    T: ImageSearchItem,
{
    if !is_active(settings, state) {
        return items.iter().take(idle_limit).cloned().collect();
    }

    rank_image_search_items(
        items.iter().cloned(),
        state.query.trim(),
        |entry| entry.image_search_ocr_text(),
        |entry| entry.file_name(),
        |entry| entry.captured_at_unix_ms(),
        sources(settings),
        settings.image_search_exact_match,
    )
    .into_iter()
    .take(active_limit)
    .collect()
}

pub(crate) fn diagnostics<T>(
    settings: &AppSettings,
    state: &ImageSearchUiState,
    entry: &T,
) -> ImageSearchRecordDiagnostics
where
    T: ImageSearchItem,
{
    image_search_record_diagnostics(
        PathBuf::from(entry.file_path()),
        entry.image_search_record(),
        entry.file_name(),
        is_active(settings, state).then_some(state.query.trim()),
        sources(settings),
        settings.image_search_exact_match,
    )
}

pub(crate) fn match_summary(diagnostics: &ImageSearchRecordDiagnostics) -> String {
    if diagnostics.match_text.is_empty() {
        diagnostics.status_text.clone()
    } else {
        format!("{} - {}", diagnostics.match_text, diagnostics.status_text)
    }
}

pub(crate) fn toggle_source(settings: &mut AppSettings, source: ImageSearchSources) {
    let mut bits = sources(settings).bits();
    if ImageSearchSources::from_bits(bits).contains(source) {
        bits &= !source.bits();
    } else {
        bits |= source.bits();
    }
    settings.image_search_sources = ImageSearchSources::from_bits(bits).bits();
}
