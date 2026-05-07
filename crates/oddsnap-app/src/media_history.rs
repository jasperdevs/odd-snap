use std::{
    fs,
    path::{Path, PathBuf},
};

use oddsnap_core::{orphan_video_thumbnail_paths, HistoryIndex, HistoryKind, HistoryStore};

use crate::{history_kind_label, CaptureHistoryEntry};

pub(crate) const DEFAULT_MEDIA_HISTORY_VISIBLE_LIMIT: usize = 6;
const MEDIA_HISTORY_VISIBLE_INCREMENT: usize = 12;
pub(crate) const IMAGE_SEARCH_MEDIA_HISTORY_VISIBLE_LIMIT: usize = 20;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum HistoryKindFilter {
    All,
    Image,
    Gif,
    Video,
    Sticker,
}

impl HistoryKindFilter {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::All => "All",
            Self::Image => "Images",
            Self::Gif => "GIFs",
            Self::Video => "Videos",
            Self::Sticker => "Stickers",
        }
    }

    pub(crate) fn next(self) -> Self {
        match self {
            Self::All => Self::Image,
            Self::Image => Self::Gif,
            Self::Gif => Self::Video,
            Self::Video => Self::Sticker,
            Self::Sticker => Self::All,
        }
    }

    fn matches(self, kind: HistoryKind) -> bool {
        match self {
            Self::All => true,
            Self::Image => kind == HistoryKind::Image,
            Self::Gif => kind == HistoryKind::Gif,
            Self::Video => kind == HistoryKind::Video,
            Self::Sticker => kind == HistoryKind::Sticker,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum HistoryUploadFilter {
    All,
    Uploaded,
    Failed,
    Pending,
    NoLink,
}

impl HistoryUploadFilter {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::All => "All uploads",
            Self::Uploaded => "Uploaded",
            Self::Failed => "Failed",
            Self::Pending => "Pending",
            Self::NoLink => "No link",
        }
    }

    pub(crate) fn next(self) -> Self {
        match self {
            Self::All => Self::Uploaded,
            Self::Uploaded => Self::Failed,
            Self::Failed => Self::Pending,
            Self::Pending => Self::NoLink,
            Self::NoLink => Self::All,
        }
    }

    fn matches(self, entry: &CaptureHistoryEntry) -> bool {
        match self {
            Self::All => true,
            Self::Uploaded => entry
                .upload_url
                .as_deref()
                .is_some_and(|url| !url.trim().is_empty()),
            Self::Failed => entry
                .upload_error
                .as_deref()
                .filter(|error| !error.trim().is_empty())
                .is_some_and(|error| !error.starts_with("pending: ")),
            Self::Pending => entry
                .upload_error
                .as_deref()
                .is_some_and(|error| error.starts_with("pending: ")),
            Self::NoLink => {
                entry
                    .upload_url
                    .as_deref()
                    .is_none_or(|url| url.trim().is_empty())
                    && entry
                        .upload_error
                        .as_deref()
                        .is_none_or(|error| error.trim().is_empty())
            }
        }
    }
}

pub(crate) fn filtered_capture_history(
    entries: &[CaptureHistoryEntry],
    kind_filter: HistoryKindFilter,
    upload_filter: HistoryUploadFilter,
) -> Vec<CaptureHistoryEntry> {
    entries
        .iter()
        .filter(|entry| kind_filter.matches(entry.kind) && upload_filter.matches(entry))
        .cloned()
        .collect()
}

pub(crate) fn history_selection_contains(selected_paths: &[String], path: &str) -> bool {
    selected_paths
        .iter()
        .any(|selected_path| selected_path == path)
}

pub(crate) fn toggle_selected_history_path(selected_paths: &mut Vec<String>, path: String) -> bool {
    if let Some(index) = selected_paths
        .iter()
        .position(|selected_path| selected_path == &path)
    {
        selected_paths.remove(index);
        false
    } else {
        selected_paths.push(path);
        true
    }
}

pub(crate) fn add_selected_history_paths<I>(selected_paths: &mut Vec<String>, paths: I) -> usize
where
    I: IntoIterator<Item = String>,
{
    let mut added_count = 0usize;
    for path in paths {
        if !history_selection_contains(selected_paths, &path) {
            selected_paths.push(path);
            added_count += 1;
        }
    }
    added_count
}

pub(crate) fn retain_selected_history_paths(
    selected_paths: &mut Vec<String>,
    available_paths: &[String],
) {
    selected_paths.retain(|selected_path| {
        available_paths
            .iter()
            .any(|available_path| available_path == selected_path)
    });
}

pub(crate) fn next_media_history_visible_limit(current: usize, total: usize) -> usize {
    current
        .max(DEFAULT_MEDIA_HISTORY_VISIBLE_LIMIT)
        .saturating_add(MEDIA_HISTORY_VISIBLE_INCREMENT)
        .min(total.max(DEFAULT_MEDIA_HISTORY_VISIBLE_LIMIT))
}

