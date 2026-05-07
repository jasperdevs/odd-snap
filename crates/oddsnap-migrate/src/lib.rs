use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use chrono::{DateTime, NaiveDateTime, Utc};
use oddsnap_core::{
    normalize_file_name_template, AppSettings, CaptureImageFormat, CodeHistoryEntry,
    ColorHistoryEntry, DefaultCaptureMode, HistoryEntry, HistoryIndex, HistoryKind,
    OcrHistoryEntry, RecordingFormat, RecordingQuality, ToastPosition,
};
use rusqlite::Connection;
use serde_json::Value;
use thiserror::Error;

#[derive(Debug, Error)]
pub enum MigrationError {
    #[error("legacy settings file does not exist: {0}")]
    MissingSettings(PathBuf),
    #[error("failed to read legacy settings file {path}: {source}")]
    ReadSettings {
        path: PathBuf,
        source: std::io::Error,
    },
    #[error("failed to parse legacy settings JSON {path}: {source}")]
    ParseSettings {
        path: PathBuf,
        source: serde_json::Error,
    },
    #[error("failed to read legacy history file {path}: {source}")]
    ReadHistory {
        path: PathBuf,
        source: std::io::Error,
    },
    #[error("failed to parse legacy history JSON {path}: {source}")]
    ParseHistory {
        path: PathBuf,
        source: serde_json::Error,
    },
    #[error("failed to read legacy history database {path}: {source}")]
    ReadHistoryDatabase {
        path: PathBuf,
        source: rusqlite::Error,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LegacyOddSnapPaths {
    pub roaming_dir: PathBuf,
    pub settings_path: PathBuf,
    pub history_dir: PathBuf,
    pub current_history_dir: PathBuf,
    pub current_history_database_path: PathBuf,
    pub current_history_index_path: PathBuf,
}

#[derive(Debug, Clone, PartialEq)]
pub struct LegacySettingsImport {
    pub source_path: PathBuf,
    pub top_level_key_count: usize,
    pub raw: Value,
}

impl LegacyOddSnapPaths {
    pub fn from_roaming_dir(roaming_dir: impl Into<PathBuf>) -> Self {
        let roaming_dir = roaming_dir.into();
        Self {
            settings_path: roaming_dir.join("settings.json"),
            history_dir: roaming_dir.join("history"),
            current_history_dir: default_current_history_dir(),
            current_history_database_path: default_current_history_dir().join("history.db"),
            current_history_index_path: default_current_history_dir().join("index.json"),
            roaming_dir,
        }
    }

    pub fn from_current_environment() -> Option<Self> {
        #[cfg(target_os = "windows")]
        {
            env::var_os("APPDATA")
                .map(PathBuf::from)
                .map(|path| Self::from_roaming_dir(path.join("OddSnap")))
        }

        #[cfg(target_os = "macos")]
        {
            env::var_os("HOME").map(|home| {
                Self::from_roaming_dir(
                    PathBuf::from(home)
                        .join("Library")
                        .join("Application Support")
                        .join("OddSnap"),
                )
            })
        }

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        {
            let base = env::var_os("XDG_CONFIG_HOME")
                .map(PathBuf::from)
                .or_else(|| env::var_os("HOME").map(|home| PathBuf::from(home).join(".config")))?;
            Some(Self::from_roaming_dir(base.join("OddSnap")))
        }
    }
}

fn default_current_history_dir() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(profile) = env::var_os("USERPROFILE") {
            return PathBuf::from(profile)
                .join("Pictures")
                .join("OddSnap History");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        if let Some(home) = env::var_os("HOME") {
            return PathBuf::from(home).join("Pictures").join("OddSnap History");
        }
    }

    env::temp_dir().join("OddSnap History")
}

pub fn read_legacy_settings(
    path: impl AsRef<Path>,
) -> Result<LegacySettingsImport, MigrationError> {
    let path = path.as_ref().to_path_buf();
    if !path.exists() {
        return Err(MigrationError::MissingSettings(path));
    }

    let json = fs::read_to_string(&path).map_err(|source| MigrationError::ReadSettings {
        path: path.clone(),
        source,
    })?;
    let raw: Value =
        serde_json::from_str(&json).map_err(|source| MigrationError::ParseSettings {
            path: path.clone(),
            source,
        })?;
    let top_level_key_count = raw.as_object().map_or(0, serde_json::Map::len);

    Ok(LegacySettingsImport {
        source_path: path,
        top_level_key_count,
        raw,
    })
}

pub fn import_app_settings(import: &LegacySettingsImport) -> AppSettings {
    let mut settings = AppSettings::default();

    if let Some(save_directory) = import.raw.get("SaveDirectory").and_then(Value::as_str) {
        if !save_directory.trim().is_empty() {
            settings.capture_output_directory = Some(PathBuf::from(save_directory));
        }
    }

    if let Some(save_history) = import.raw.get("SaveHistory").and_then(Value::as_bool) {
        settings.save_history = save_history;
    }

    if let Some(after_capture) = import.raw.get("AfterCapture") {
        settings.copy_captures_to_clipboard = legacy_after_capture_copies(after_capture);
    }

    if let Some(capture_image_format) = import.raw.get("CaptureImageFormat") {
        settings.capture_image_format = legacy_capture_image_format(capture_image_format);
    }

    if let Some(jpeg_quality) = import.raw.get("JpegQuality").and_then(Value::as_u64) {
        settings.jpeg_quality = jpeg_quality.clamp(1, 100) as u8;
    }

    if let Some(save_in_monthly_folders) = import
        .raw
        .get("SaveInMonthlyFolders")
        .and_then(Value::as_bool)
    {
        settings.save_in_monthly_folders = save_in_monthly_folders;
    }

    if let Some(file_name_template) = import.raw.get("FileNameTemplate").and_then(Value::as_str) {
        settings.file_name_template = normalize_file_name_template(file_name_template);
    }

    if let Some(recording_format) = import.raw.get("RecordingFormat") {
        settings.recording_format = legacy_recording_format(recording_format);
    }

    if let Some(recording_quality) = import.raw.get("RecordingQuality") {
        settings.recording_quality = legacy_recording_quality(recording_quality);
    }

    if let Some(recording_fps) = legacy_fps(import.raw.get("RecordingFps")) {
        settings.recording_fps = recording_fps;
    }

    if let Some(gif_fps) = legacy_fps(import.raw.get("GifFps")) {
        settings.gif_fps = gif_fps;
    }

    if let Some(record_microphone) = import.raw.get("RecordMicrophone").and_then(Value::as_bool) {
        settings.record_microphone = record_microphone;
    }

    if let Some(record_desktop_audio) = import
        .raw
        .get("RecordDesktopAudio")
        .and_then(Value::as_bool)
    {
        settings.record_desktop_audio = record_desktop_audio;
    }

    if let Some(microphone_device_id) =
        legacy_non_empty_string(import.raw.get("MicrophoneDeviceId"))
    {
        settings.microphone_device_id = Some(microphone_device_id);
    }

    if let Some(desktop_audio_device_id) =
        legacy_non_empty_string(import.raw.get("DesktopAudioDeviceId"))
    {
        settings.desktop_audio_device_id = Some(desktop_audio_device_id);
    }

    if let Some(capture_hotkey) = legacy_hotkey(
        import.raw.get("HotkeyModifiers"),
        import.raw.get("HotkeyKey"),
    ) {
        settings.capture_hotkey = capture_hotkey;
    }

    settings.recording_hotkey = legacy_hotkey(
        import.raw.get("GifHotkeyModifiers"),
        import.raw.get("GifHotkeyKey"),
    );

    if let Some(start_with_windows) = import.raw.get("StartWithWindows").and_then(Value::as_bool) {
        settings.start_with_windows = start_with_windows;
    }

    if let Some(auto_check_for_updates) = import
        .raw
        .get("AutoCheckForUpdates")
        .and_then(Value::as_bool)
    {
        settings.auto_check_for_updates = auto_check_for_updates;
    }

    if let Some(capture_delay_seconds) = import
        .raw
        .get("CaptureDelaySeconds")
        .and_then(Value::as_u64)
    {
        settings.capture_delay_seconds = capture_delay_seconds.min(60) as u32;
    }

    if let Some(mute_sounds) = import.raw.get("MuteSounds").and_then(Value::as_bool) {
        settings.mute_sounds = mute_sounds;
    }

    if let Some(disable_animations) = import.raw.get("DisableAnimations").and_then(Value::as_bool) {
        settings.disable_animations = disable_animations;
    }

    if let Some(ui_scale) = import.raw.get("UiScale").and_then(Value::as_f64) {
        settings.ui_scale = ui_scale.clamp(0.8, 1.4);
    }

    if let Some(show_crosshair_guides) = import
        .raw
        .get("ShowCrosshairGuides")
        .and_then(Value::as_bool)
    {
        settings.show_crosshair_guides = show_crosshair_guides;
    }

    if let Some(show_cursor) = import.raw.get("ShowCursor").and_then(Value::as_bool) {
        settings.show_cursor = show_cursor;
    }

    if let Some(show_capture_magnifier) = import
        .raw
        .get("ShowCaptureMagnifier")
        .and_then(Value::as_bool)
    {
        settings.show_capture_magnifier = show_capture_magnifier;
    }

    if let Some(overlay_capture_all_monitors) = import
        .raw
        .get("OverlayCaptureAllMonitors")
        .and_then(Value::as_bool)
    {
        settings.overlay_capture_all_monitors = overlay_capture_all_monitors;
    }

    if let Some(detect_windows) = import.raw.get("DetectWindows").and_then(Value::as_bool) {
        settings.detect_windows = detect_windows;
    }

    if let Some(default_capture_mode) = import.raw.get("DefaultCaptureMode") {
        settings.default_capture_mode = legacy_default_capture_mode(default_capture_mode);
    }

    if let Some(last_capture_mode) = import.raw.get("LastCaptureMode") {
        settings.last_capture_mode = legacy_default_capture_mode(last_capture_mode);
    }

    if let Some(toast_position) = import.raw.get("ToastPosition") {
        settings.toast_position = legacy_toast_position(toast_position);
    }

    settings.toast_button_layout = import.raw.get("ToastButtons").cloned();

    settings.ocr_hotkey = legacy_hotkey(
        import.raw.get("OcrHotkeyModifiers"),
        import.raw.get("OcrHotkeyKey"),
    );
    settings.picker_hotkey = legacy_hotkey(
        import.raw.get("PickerHotkeyModifiers"),
        import.raw.get("PickerHotkeyKey"),
    );
    settings.scan_hotkey = legacy_hotkey(
        import.raw.get("ScanHotkeyModifiers"),
        import.raw.get("ScanHotkeyKey"),
    );
    settings.sticker_hotkey = legacy_hotkey(
        import.raw.get("StickerHotkeyModifiers"),
        import.raw.get("StickerHotkeyKey"),
    );
    settings.upscale_hotkey = legacy_hotkey(
        import.raw.get("UpscaleHotkeyModifiers"),
        import.raw.get("UpscaleHotkeyKey"),
    );
    settings.center_hotkey = legacy_hotkey(
        import.raw.get("CenterHotkeyModifiers"),
        import.raw.get("CenterHotkeyKey"),
    );
    settings.fullscreen_hotkey = legacy_hotkey(
        import.raw.get("FullscreenHotkeyModifiers"),
        import.raw.get("FullscreenHotkeyKey"),
    );
    settings.active_window_hotkey = legacy_hotkey(
        import.raw.get("ActiveWindowHotkeyModifiers"),
        import.raw.get("ActiveWindowHotkeyKey"),
    );
    settings.ruler_hotkey = legacy_hotkey(
        import.raw.get("RulerHotkeyModifiers"),
        import.raw.get("RulerHotkeyKey"),
    );
    settings.scroll_capture_hotkey = legacy_hotkey(
        import.raw.get("ScrollCaptureHotkeyModifiers"),
        import.raw.get("ScrollCaptureHotkeyKey"),
    );
    settings.ai_redirect_hotkey = legacy_hotkey(
        import.raw.get("AiRedirectHotkeyModifiers"),
        import.raw.get("AiRedirectHotkeyKey"),
    );

    import_string(
        &import.raw,
        "OcrLanguageTag",
        &mut settings.ocr_language_tag,
    );
    import_u32(
        &import.raw,
        "OcrModelQuality",
        &mut settings.ocr_model_quality,
    );
    import_string(
        &import.raw,
        "OcrDefaultTranslateFrom",
        &mut settings.ocr_default_translate_from,
    );
    import_string(
        &import.raw,
        "OcrDefaultTranslateTo",
        &mut settings.ocr_default_translate_to,
    );
    settings.google_translate_api_key =
        legacy_non_empty_string(import.raw.get("GoogleTranslateApiKey"));
    import_bool(
        &import.raw,
        "TranslationRuntimeInstalled",
        &mut settings.translation_runtime_installed,
    );
    import_u32(
        &import.raw,
        "TranslationModel",
        &mut settings.translation_model,
    );
    import_bool(
        &import.raw,
        "AnnotationStrokeShadow",
        &mut settings.annotation_stroke_shadow,
    );
    import_bool(&import.raw, "SaveToFile", &mut settings.save_to_file);
    import_bool(
        &import.raw,
        "AskForFileNameOnSave",
        &mut settings.ask_for_file_name_on_save,
    );
    import_bool(
        &import.raw,
        "StyleScreenshots",
        &mut settings.style_screenshots,
    );
    import_bool(
        &import.raw,
        "AddScreenshotShadow",
        &mut settings.add_screenshot_shadow,
    );
    import_bool(
        &import.raw,
        "AddScreenshotStroke",
        &mut settings.add_screenshot_stroke,
    );
    import_u32(
        &import.raw,
        "CaptureMaxLongEdge",
        &mut settings.capture_max_long_edge,
    );
    import_enumish_string(
        &import.raw,
        "WindowDetection",
        &mut settings.window_detection,
    );
    import_enumish_string(
        &import.raw,
        "CaptureDockSide",
        &mut settings.capture_dock_side,
    );
    import_enumish_string(
        &import.raw,
        "ScrollingCaptureMode",
        &mut settings.scrolling_capture_mode,
    );
    import_string(
        &import.raw,
        "InterfaceLanguage",
        &mut settings.interface_language,
    );
    import_bool(
        &import.raw,
        "CompressHistory",
        &mut settings.compress_history,
    );
    import_bool(
        &import.raw,
        "HasCompletedSetup",
        &mut settings.has_completed_setup,
    );
    import_enumish_string(
        &import.raw,
        "CenterSelectionAspectRatio",
        &mut settings.center_selection_aspect_ratio,
    );
    import_bool(
        &import.raw,
        "ShowToolNumberBadges",
        &mut settings.show_tool_number_badges,
    );
    import_enumish_string(
        &import.raw,
        "HistoryRetention",
        &mut settings.history_retention,
    );
    import_u32(
        &import.raw,
        "ImageSearchSources",
        &mut settings.image_search_sources,
    );
    import_bool(
        &import.raw,
        "ShowImageSearchBar",
        &mut settings.show_image_search_bar,
    );
    import_bool(
        &import.raw,
        "ImageSearchExactMatch",
        &mut settings.image_search_exact_match,
    );
    import_bool(
        &import.raw,
        "ShowImageSearchDiagnostics",
        &mut settings.show_image_search_diagnostics,
    );
    import_bool(
        &import.raw,
        "AutoIndexImages",
        &mut settings.auto_index_images,
    );
    import_bool(
        &import.raw,
        "AutoUploadScreenshots",
        &mut settings.auto_upload_screenshots,
    );
    import_bool(
        &import.raw,
        "AutoUploadGifs",
        &mut settings.auto_upload_gifs,
    );
    import_bool(
        &import.raw,
        "AutoUploadVideos",
        &mut settings.auto_upload_videos,
    );
    import_enumish_string(
        &import.raw,
        "ImageUploadDestination",
        &mut settings.image_upload_destination,
    );
    settings.image_upload_settings = import.raw.get("ImageUploadSettings").cloned();
    settings.sticker_upload_settings = import.raw.get("StickerUploadSettings").cloned();
    settings.upscale_upload_settings = import.raw.get("UpscaleUploadSettings").cloned();
    import_f64(
        &import.raw,
        "ToastDurationSeconds",
        &mut settings.toast_duration_seconds,
    );
    import_bool(
        &import.raw,
        "ToastFadeOutEnabled",
        &mut settings.toast_fade_out_enabled,
    );
    import_f64(
        &import.raw,
        "ToastFadeOutSeconds",
        &mut settings.toast_fade_out_seconds,
    );
    import_bool(
        &import.raw,
        "AutoPinPreviews",
        &mut settings.auto_pin_previews,
    );
    if let Some(open_with_apps) = import.raw.get("OpenWithApps").and_then(Value::as_object) {
        settings.open_with_apps = open_with_apps
            .iter()
            .filter_map(|(key, value)| {
                value
                    .as_str()
                    .filter(|value| !value.trim().is_empty())
                    .map(|value| (key.clone(), value.to_string()))
            })
            .collect();
    }
    import_enumish_string(&import.raw, "SoundPack", &mut settings.sound_pack);
    if let Some(enabled_tools) = import.raw.get("EnabledTools").and_then(Value::as_array) {
        let tools: Vec<String> = enabled_tools
            .iter()
            .filter_map(Value::as_str)
            .map(str::trim)
            .filter(|tool| !tool.is_empty())
            .map(str::to_string)
            .collect();
        settings.enabled_tools = Some(tools);
    }
    if let Some(tool_hotkeys) = import.raw.get("ToolHotkeys").and_then(Value::as_object) {
        settings.tool_hotkeys = tool_hotkeys
            .iter()
            .filter_map(|(tool, value)| {
                let values = value.as_array()?;
                let modifiers = values.first()?.as_u64()? as u32;
                let key = values.get(1)?.as_u64()? as u32;
                Some((tool.clone(), vec![modifiers, key]))
            })
            .collect();
    }

    settings
}

pub fn import_existing_history(paths: &LegacyOddSnapPaths) -> Result<HistoryIndex, MigrationError> {
    if paths.current_history_database_path.exists() {
        let mut index = import_history_database(&paths.current_history_database_path)?;
        if index.colors.is_empty() {
            index.colors = import_first_color_history_json(paths)?;
        }
        if index.ocr_entries.is_empty() {
            index.ocr_entries = import_first_ocr_history_json(paths)?;
        }
        if history_index_has_content(&index) {
            return Ok(index);
        }
    }

    let json_paths = [
        paths.current_history_index_path.clone(),
        paths.history_dir.join("index.json"),
    ];
    for path in json_paths {
        if path.exists() {
            let mut index = import_history_json(&path)?;
            if index.colors.is_empty() {
                index.colors = import_first_color_history_json(paths)?;
            }
            if index.ocr_entries.is_empty() {
                index.ocr_entries = import_first_ocr_history_json(paths)?;
            }
            if history_index_has_content(&index) {
                return Ok(index);
            }
        }
    }

    let colors = import_first_color_history_json(paths)?;
    let ocr_entries = import_first_ocr_history_json(paths)?;
    if !colors.is_empty() || !ocr_entries.is_empty() {
        return Ok(HistoryIndex {
            entries: Vec::new(),
            colors,
            ocr_entries,
            code_entries: Vec::new(),
        });
    }

    Ok(HistoryIndex::default())
}

fn history_index_has_content(index: &HistoryIndex) -> bool {
    !index.entries.is_empty()
        || !index.colors.is_empty()
        || !index.ocr_entries.is_empty()
        || !index.code_entries.is_empty()
}

fn import_history_database(path: &Path) -> Result<HistoryIndex, MigrationError> {
    let connection =
        Connection::open(path).map_err(|source| MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        })?;
    let mut entries = Vec::new();

