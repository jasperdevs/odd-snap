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
    HistoryStoreError, OcrHistoryEntry,
};
pub use image_search::{
    build_image_search_text, default_image_search_index_path, describe_image_search_match,
    history_entry_can_be_image_indexed, image_search_diagnostics_text,
    image_search_record_diagnostics, image_search_record_matches_history_entry,
    image_search_status_text, normalize_image_search_text,
    pending_image_search_record_from_history_entry, rank_image_search_items,
    retain_indexed_image_paths, score_image_search, score_normalized_image_search,
    score_pre_normalized_image_search, upsert_image_search_record, ImageSearchIndex,
    ImageSearchIndexRecord, ImageSearchIndexStore, ImageSearchIndexStoreError, ImageSearchOcrState,
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
    build_curl_upload_request, build_curl_upload_request_with_settings,
    build_dropbox_curl_upload_plan, build_google_drive_curl_upload_plan,
    build_google_drive_permission_request, build_google_drive_resumable_upload_request,
    build_google_lens_url, build_onedrive_create_link_request, build_onedrive_curl_upload_plan,
    dropbox_shared_link_already_exists, normalize_ai_chat_upload_destination,
    parse_curl_upload_output, parse_curl_upload_output_with_settings,
    parse_curl_upload_output_with_success_url, parse_dropbox_list_shared_links_output,
    parse_dropbox_shared_link_output, parse_dropbox_upload_ack,
    parse_google_drive_permission_output, parse_google_drive_resumable_session_output,
    parse_google_drive_upload_file_id, parse_onedrive_create_link_output,
    parse_onedrive_upload_item_id, parse_upload_response, parse_upload_response_with_settings,
    should_upload_media, temporary_host_fallbacks, upload_preflight_for_explicit_media,
    upload_preflight_for_media, AiChatProvider, CurlUploadRequest, DropboxCurlUploadPlan,
    GoogleDriveCurlUploadPlan, GoogleDriveUploadPlanKind, OneDriveCurlUploadPlan,
    UploadDestination, UploadPreflight, UploadSettings, UploadSuccess,
};