pub(crate) fn media_history_detail_line(entry: &CaptureHistoryEntry, now_ms: u64) -> String {
    format!(
        "{} · {} · {}x{} · {} · {}",
        history_kind_label(entry.kind),
        entry.mode.label(),
        entry.width,
        entry.height,
        format_storage_size(entry.file_size_bytes),
        format_history_age(entry.captured_at_unix_ms, now_ms)
    )
}

pub(crate) fn media_history_group_label(captured_at_unix_ms: u64, now_ms: u64) -> &'static str {
    if captured_at_unix_ms == 0 {
        return "This session";
    }

    let elapsed_seconds = now_ms.saturating_sub(captured_at_unix_ms) / 1000;
    if elapsed_seconds < 60 * 60 {
        "Past hour"
    } else if elapsed_seconds < 60 * 60 * 24 {
        "Past day"
    } else if elapsed_seconds < 60 * 60 * 24 * 7 {
        "Past week"
    } else {
        "Older"
    }
}

pub(crate) fn media_history_count_text(
    visible_count: usize,
    total_count: usize,
    total_bytes: u64,
    filter_active: bool,
) -> String {
    let size = format_storage_size(total_bytes);
    if visible_count < total_count {
        if filter_active {
            format!("{visible_count} of {total_count} filtered rows · {size}")
        } else {
            format!("{visible_count} of {total_count} rows · {size}")
        }
    } else if filter_active {
        format!("{visible_count} filtered rows · {size}")
    } else {
        format!("{visible_count} rows · {size}")
    }
}

pub(crate) fn cleanup_orphan_video_thumbnails(
    history_store: &HistoryStore,
    index: &HistoryIndex,
) -> Result<usize, String> {
    let candidates = managed_video_thumbnail_candidates(history_store)?;
    let orphan_paths = orphan_video_thumbnail_paths(index, candidates);
    let mut removed = 0usize;
    for path in orphan_paths {
        match fs::remove_file(&path) {
            Ok(()) => removed += 1,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => {
                return Err(format!("failed to remove {}: {error}", path.display()));
            }
        }
    }
    Ok(removed)
}

pub(crate) fn managed_video_thumbnail_candidates(
    history_store: &HistoryStore,
) -> Result<Vec<PathBuf>, String> {
    let Some(directory) = history_store
        .path()
        .parent()
        .map(|parent| parent.join("thumbs"))
    else {
        return Ok(Vec::new());
    };
    if !directory.exists() {
        return Ok(Vec::new());
    }

    let entries = fs::read_dir(&directory)
        .map_err(|error| format!("failed to read {}: {error}", directory.display()))?;
    let mut candidates = Vec::new();
    for entry in entries {
        let entry = entry.map_err(|error| {
            format!(
                "failed to inspect thumbnail in {}: {error}",
                directory.display()
            )
        })?;
        let path = entry.path();
        if is_managed_video_thumbnail_path(&path) {
            candidates.push(path);
        }
    }
    Ok(candidates)
}

fn is_managed_video_thumbnail_path(path: &Path) -> bool {
    path.extension()
        .and_then(|ext| ext.to_str())
        .is_some_and(|ext| ext.eq_ignore_ascii_case("jpg"))
}

fn format_storage_size(bytes: u64) -> String {
    const KIB: f64 = 1024.0;
    const MIB: f64 = KIB * 1024.0;
    const GIB: f64 = MIB * 1024.0;

    let bytes_f = bytes as f64;
    if bytes < 1024 {
        format!("{bytes} B")
    } else if bytes_f < MIB {
        format!("{:.1} KB", bytes_f / KIB)
    } else if bytes_f < GIB {
        format!("{:.1} MB", bytes_f / MIB)
    } else {
        format!("{:.1} GB", bytes_f / GIB)
    }
}