    if sqlite_table_exists(&connection, "history_entries").map_err(|source| {
        MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        }
    })? {
        let mut statement = connection
            .prepare(
                "SELECT file_name, file_path, width, height, file_size_bytes, kind, upload_url, upload_provider, upload_error \
                 FROM history_entries ORDER BY captured_at_ticks DESC",
            )
            .map_err(|source| MigrationError::ReadHistoryDatabase {
                path: path.to_path_buf(),
                source,
            })?;

        let rows = statement
            .query_map([], |row| {
                let file_name: String = row.get(0)?;
                let file_path: String = row.get(1)?;
                let width: i64 = row.get(2)?;
                let height: i64 = row.get(3)?;
                let file_size_bytes: i64 = row.get(4)?;
                let kind: i64 = row.get(5)?;
                let upload_url: Option<String> = row.get(6)?;
                let upload_provider: Option<String> = row.get(7)?;
                let upload_error: Option<String> = row.get(8)?;
                Ok(legacy_history_entry(LegacyHistoryRecord {
                    file_name,
                    file_path: PathBuf::from(file_path),
                    width,
                    height,
                    file_size_bytes,
                    kind: legacy_history_kind_from_i64(kind),
                    upload_url,
                    upload_provider,
                    upload_error,
                }))
            })
            .map_err(|source| MigrationError::ReadHistoryDatabase {
                path: path.to_path_buf(),
                source,
            })?;

        for row in rows {
            let entry = row.map_err(|source| MigrationError::ReadHistoryDatabase {
                path: path.to_path_buf(),
                source,
            })?;
            if is_supported_history_file(&entry.file_path) && entry.file_path.exists() {
                entries.push(entry);
            }
        }
    }

    let colors = import_color_history_database(&connection).map_err(|source| {
        MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        }
    })?;
    let ocr_entries = import_ocr_history_database(&connection).map_err(|source| {
        MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        }
    })?;
    let code_entries = import_code_history_database(&connection).map_err(|source| {
        MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        }
    })?;

    Ok(HistoryIndex {
        entries,
        colors,
        ocr_entries,
        code_entries,
    })
}

