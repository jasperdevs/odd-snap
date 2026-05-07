pub mod capabilities;
pub mod filename_template;
pub mod history;
pub mod image_search;
pub mod jobs;
pub mod media;
pub mod native_ui;
pub mod ocr;
pub mod settings;
pub mod translation;
pub mod upload;

pub use capabilities::{CapabilityState, PlatformCapabilities, PlatformCapability};
pub use filename_template::{
    build_available_capture_path, format_file_name_template, normalize_file_name_template,
    DEFAULT_FILE_NAME_TEMPLATE, LEGACY_DEFAULT_FILE_NAME_TEMPLATE,
};
pub use history::{
    default_history_path, ColorHistoryEntry, HistoryEntry, HistoryIndex, HistoryKind, HistoryStore,
    HistoryStoreError,
};
pub use image_search::{
    build_image_search_text, describe_image_search_match, image_search_diagnostics_text,
    image_search_record_diagnostics, image_search_status_text, normalize_image_search_text,
    rank_image_search_items, score_image_search, score_normalized_image_search,
    score_pre_normalized_image_search, ImageSearchIndexRecord, ImageSearchOcrState,
    ImageSearchRecordDiagnostics, ImageSearchSources,
};
pub use jobs::{AppJobArea, AppJobSnapshot};
pub use media::{
    build_recording_output_args, build_video_thumbnail_args, build_video_thumbnail_fallback_args,
    discover_ffmpeg_tools, discover_ffmpeg_tools_in_locations, discover_ffmpeg_tools_in_path,
    FfmpegRecordingRequest, FfmpegThumbnailRequest, FfmpegTools,
};
pub use native_ui::{NativeMaterial, NativeUiProfile};
pub use ocr::{format_recognized_ocr_text, OcrLineLayout};
pub use settings::{
    default_settings_path, AppSettings, CaptureImageFormat, DefaultCaptureMode, RecordingFormat,
    RecordingQuality, SettingDefinition, SettingsOptionDefinition, SettingsPageDefinition,
    SettingsSectionDefinition, SettingsStore, SettingsStoreError, SettingsValueKind, ToastPosition,
};
pub use translation::{
    normalize_supported_translation_language, resolve_translation_source_language,
    resolve_translation_target_language, translation_configuration_error,
    translation_language_name, TranslationModel, SUPPORTED_TRANSLATION_LANGUAGES,
};
pub use upload::{
    build_curl_upload_request, build_curl_upload_request_with_settings, build_google_lens_url,
    normalize_ai_chat_upload_destination, parse_curl_upload_output, parse_upload_response,
    should_upload_media, temporary_host_fallbacks, upload_preflight_for_explicit_media,
    upload_preflight_for_media, AiChatProvider, CurlUploadRequest, UploadDestination,
    UploadPreflight, UploadSettings, UploadSuccess,
};