fn format_history_age(captured_at_unix_ms: u64, now_ms: u64) -> String {
    if captured_at_unix_ms == 0 {
        return "this session".into();
    }

    let elapsed_seconds = now_ms.saturating_sub(captured_at_unix_ms) / 1000;
    if elapsed_seconds < 60 {
        "just now".into()
    } else if elapsed_seconds < 60 * 60 {
        let minutes = elapsed_seconds / 60;
        format!("{minutes}m ago")
    } else if elapsed_seconds < 60 * 60 * 24 {
        let hours = elapsed_seconds / (60 * 60);
        format!("{hours}h ago")
    } else if elapsed_seconds < 60 * 60 * 24 * 7 {
        let days = elapsed_seconds / (60 * 60 * 24);
        format!("{days}d ago")
    } else {
        "older".into()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::CaptureMode;

    #[test]
    fn media_history_visible_limit_expands_without_shrinking_below_default() {
        assert_eq!(
            next_media_history_visible_limit(DEFAULT_MEDIA_HISTORY_VISIBLE_LIMIT, 40),
            18
        );
        assert_eq!(next_media_history_visible_limit(18, 20), 20);
        assert_eq!(next_media_history_visible_limit(2, 4), 6);
    }

    #[test]
    fn media_history_detail_text_uses_index_metadata() {
        let entry = CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: HistoryKind::Image,
            path: "capture.png".into(),
            file_name: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            file_size_bytes: 1024,
            captured_at_unix_ms: 1,
            image_search_ocr_text: String::new(),
            image_search_record: None,
            upload_url: None,
            upload_provider: None,
            upload_error: None,
        };

        assert_eq!(
            media_history_detail_line(&entry, 60_000),
            "Image · Full screen · 10x10 · 1.0 KB · just now"
        );
        assert_eq!(media_history_group_label(0, 60_000), "This session");
        assert_eq!(media_history_group_label(1, 60_000), "Past hour");
        assert_eq!(media_history_group_label(1, 60_000 * 60 * 2), "Past day");
        assert_eq!(
            media_history_group_label(1, 60_000 * 60 * 24 * 2),
            "Past week"
        );
        assert_eq!(media_history_group_label(1, 60_000 * 60 * 24 * 10), "Older");
        assert_eq!(
            media_history_count_text(3, 9, 3 * 1024 * 1024, true),
            "3 of 9 filtered rows · 3.0 MB"
        );
        assert_eq!(
            media_history_count_text(3, 9, 3 * 1024 * 1024, false),
            "3 of 9 rows · 3.0 MB"
        );
        assert_eq!(
            media_history_count_text(9, 9, 0, true),
            "9 filtered rows · 0 B"
        );
        assert_eq!(media_history_count_text(9, 9, 0, false), "9 rows · 0 B");
        assert_eq!(format_storage_size(1024 * 1024 * 3), "3.0 MB");
        assert_eq!(format_history_age(0, 60_000), "this session");
        assert_eq!(format_history_age(1, 60_000 * 10), "9m ago");
    }

    #[test]
    fn history_selection_toggles_and_discards_missing_paths() {
        let mut selected_paths = vec!["one.png".into()];

        assert!(history_selection_contains(&selected_paths, "one.png"));
        assert!(toggle_selected_history_path(
            &mut selected_paths,
            "two.png".into()
        ));
        assert_eq!(selected_paths, vec!["one.png", "two.png"]);

        assert!(!toggle_selected_history_path(
            &mut selected_paths,
            "one.png".into()
        ));
        assert_eq!(selected_paths, vec!["two.png"]);

        let added_count =
            add_selected_history_paths(&mut selected_paths, ["two.png".into(), "three.png".into()]);
        assert_eq!(added_count, 1);
        assert_eq!(selected_paths, vec!["two.png", "three.png"]);

        retain_selected_history_paths(&mut selected_paths, &["three.png".into(), "two.png".into()]);
        assert_eq!(selected_paths, vec!["two.png", "three.png"]);

        retain_selected_history_paths(&mut selected_paths, &["three.png".into()]);
        assert_eq!(selected_paths, vec!["three.png"]);
    }

    #[test]
    fn cleanup_orphan_video_thumbnails_only_removes_unreferenced_jpgs() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-video-thumb-cleanup-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        let thumbs = root.join("thumbs");
        fs::create_dir_all(&thumbs).expect("create thumbnail dir");

        let live = thumbs.join("live.jpg");
        let orphan = thumbs.join("orphan.JPG");
        let non_jpg = thumbs.join("notes.txt");
        fs::write(&live, b"live").expect("write live thumb");
        fs::write(&orphan, b"orphan").expect("write orphan thumb");
        fs::write(&non_jpg, b"keep").expect("write non-jpg");

        let store = HistoryStore::new(root.join("rust-history.json"));
        let index = HistoryIndex {
            entries: vec![oddsnap_core::HistoryEntry {
                file_path: root.join("clip.mp4"),
                file_name: "clip.mp4".into(),
                captured_at_unix_ms: 1,
                width: 10,
                height: 10,
                file_size_bytes: 10,
                kind: HistoryKind::Video,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
                thumbnail_path: Some(live.clone()),
            }],
            ..HistoryIndex::default()
        };

        assert_eq!(
            managed_video_thumbnail_candidates(&store)
                .expect("list managed thumbnails")
                .len(),
            2
        );
        assert_eq!(
            cleanup_orphan_video_thumbnails(&store, &index).expect("cleanup thumbnails"),
            1
        );
        assert!(live.exists());
        assert!(!orphan.exists());
        assert!(non_jpg.exists());

        let _ = fs::remove_dir_all(root);
    }
}