fn sqlite_table_exists(connection: &Connection, table_name: &str) -> Result<bool, rusqlite::Error> {
    let table_count: i64 = connection.query_row(
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?1",
        [table_name],
        |row| row.get(0),
    )?;
    Ok(table_count > 0)
}

fn import_color_history_database(
    connection: &Connection,
) -> Result<Vec<ColorHistoryEntry>, rusqlite::Error> {
    if !sqlite_table_exists(connection, "color_entries")? {
        return Ok(Vec::new());
    }

    let mut statement = connection
        .prepare("SELECT hex FROM color_entries ORDER BY captured_at_ticks DESC LIMIT 200")?;
    let rows = statement.query_map([], |row| row.get::<_, String>(0))?;
    let mut colors = Vec::new();
    for row in rows {
        if let Some(hex) = normalize_color_hex(&row?) {
            colors.push(ColorHistoryEntry::new(hex));
        }
    }
    Ok(colors)
}

fn import_ocr_history_database(
    connection: &Connection,
) -> Result<Vec<OcrHistoryEntry>, rusqlite::Error> {
    if !sqlite_table_exists(connection, "ocr_entries")? {
        return Ok(Vec::new());
    }

    let mut statement = connection.prepare(
        "SELECT text, captured_at_ticks FROM ocr_entries ORDER BY captured_at_ticks DESC LIMIT 500",
    )?;
    let rows = statement.query_map([], |row| {
        Ok((row.get::<_, String>(0)?, row.get::<_, i64>(1)?))
    })?;
    let mut entries = Vec::new();
    for row in rows {
        let (text, captured_at_ticks) = row?;
        let text = text.trim();
        if text.is_empty() {
            continue;
        }
        entries.push(OcrHistoryEntry {
            text: text.to_string(),
            captured_at_unix_ms: dotnet_datetime_binary_to_unix_ms(captured_at_ticks)
                .unwrap_or_default(),
        });
    }
    Ok(entries)
}

