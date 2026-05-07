use oddsnap_core::HistoryKind;

use crate::CaptureHistoryEntry;

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
