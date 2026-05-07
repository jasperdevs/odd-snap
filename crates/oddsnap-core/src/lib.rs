pub mod capabilities;
pub mod filename_template;
pub mod history;
pub mod jobs;
pub mod media;
pub mod native_ui;
pub mod settings;

pub use capabilities::{CapabilityState, PlatformCapabilities, PlatformCapability};
pub use filename_template::{
    build_available_capture_path, format_file_name_template, normalize_file_name_template,
    DEFAULT_FILE_NAME_TEMPLATE, LEGACY_DEFAULT_FILE_NAME_TEMPLATE,
};
pub use history::{
    default_history_path, HistoryEntry, HistoryIndex, HistoryKind, HistoryStore, HistoryStoreError,
};
pub use jobs::{AppJobArea, AppJobSnapshot};
pub use media::{
    build_recording_output_args, build_video_thumbnail_args, build_video_thumbnail_fallback_args,
    discover_ffmpeg_tools, discover_ffmpeg_tools_in_locations, discover_ffmpeg_tools_in_path,
    FfmpegRecordingRequest, FfmpegThumbnailRequest, FfmpegTools,
};
pub use native_ui::{NativeMaterial, NativeUiProfile};
pub use settings::{
    default_settings_path, AppSettings, CaptureImageFormat, DefaultCaptureMode, RecordingFormat,
    RecordingQuality, SettingDefinition, SettingsOptionDefinition, SettingsPageDefinition,
    SettingsSectionDefinition, SettingsStore, SettingsStoreError, SettingsValueKind, ToastPosition,
};