fn import_code_history_database(
    connection: &Connection,
) -> Result<Vec<CodeHistoryEntry>, rusqlite::Error> {
    if !sqlite_table_exists(connection, "code_entries")? {
        return Ok(Vec::new());
    }

    let mut statement = connection.prepare(
        "SELECT text, format, captured_at_ticks FROM code_entries ORDER BY captured_at_ticks DESC LIMIT 200",
    )?;
    let rows = statement.query_map([], |row| {
        Ok((
            row.get::<_, String>(0)?,
            row.get::<_, String>(1)?,
            row.get::<_, i64>(2)?,
        ))
    })?;
    let mut entries = Vec::new();
    for row in rows {
        let (text, format, captured_at_ticks) = row?;
        let text = text.trim();
        let format = format.trim();
        if text.is_empty() {
            continue;
        }
        entries.push(CodeHistoryEntry {
            text: text.to_string(),
            format: if format.is_empty() {
                "UNKNOWN".into()
            } else {
                format.to_string()
            },
            captured_at_unix_ms: dotnet_datetime_binary_to_unix_ms(captured_at_ticks)
                .unwrap_or_default(),
        });
    }
    Ok(entries)
}

fn import_history_json(path: &Path) -> Result<HistoryIndex, MigrationError> {
    let json = fs::read_to_string(path).map_err(|source| MigrationError::ReadHistory {
        path: path.to_path_buf(),
        source,
    })?;
    let raw: Vec<Value> =
        serde_json::from_str(&json).map_err(|source| MigrationError::ParseHistory {
            path: path.to_path_buf(),
            source,
        })?;

    let entries = raw
        .into_iter()
        .filter_map(|entry| {
            let file_path = entry.get("FilePath")?.as_str().map(PathBuf::from)?;
            if !file_path.exists() || !is_supported_history_file(&file_path) {
                return None;
            }

            Some(legacy_history_entry(LegacyHistoryRecord {
                file_name: entry
                    .get("FileName")
                    .and_then(Value::as_str)
                    .unwrap_or_else(|| {
                        file_path
                            .file_name()
                            .and_then(|name| name.to_str())
                            .unwrap_or("")
                    })
                    .to_string(),
                file_path,
                width: entry
                    .get("Width")
                    .and_then(Value::as_i64)
                    .unwrap_or_default(),
                height: entry
                    .get("Height")
                    .and_then(Value::as_i64)
                    .unwrap_or_default(),
                file_size_bytes: entry
                    .get("FileSizeBytes")
                    .and_then(Value::as_i64)
                    .unwrap_or_default(),
                kind: legacy_history_kind_from_value(entry.get("Kind")),
                upload_url: entry
                    .get("UploadUrl")
                    .and_then(Value::as_str)
                    .map(str::to_string),
                upload_provider: entry
                    .get("UploadProvider")
                    .and_then(Value::as_str)
                    .map(str::to_string),
                upload_error: entry
                    .get("UploadError")
                    .and_then(Value::as_str)
                    .map(str::to_string),
            }))
        })
        .collect();

    Ok(HistoryIndex {
        entries,
        colors: Vec::new(),
        ocr_entries: Vec::new(),
        code_entries: Vec::new(),
    })
}

fn import_first_color_history_json(
    paths: &LegacyOddSnapPaths,
) -> Result<Vec<ColorHistoryEntry>, MigrationError> {
    for path in [
        paths.current_history_dir.join("color_index.json"),
        paths.history_dir.join("color_index.json"),
    ] {
        if path.exists() {
            let colors = import_color_history_json(&path)?;
            if !colors.is_empty() {
                return Ok(colors);
            }
        }
    }

    Ok(Vec::new())
}

fn import_first_ocr_history_json(
    paths: &LegacyOddSnapPaths,
) -> Result<Vec<OcrHistoryEntry>, MigrationError> {
    for path in [
        paths.current_history_dir.join("ocr_index.json"),
        paths.history_dir.join("ocr_index.json"),
    ] {
        if path.exists() {
            let entries = import_ocr_history_json(&path)?;
            if !entries.is_empty() {
                return Ok(entries);
            }
        }
    }

    Ok(Vec::new())
}

fn import_ocr_history_json(path: &Path) -> Result<Vec<OcrHistoryEntry>, MigrationError> {
    let json = fs::read_to_string(path).map_err(|source| MigrationError::ReadHistory {
        path: path.to_path_buf(),
        source,
    })?;
    let raw: Vec<Value> =
        serde_json::from_str(&json).map_err(|source| MigrationError::ParseHistory {
            path: path.to_path_buf(),
            source,
        })?;

    Ok(raw
        .into_iter()
        .filter_map(|entry| {
            let text = entry
                .get("Text")
                .or_else(|| entry.get("text"))?
                .as_str()?
                .trim();
            if text.is_empty() {
                return None;
            }
            Some(OcrHistoryEntry {
                text: text.to_string(),
                captured_at_unix_ms: legacy_captured_at_unix_ms(&entry).unwrap_or_default(),
            })
        })
        .take(500)
        .collect())
}

fn import_color_history_json(path: &Path) -> Result<Vec<ColorHistoryEntry>, MigrationError> {
    let json = fs::read_to_string(path).map_err(|source| MigrationError::ReadHistory {
        path: path.to_path_buf(),
        source,
    })?;
    let raw: Vec<Value> =
        serde_json::from_str(&json).map_err(|source| MigrationError::ParseHistory {
            path: path.to_path_buf(),
            source,
        })?;

    Ok(raw
        .into_iter()
        .filter_map(|entry| {
            let hex = entry.get("Hex").or_else(|| entry.get("hex"))?.as_str()?;
            normalize_color_hex(hex).map(ColorHistoryEntry::new)
        })
        .take(200)
        .collect())
}

fn legacy_captured_at_unix_ms(entry: &Value) -> Option<u64> {
    for key in ["CapturedAtUnixMs", "captured_at_unix_ms"] {
        if let Some(value) = entry.get(key).and_then(Value::as_u64) {
            return Some(value);
        }
    }

    for key in ["CapturedAtTicks", "captured_at_ticks"] {
        if let Some(value) = entry.get(key).and_then(Value::as_i64) {
            return dotnet_datetime_binary_to_unix_ms(value);
        }
    }

    entry
        .get("CapturedAt")
        .or_else(|| entry.get("captured_at"))
        .and_then(Value::as_str)
        .and_then(parse_legacy_datetime_to_unix_ms)
}

fn dotnet_datetime_binary_to_unix_ms(value: i64) -> Option<u64> {
    const DOTNET_TICKS_PER_MILLISECOND: u64 = 10_000;
    const DOTNET_UNIX_EPOCH_TICKS: u64 = 621_355_968_000_000_000;
    const DOTNET_MAX_TICKS: u64 = 3_155_378_975_999_999_999;
    const DOTNET_TICKS_MASK: u64 = 0x3fff_ffff_ffff_ffff;
    const DOTNET_KIND_MASK: u64 = 0xc000_0000_0000_0000;
    const DOTNET_LOCAL_MASK: u64 = 0x8000_0000_0000_0000;
    const DOTNET_TICKS_CEILING: u64 = 0x4000_0000_0000_0000;

    let data = value as u64;
    let kind = data & DOTNET_KIND_MASK;
    let mut ticks = data & DOTNET_TICKS_MASK;

    if matches!(kind, DOTNET_LOCAL_MASK | DOTNET_KIND_MASK) && ticks > DOTNET_MAX_TICKS {
        ticks = ticks.saturating_sub(DOTNET_TICKS_CEILING);
    }

    if ticks < DOTNET_UNIX_EPOCH_TICKS {
        return None;
    }

    Some((ticks - DOTNET_UNIX_EPOCH_TICKS) / DOTNET_TICKS_PER_MILLISECOND)
}

fn parse_legacy_datetime_to_unix_ms(value: &str) -> Option<u64> {
    let value = value.trim();
    if value.is_empty() {
        return None;
    }

    if let Ok(datetime) = DateTime::parse_from_rfc3339(value) {
        return chrono_datetime_to_unix_ms(datetime.with_timezone(&Utc));
    }

    for format in [
        "%Y-%m-%dT%H:%M:%S%.f",
        "%Y-%m-%d %H:%M:%S%.f",
        "%Y-%m-%dT%H:%M:%S",
        "%Y-%m-%d %H:%M:%S",
    ] {
        if let Ok(datetime) = NaiveDateTime::parse_from_str(value, format) {
            return chrono_datetime_to_unix_ms(DateTime::<Utc>::from_naive_utc_and_offset(
                datetime, Utc,
            ));
        }
    }

    None
}

fn chrono_datetime_to_unix_ms(datetime: DateTime<Utc>) -> Option<u64> {
    let timestamp = datetime.timestamp_millis();
    if timestamp < 0 {
        return None;
    }
    Some(timestamp as u64)
}

fn normalize_color_hex(hex: &str) -> Option<String> {
    let value = hex.trim().trim_start_matches('#');
    if value.len() != 6 || !value.chars().all(|ch| ch.is_ascii_hexdigit()) {
        return None;
    }
    Some(value.to_ascii_uppercase())
}

struct LegacyHistoryRecord {
    file_name: String,
    file_path: PathBuf,
    width: i64,
    height: i64,
    file_size_bytes: i64,
    kind: HistoryKind,
    upload_url: Option<String>,
    upload_provider: Option<String>,
    upload_error: Option<String>,
}

fn legacy_history_entry(record: LegacyHistoryRecord) -> HistoryEntry {
    let metadata = fs::metadata(&record.file_path).ok();
    let file_size_bytes = if record.file_size_bytes > 0 {
        record.file_size_bytes as u64
    } else {
        metadata.as_ref().map_or(0, std::fs::Metadata::len)
    };
    let captured_at_unix_ms = metadata
        .and_then(|metadata| metadata.created().or_else(|_| metadata.modified()).ok())
        .map(system_time_to_unix_ms)
        .unwrap_or_default();

    HistoryEntry {
        file_name: record.file_name,
        file_path: record.file_path,
        captured_at_unix_ms,
        width: record.width.max(0) as u32,
        height: record.height.max(0) as u32,
        file_size_bytes,
        kind: record.kind,
        upload_url: record.upload_url,
        upload_provider: record.upload_provider,
        upload_error: record.upload_error,
        thumbnail_path: None,
    }
}

fn legacy_history_kind_from_value(value: Option<&Value>) -> HistoryKind {
    match value {
        Some(Value::Number(number)) => number
            .as_i64()
            .map(legacy_history_kind_from_i64)
            .unwrap_or(HistoryKind::Image),
        Some(Value::String(name)) if name.eq_ignore_ascii_case("Gif") => HistoryKind::Gif,
        Some(Value::String(name)) if name.eq_ignore_ascii_case("Sticker") => HistoryKind::Sticker,
        Some(Value::String(name)) if name.eq_ignore_ascii_case("Video") => HistoryKind::Video,
        _ => HistoryKind::Image,
    }
}

fn legacy_history_kind_from_i64(value: i64) -> HistoryKind {
    match value {
        1 => HistoryKind::Gif,
        2 => HistoryKind::Sticker,
        3 => HistoryKind::Video,
        _ => HistoryKind::Image,
    }
}

fn is_supported_history_file(path: &Path) -> bool {
    let Some(extension) = path.extension().and_then(|extension| extension.to_str()) else {
        return false;
    };
    matches!(
        extension.to_ascii_lowercase().as_str(),
        "png" | "jpg" | "jpeg" | "bmp" | "gif" | "mp4" | "webm" | "mkv"
    )
}

fn system_time_to_unix_ms(value: SystemTime) -> u64 {
    value
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64)
        .unwrap_or_default()
}

fn legacy_after_capture_copies(value: &Value) -> bool {
    match value {
        Value::Number(number) => number.as_u64().is_none_or(|index| index != 2),
        Value::String(name) => !name.eq_ignore_ascii_case("PreviewOnly"),
        _ => AppSettings::default().copy_captures_to_clipboard,
    }
}

fn legacy_capture_image_format(value: &Value) -> CaptureImageFormat {
    match value {
        Value::Number(number) => match number.as_u64() {
            Some(1) => CaptureImageFormat::Jpeg,
            Some(2) => CaptureImageFormat::Bmp,
            _ => CaptureImageFormat::Png,
        },
        Value::String(name) if name.eq_ignore_ascii_case("Jpeg") => CaptureImageFormat::Jpeg,
        Value::String(name) if name.eq_ignore_ascii_case("Bmp") => CaptureImageFormat::Bmp,
        _ => CaptureImageFormat::Png,
    }
}

fn legacy_recording_format(value: &Value) -> RecordingFormat {
    match value {
        Value::Number(number) => match number.as_u64() {
            Some(0) => RecordingFormat::Gif,
            Some(2) => RecordingFormat::WebM,
            Some(3) => RecordingFormat::Mkv,
            _ => RecordingFormat::Mp4,
        },
        Value::String(name) if name.eq_ignore_ascii_case("Gif") => RecordingFormat::Gif,
        Value::String(name) if name.eq_ignore_ascii_case("GIF") => RecordingFormat::Gif,
        Value::String(name) if name.eq_ignore_ascii_case("WebM") => RecordingFormat::WebM,
        Value::String(name) if name.eq_ignore_ascii_case("Mkv") => RecordingFormat::Mkv,
        Value::String(name) if name.eq_ignore_ascii_case("MKV") => RecordingFormat::Mkv,
        _ => RecordingFormat::Mp4,
    }
}

fn legacy_recording_quality(value: &Value) -> RecordingQuality {
    match value {
        Value::Number(number) => match number.as_u64() {
            Some(1) => RecordingQuality::P1080,
            Some(2) => RecordingQuality::P720,
            Some(3) => RecordingQuality::P480,
            _ => RecordingQuality::Original,
        },
        Value::String(name) if name.eq_ignore_ascii_case("P1080") => RecordingQuality::P1080,
        Value::String(name) if name.eq_ignore_ascii_case("1080p") => RecordingQuality::P1080,
        Value::String(name) if name.eq_ignore_ascii_case("P720") => RecordingQuality::P720,
        Value::String(name) if name.eq_ignore_ascii_case("720p") => RecordingQuality::P720,
        Value::String(name) if name.eq_ignore_ascii_case("P480") => RecordingQuality::P480,
        Value::String(name) if name.eq_ignore_ascii_case("480p") => RecordingQuality::P480,
        _ => RecordingQuality::Original,
    }
}

fn legacy_fps(value: Option<&Value>) -> Option<u32> {
    value
        .and_then(Value::as_u64)
        .map(|fps| fps.clamp(1, 240) as u32)
}

fn legacy_non_empty_string(value: Option<&Value>) -> Option<String> {
    value
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_string)
}

fn import_bool(raw: &Value, key: &str, destination: &mut bool) {
    if let Some(value) = raw.get(key).and_then(Value::as_bool) {
        *destination = value;
    }
}

fn import_string(raw: &Value, key: &str, destination: &mut String) {
    if let Some(value) = legacy_non_empty_string(raw.get(key)) {
        *destination = value;
    }
}

fn import_enumish_string(raw: &Value, key: &str, destination: &mut String) {
    match raw.get(key) {
        Some(Value::String(value)) if !value.trim().is_empty() => {
            *destination = value.trim().to_string();
        }
        Some(Value::Number(number)) => {
            *destination = number.to_string();
        }
        _ => {}
    }
}

fn import_u32(raw: &Value, key: &str, destination: &mut u32) {
    if let Some(value) = raw.get(key).and_then(Value::as_u64) {
        *destination = value.min(u32::MAX as u64) as u32;
    }
}

fn import_f64(raw: &Value, key: &str, destination: &mut f64) {
    if let Some(value) = raw.get(key).and_then(Value::as_f64) {
        *destination = value;
    }
}

fn legacy_hotkey(modifiers: Option<&Value>, key: Option<&Value>) -> Option<String> {
    let modifiers = modifiers.and_then(Value::as_u64)? as u32;
    let key = key.and_then(Value::as_u64)? as u32;
    if modifiers == 0 || key == 0 {
        return None;
    }

    let mut parts = Vec::new();
    if modifiers & 0x0002 != 0 {
        parts.push("Ctrl".to_string());
    }
    if modifiers & 0x0001 != 0 {
        parts.push("Alt".to_string());
    }
    if modifiers & 0x0004 != 0 {
        parts.push("Shift".to_string());
    }
    if modifiers & 0x0008 != 0 {
        parts.push("Win".to_string());
    }
    parts.push(legacy_virtual_key_name(key)?);
    Some(parts.join("+"))
}

fn legacy_virtual_key_name(key: u32) -> Option<String> {
    match key {
        0xC0 => Some("`".into()),
        0x70..=0x87 => Some(format!("F{}", key - 0x70 + 1)),
        value if (0x30..=0x39).contains(&value) || (0x41..=0x5A).contains(&value) => {
            char::from_u32(value).map(|value| value.to_string())
        }
        _ => None,
    }
}

fn legacy_default_capture_mode(value: &Value) -> DefaultCaptureMode {
    match value {
        Value::Number(number) => match number.as_u64() {
            Some(1) => DefaultCaptureMode::Fullscreen,
            Some(2) => DefaultCaptureMode::ActiveWindow,
            Some(3) => DefaultCaptureMode::ColorPicker,
            Some(4) => DefaultCaptureMode::Ocr,
            Some(5) => DefaultCaptureMode::Scan,
            Some(6) => DefaultCaptureMode::Sticker,
            Some(7) => DefaultCaptureMode::Upscale,
            Some(8) => DefaultCaptureMode::Center,
            Some(9) => DefaultCaptureMode::Ruler,
            _ => DefaultCaptureMode::Rectangle,
        },
        Value::String(name) if name.eq_ignore_ascii_case("Fullscreen") => {
            DefaultCaptureMode::Fullscreen
        }
        Value::String(name) if name.eq_ignore_ascii_case("ActiveWindow") => {
            DefaultCaptureMode::ActiveWindow
        }
        Value::String(name) if name.eq_ignore_ascii_case("ColorPicker") => {
            DefaultCaptureMode::ColorPicker
        }
        Value::String(name) if name.eq_ignore_ascii_case("Ocr") => DefaultCaptureMode::Ocr,
        Value::String(name) if name.eq_ignore_ascii_case("Scan") => DefaultCaptureMode::Scan,
        Value::String(name) if name.eq_ignore_ascii_case("Sticker") => DefaultCaptureMode::Sticker,
        Value::String(name) if name.eq_ignore_ascii_case("Upscale") => DefaultCaptureMode::Upscale,
        Value::String(name) if name.eq_ignore_ascii_case("Center") => DefaultCaptureMode::Center,
        Value::String(name) if name.eq_ignore_ascii_case("Ruler") => DefaultCaptureMode::Ruler,
        _ => DefaultCaptureMode::Rectangle,
    }
}

fn legacy_toast_position(value: &Value) -> ToastPosition {
    match value {
        Value::Number(number) => match number.as_u64() {
            Some(1) => ToastPosition::Left,
            Some(2) => ToastPosition::TopLeft,
            Some(3) => ToastPosition::TopRight,
            _ => ToastPosition::Right,
        },
        Value::String(name) if name.eq_ignore_ascii_case("Left") => ToastPosition::Left,
        Value::String(name) if name.eq_ignore_ascii_case("TopLeft") => ToastPosition::TopLeft,
        Value::String(name) if name.eq_ignore_ascii_case("TopRight") => ToastPosition::TopRight,
        _ => ToastPosition::Right,
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::{import_app_settings, read_legacy_settings, LegacyOddSnapPaths};
    use oddsnap_core::{
        CaptureImageFormat, DefaultCaptureMode, RecordingFormat, RecordingQuality, ToastPosition,
    };

    #[test]
    fn builds_paths_from_roaming_directory() {
        let paths = LegacyOddSnapPaths::from_roaming_dir("C:/Users/test/AppData/Roaming/OddSnap");

        assert!(paths.settings_path.ends_with("settings.json"));
        assert!(paths.history_dir.ends_with("history"));
        assert!(paths.current_history_database_path.ends_with("history.db"));
    }

    #[test]
    fn reads_legacy_settings_json_without_needing_full_schema() {
        let path = std::env::temp_dir().join(format!(
            "oddsnap-settings-{}.json",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock should be after unix epoch")
                .as_nanos()
        ));
        fs::write(&path, r#"{"SaveToFile":true,"CaptureImageFormat":"Png"}"#)
            .expect("write test settings");

        let imported = read_legacy_settings(&path).expect("read legacy settings");
        fs::remove_file(&path).expect("remove test settings");

        assert_eq!(imported.top_level_key_count, 2);
        assert_eq!(imported.raw["SaveToFile"], true);
    }

    #[test]
    fn imports_core_rust_settings_from_legacy_settings() {
        let path = std::env::temp_dir().join(format!(
            "oddsnap-settings-import-{}.json",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock should be after unix epoch")
                .as_nanos()
        ));
        fs::write(
            &path,
            r#"{"SaveDirectory":"C:\\Users\\test\\Pictures\\OddSnap","SaveHistory":false,"AfterCapture":2,"CaptureImageFormat":1,"JpegQuality":92,"SaveInMonthlyFolders":false,"FileNameTemplate":"Screenshot_{date}","RecordingFormat":2,"RecordingQuality":1,"RecordingFps":60,"GifFps":24,"RecordMicrophone":true,"RecordDesktopAudio":false,"MicrophoneDeviceId":"mic-1","DesktopAudioDeviceId":"desktop-1","HotkeyModifiers":5,"HotkeyKey":67,"GifHotkeyModifiers":1,"GifHotkeyKey":82,"StartWithWindows":false,"AutoCheckForUpdates":false,"CaptureDelaySeconds":5,"MuteSounds":true,"DisableAnimations":true,"UiScale":1.25,"ShowCrosshairGuides":true,"ShowCursor":true,"ShowCaptureMagnifier":false,"OverlayCaptureAllMonitors":false,"DetectWindows":false,"DefaultCaptureMode":"ActiveWindow","LastCaptureMode":"ColorPicker","ToastPosition":3,"ToastButtons":{"Copy":{"Visible":false}}}"#,
        )
        .expect("write test settings");

        let imported = read_legacy_settings(&path).expect("read legacy settings");
        let settings = import_app_settings(&imported);
        fs::remove_file(&path).expect("remove test settings");

        assert_eq!(
            settings.capture_output_directory.as_deref(),
            Some(std::path::Path::new("C:\\Users\\test\\Pictures\\OddSnap"))
        );
        assert!(!settings.save_history);
        assert!(!settings.copy_captures_to_clipboard);
        assert_eq!(settings.capture_image_format, CaptureImageFormat::Jpeg);
        assert_eq!(settings.jpeg_quality, 92);
        assert!(!settings.save_in_monthly_folders);
        assert_eq!(settings.file_name_template, "Screenshot_{date}");
        assert_eq!(settings.recording_format, RecordingFormat::WebM);
        assert_eq!(settings.recording_quality, RecordingQuality::P1080);
        assert_eq!(settings.recording_fps, 60);
        assert_eq!(settings.gif_fps, 24);
        assert!(settings.record_microphone);
        assert!(!settings.record_desktop_audio);
        assert_eq!(settings.microphone_device_id.as_deref(), Some("mic-1"));
        assert_eq!(
            settings.desktop_audio_device_id.as_deref(),
            Some("desktop-1")
        );
        assert_eq!(settings.capture_hotkey, "Alt+Shift+C");
        assert_eq!(settings.recording_hotkey.as_deref(), Some("Alt+R"));
        assert!(!settings.start_with_windows);
        assert!(!settings.auto_check_for_updates);
        assert_eq!(settings.capture_delay_seconds, 5);
        assert!(settings.mute_sounds);
        assert!(settings.disable_animations);
        assert_eq!(settings.ui_scale, 1.25);
        assert!(settings.show_crosshair_guides);
        assert!(settings.show_cursor);
        assert!(!settings.show_capture_magnifier);
        assert!(!settings.overlay_capture_all_monitors);
        assert!(!settings.detect_windows);
        assert_eq!(
            settings.default_capture_mode,
            DefaultCaptureMode::ActiveWindow
        );
        assert_eq!(settings.last_capture_mode, DefaultCaptureMode::ColorPicker);
        assert_eq!(settings.toast_position, ToastPosition::TopRight);
        assert_eq!(
            settings
                .toast_button_layout
                .as_ref()
                .expect("toast button layout should import")["Copy"]["Visible"],
            false
        );
    }

    #[test]
    fn imports_string_after_capture_copy_modes() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 1,
            raw: serde_json::json!({"AfterCapture":"PreviewAndCopy"}),
        };

        let settings = import_app_settings(&import);

        assert!(settings.copy_captures_to_clipboard);
    }

    #[test]
    fn imports_invalid_capture_format_as_png_and_clamps_jpeg_quality() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 2,
            raw: serde_json::json!({"CaptureImageFormat":99,"JpegQuality":250}),
        };

        let settings = import_app_settings(&import);

        assert_eq!(settings.capture_image_format, CaptureImageFormat::Png);
        assert_eq!(settings.jpeg_quality, 100);
    }

    #[test]
    fn imports_recording_string_values_and_clamps_fps() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 6,
            raw: serde_json::json!({
                "RecordingFormat": "MKV",
                "RecordingQuality": "480p",
                "RecordingFps": 500,
                "GifFps": 0,
                "MicrophoneDeviceId": " ",
                "DesktopAudioDeviceId": "device"
            }),
        };

        let settings = import_app_settings(&import);

        assert_eq!(settings.recording_format, RecordingFormat::Mkv);
        assert_eq!(settings.recording_quality, RecordingQuality::P480);
        assert_eq!(settings.recording_fps, 240);
        assert_eq!(settings.gif_fps, 1);
        assert_eq!(settings.microphone_device_id, None);
        assert_eq!(settings.desktop_audio_device_id.as_deref(), Some("device"));
    }

    #[test]
    fn skips_disabled_or_unknown_legacy_hotkeys() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 4,
            raw: serde_json::json!({
                "HotkeyModifiers": 0,
                "HotkeyKey": 0,
                "GifHotkeyModifiers": 1,
                "GifHotkeyKey": 999
            }),
        };

        let settings = import_app_settings(&import);

        assert_eq!(settings.capture_hotkey, "Alt+`");
        assert_eq!(settings.recording_hotkey, None);
    }

    #[test]
    fn imports_capture_preference_numbers_and_clamps_ui_values() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 4,
            raw: serde_json::json!({
                "CaptureDelaySeconds": 999,
                "UiScale": 4.0,
                "DefaultCaptureMode": 8,
                "LastCaptureMode": 9,
                "ToastPosition": "TopLeft"
            }),
        };

        let settings = import_app_settings(&import);

        assert_eq!(settings.capture_delay_seconds, 60);
        assert_eq!(settings.ui_scale, 1.4);
        assert_eq!(settings.default_capture_mode, DefaultCaptureMode::Center);
        assert_eq!(settings.last_capture_mode, DefaultCaptureMode::Ruler);
        assert_eq!(settings.toast_position, ToastPosition::TopLeft);
    }

    #[test]
    fn imports_advanced_legacy_settings_without_dropping_feature_configuration() {
        let import = super::LegacySettingsImport {
            source_path: PathBuf::from("settings.json"),
            top_level_key_count: 20,
            raw: serde_json::from_str(
                r#"{
                    "OcrHotkeyModifiers": 5,
                    "OcrHotkeyKey": 79,
                    "PickerHotkeyModifiers": 1,
                    "PickerHotkeyKey": 67,
                    "StickerHotkeyModifiers": 1,
                    "StickerHotkeyKey": 83,
                    "AiRedirectHotkeyModifiers": 1,
                    "AiRedirectHotkeyKey": 65,
                    "OcrLanguageTag": "en-US",
                    "OcrModelQuality": 1,
                    "OcrDefaultTranslateFrom": "en",
                    "OcrDefaultTranslateTo": "es",
                    "GoogleTranslateApiKey": "key",
                    "TranslationRuntimeInstalled": true,
                    "TranslationModel": 1,
                    "AnnotationStrokeShadow": false,
                    "StyleScreenshots": true,
                    "AddScreenshotShadow": true,
                    "AddScreenshotStroke": true,
                    "CaptureMaxLongEdge": 1440,
                    "WindowDetection": "Off",
                    "CaptureDockSide": "Bottom",
                    "ScrollingCaptureMode": "Manual",
                    "InterfaceLanguage": "fr",
                    "CompressHistory": true,
                    "HasCompletedSetup": true,
                    "CenterSelectionAspectRatio": "Widescreen16x9",
                    "ShowToolNumberBadges": false,
                    "HistoryRetention": "ThirtyDays",
                    "ImageSearchSources": 1,
                    "ShowImageSearchBar": false,
                    "ImageSearchExactMatch": true,
                    "ShowImageSearchDiagnostics": true,
                    "AutoIndexImages": false,
                    "AutoUploadScreenshots": false,
                    "AutoUploadGifs": true,
                    "AutoUploadVideos": true,
                    "ImageUploadDestination": "Imgur",
                    "ImageUploadSettings": {"ClientId": "cid"},
                    "StickerUploadSettings": {"RuntimeInstalled": true},
                    "UpscaleUploadSettings": {"Scale": 4},
                    "ToastDurationSeconds": 4.5,
                    "ToastFadeOutEnabled": true,
                    "ToastFadeOutSeconds": 2.0,
                    "AutoPinPreviews": true,
                    "OpenWithApps": {"paint": "mspaint.exe"},
                    "SoundPack": "Soft",
                    "EnabledTools": ["rect", "ocr", "sticker"],
                    "ToolHotkeys": {"arrow": [0, 49]}
                }"#,
            )
            .expect("parse advanced settings fixture"),
        };

        let settings = import_app_settings(&import);

        assert_eq!(settings.ocr_hotkey.as_deref(), Some("Alt+Shift+O"));
        assert_eq!(settings.picker_hotkey.as_deref(), Some("Alt+C"));
        assert_eq!(settings.sticker_hotkey.as_deref(), Some("Alt+S"));
        assert_eq!(settings.ai_redirect_hotkey.as_deref(), Some("Alt+A"));
        assert_eq!(settings.ocr_language_tag, "en-US");
        assert_eq!(settings.ocr_model_quality, 1);
        assert_eq!(settings.ocr_default_translate_from, "en");
        assert_eq!(settings.ocr_default_translate_to, "es");
        assert_eq!(settings.google_translate_api_key.as_deref(), Some("key"));
        assert!(settings.translation_runtime_installed);
        assert_eq!(settings.translation_model, 1);
        assert!(!settings.annotation_stroke_shadow);
        assert!(settings.style_screenshots);
        assert!(settings.add_screenshot_shadow);
        assert!(settings.add_screenshot_stroke);
        assert_eq!(settings.capture_max_long_edge, 1440);
        assert_eq!(settings.window_detection, "Off");
        assert_eq!(settings.capture_dock_side, "Bottom");
        assert_eq!(settings.scrolling_capture_mode, "Manual");
        assert_eq!(settings.interface_language, "fr");
        assert!(settings.compress_history);
        assert!(settings.has_completed_setup);
        assert_eq!(settings.center_selection_aspect_ratio, "Widescreen16x9");
        assert!(!settings.show_tool_number_badges);
        assert_eq!(settings.history_retention, "ThirtyDays");
        assert_eq!(settings.image_search_sources, 1);
        assert!(!settings.show_image_search_bar);
        assert!(settings.image_search_exact_match);
        assert!(settings.show_image_search_diagnostics);
        assert!(!settings.auto_index_images);
        assert!(!settings.auto_upload_screenshots);
        assert!(settings.auto_upload_gifs);
        assert!(settings.auto_upload_videos);
        assert_eq!(settings.image_upload_destination, "Imgur");
        assert_eq!(
            settings
                .image_upload_settings
                .as_ref()
                .expect("image upload settings should import")["ClientId"],
            "cid"
        );
        assert_eq!(
            settings
                .sticker_upload_settings
                .as_ref()
                .expect("sticker upload settings should import")["RuntimeInstalled"],
            true
        );
        assert_eq!(
            settings
                .upscale_upload_settings
                .as_ref()
                .expect("upscale upload settings should import")["Scale"],
            4
        );
        assert_eq!(settings.toast_duration_seconds, 4.5);
        assert!(settings.toast_fade_out_enabled);
        assert_eq!(settings.toast_fade_out_seconds, 2.0);
        assert!(settings.auto_pin_previews);
        assert_eq!(
            settings.open_with_apps.get("paint").map(String::as_str),
            Some("mspaint.exe")
        );
        assert_eq!(settings.sound_pack, "Soft");
        assert_eq!(
            settings.enabled_tools.as_deref(),
            Some(["rect".to_string(), "ocr".to_string(), "sticker".to_string()].as_slice())
        );
        assert_eq!(settings.tool_hotkeys.get("arrow"), Some(&vec![0, 49]));
    }

    #[test]
    fn imports_existing_history_from_current_sqlite_database() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-history-db-import-{}",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock should be after unix epoch")
                .as_nanos()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create root");
        let image = root.join("capture.png");
        fs::write(&image, b"png").expect("write image");
        let database = root.join("history.db");
        let connection = rusqlite::Connection::open(&database).expect("open db");
        connection
            .execute_batch(
                "CREATE TABLE history_entries (
                    file_path TEXT PRIMARY KEY,
                    file_name TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    kind INTEGER NOT NULL,
                    upload_url TEXT NULL,
                    upload_provider TEXT NULL,
                    upload_error TEXT NULL
                );
                CREATE TABLE color_entries (
                    hex TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL
                );
                CREATE TABLE ocr_entries (
                    text TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL
                );
                CREATE TABLE code_entries (
                    text TEXT NOT NULL,
                    format TEXT NOT NULL,
                    captured_at_ticks INTEGER NOT NULL
                );",
            )
            .expect("create table");
        let legacy_ocr_unix_ms = 1_700_000_000_123_i64;
        let legacy_ocr_ticks = 621_355_968_000_000_000_i64 + legacy_ocr_unix_ms * 10_000;
        connection
            .execute(
                "INSERT INTO history_entries(file_path, file_name, captured_at_ticks, width, height, file_size_bytes, kind, upload_url, upload_provider, upload_error)
                 VALUES (?1, 'capture.png', 123, 12, 34, 3, 0, NULL, NULL, NULL)",
                [image.display().to_string()],
            )
            .expect("insert row");
        connection
            .execute(
                "INSERT INTO color_entries(hex, captured_at_ticks) VALUES ('aabbcc', 456)",
                [],
            )
            .expect("insert color row");
        connection
            .execute(
                "INSERT INTO ocr_entries(text, captured_at_ticks) VALUES (' legacy ocr text ', ?1)",
                [legacy_ocr_ticks],
            )
            .expect("insert ocr row");
        connection
            .execute(
                "INSERT INTO code_entries(text, format, captured_at_ticks) VALUES ('https://example.test', 'QR_CODE', ?1)",
                [legacy_ocr_ticks],
            )
            .expect("insert code row");

        let paths = LegacyOddSnapPaths {
            roaming_dir: root.join("roaming"),
            settings_path: root.join("roaming").join("settings.json"),
            history_dir: root.join("roaming").join("history"),
            current_history_dir: root.clone(),
            current_history_database_path: database,
            current_history_index_path: root.join("index.json"),
        };

        let index = super::import_existing_history(&paths).expect("import history");

        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.entries[0].file_path, image);
        assert_eq!(index.entries[0].width, 12);
        assert_eq!(index.entries[0].kind, oddsnap_core::HistoryKind::Image);
        assert_eq!(index.colors.len(), 1);
        assert_eq!(index.colors[0].hex, "AABBCC");
        assert_eq!(index.ocr_entries.len(), 1);
        assert_eq!(index.ocr_entries[0].text, "legacy ocr text");
        assert_eq!(
            index.ocr_entries[0].captured_at_unix_ms,
            legacy_ocr_unix_ms as u64
        );
        assert_eq!(index.code_entries.len(), 1);
        assert_eq!(index.code_entries[0].text, "https://example.test");
        assert_eq!(index.code_entries[0].format, "QR_CODE");
        assert_eq!(
            index.code_entries[0].captured_at_unix_ms,
            legacy_ocr_unix_ms as u64
        );
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn imports_existing_history_from_json_when_database_is_missing() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-history-json-import-{}",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock should be after unix epoch")
                .as_nanos()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create root");
        let image = root.join("capture.jpg");
        fs::write(&image, b"jpg").expect("write image");
        let index_path = root.join("index.json");
        let json = serde_json::json!([{
            "FileName": "capture.jpg",
            "FilePath": image,
            "Width": 5,
            "Height": 6,
            "FileSizeBytes": 3,
            "Kind": "Image"
        }]);
        fs::write(&index_path, json.to_string()).expect("write json index");
        let color_json = serde_json::json!([
            {
                "Hex": "#112233",
                "CapturedAt": "2026-01-01T00:00:00"
            },
            {
                "Hex": "not-a-color",
                "CapturedAt": "2026-01-02T00:00:00"
            }
        ]);
        fs::write(root.join("color_index.json"), color_json.to_string())
            .expect("write color json index");
        let ocr_json = serde_json::json!([
            {
                "Text": "json ocr text",
                "CapturedAt": "2026-01-02T03:04:05Z"
            },
            {
                "Text": "   "
            }
        ]);
        fs::write(root.join("ocr_index.json"), ocr_json.to_string()).expect("write ocr json index");
        let paths = LegacyOddSnapPaths {
            roaming_dir: root.join("roaming"),
            settings_path: root.join("roaming").join("settings.json"),
            history_dir: root.join("roaming").join("history"),
            current_history_dir: root.clone(),
            current_history_database_path: root.join("missing.db"),
            current_history_index_path: index_path,
        };

        let index = super::import_existing_history(&paths).expect("import history");

        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.entries[0].file_path, image);
        assert_eq!(index.entries[0].height, 6);
        assert_eq!(index.colors.len(), 1);
        assert_eq!(index.colors[0].hex, "112233");
        assert_eq!(index.ocr_entries.len(), 1);
        assert_eq!(index.ocr_entries[0].text, "json ocr text");
        assert_eq!(index.ocr_entries[0].captured_at_unix_ms, 1_767_323_045_000);
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn imports_ocr_history_even_when_no_capture_history_exists() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-ocr-only-import-{}",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock should be after unix epoch")
                .as_nanos()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create root");
        let ocr_json = serde_json::json!([{
            "text": "ocr only",
            "captured_at_unix_ms": 1234
        }]);
        fs::write(root.join("ocr_index.json"), ocr_json.to_string()).expect("write ocr json index");
        let paths = LegacyOddSnapPaths {
            roaming_dir: root.join("roaming"),
            settings_path: root.join("roaming").join("settings.json"),
            history_dir: root.join("roaming").join("history"),
            current_history_dir: root.clone(),
            current_history_database_path: root.join("missing.db"),
            current_history_index_path: root.join("missing-index.json"),
        };

        let index = super::import_existing_history(&paths).expect("import history");

        assert!(index.entries.is_empty());
        assert!(index.colors.is_empty());
        assert_eq!(index.ocr_entries.len(), 1);
        assert_eq!(index.ocr_entries[0].text, "ocr only");
        assert_eq!(index.ocr_entries[0].captured_at_unix_ms, 1234);
        let _ = fs::remove_dir_all(root);
    }
}
