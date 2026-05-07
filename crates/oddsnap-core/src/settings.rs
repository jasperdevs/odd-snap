use std::{
    collections::BTreeMap,
    fs,
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};
use serde_json::Value;
use thiserror::Error;

use crate::DEFAULT_FILE_NAME_TEMPLATE;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum CaptureImageFormat {
    #[default]
    Png,
    Jpeg,
    Bmp,
}

impl CaptureImageFormat {
    pub fn extension(self) -> &'static str {
        match self {
            Self::Png => "png",
            Self::Jpeg => "jpg",
            Self::Bmp => "bmp",
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Png => "PNG",
            Self::Jpeg => "JPEG",
            Self::Bmp => "BMP",
        }
    }
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum RecordingFormat {
    Gif,
    #[default]
    Mp4,
    WebM,
    Mkv,
}

impl RecordingFormat {
    pub fn extension(self) -> &'static str {
        match self {
            Self::Gif => "gif",
            Self::Mp4 => "mp4",
            Self::WebM => "webm",
            Self::Mkv => "mkv",
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Gif => "GIF",
            Self::Mp4 => "MP4",
            Self::WebM => "WebM",
            Self::Mkv => "MKV",
        }
    }
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum RecordingQuality {
    #[default]
    Original,
    P1080,
    P720,
    P480,
}

impl RecordingQuality {
    pub fn max_height(self) -> Option<u32> {
        match self {
            Self::Original => None,
            Self::P1080 => Some(1080),
            Self::P720 => Some(720),
            Self::P480 => Some(480),
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Original => "Original",
            Self::P1080 => "1080p",
            Self::P720 => "720p",
            Self::P480 => "480p",
        }
    }
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum DefaultCaptureMode {
    #[default]
    Rectangle,
    Fullscreen,
    ActiveWindow,
    ColorPicker,
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
}

impl DefaultCaptureMode {
    pub fn label(self) -> &'static str {
        match self {
            Self::Rectangle => "Rectangle",
            Self::Fullscreen => "Fullscreen",
            Self::ActiveWindow => "Active window",
            Self::ColorPicker => "Color picker",
            Self::Ocr => "OCR",
            Self::Scan => "Scan",
            Self::Sticker => "Sticker",
            Self::Upscale => "Upscale",
            Self::Center => "Center select",
            Self::Ruler => "Ruler",
        }
    }
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum ToastPosition {
    #[default]
    Right,
    Left,
    TopLeft,
    TopRight,
}

impl ToastPosition {
    pub fn label(self) -> &'static str {
        match self {
            Self::Right => "Right",
            Self::Left => "Left",
            Self::TopLeft => "Top left",
            Self::TopRight => "Top right",
        }
    }
}

#[derive(Debug, Error)]
pub enum SettingsStoreError {
    #[error("failed to read settings: {0}")]
    Read(#[source] std::io::Error),
    #[error("failed to write settings: {0}")]
    Write(#[source] std::io::Error),
    #[error("failed to parse settings: {0}")]
    Parse(#[source] serde_json::Error),
    #[error("failed to serialize settings: {0}")]
    Serialize(#[source] serde_json::Error),
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AppSettings {
    #[serde(default)]
    pub capture_output_directory: Option<PathBuf>,
    #[serde(default = "default_copy_captures_to_clipboard")]
    pub copy_captures_to_clipboard: bool,
    #[serde(default = "default_save_history")]
    pub save_history: bool,
    #[serde(default)]
    pub capture_image_format: CaptureImageFormat,
    #[serde(default = "default_jpeg_quality")]
    pub jpeg_quality: u8,
    #[serde(default = "default_save_in_monthly_folders")]
    pub save_in_monthly_folders: bool,
    #[serde(default = "default_file_name_template")]
    pub file_name_template: String,
    #[serde(default)]
    pub recording_format: RecordingFormat,
    #[serde(default)]
    pub recording_quality: RecordingQuality,
    #[serde(default = "default_recording_fps")]
    pub recording_fps: u32,
    #[serde(default = "default_gif_fps")]
    pub gif_fps: u32,
    #[serde(default)]
    pub record_microphone: bool,
    #[serde(default = "default_record_desktop_audio")]
    pub record_desktop_audio: bool,
    #[serde(default)]
    pub microphone_device_id: Option<String>,
    #[serde(default)]
    pub desktop_audio_device_id: Option<String>,
    #[serde(default = "default_capture_hotkey")]
    pub capture_hotkey: String,
    #[serde(default)]
    pub recording_hotkey: Option<String>,
    #[serde(default = "default_start_with_windows")]
    pub start_with_windows: bool,
    #[serde(default = "default_auto_check_for_updates")]
    pub auto_check_for_updates: bool,
    #[serde(default)]
    pub capture_delay_seconds: u32,
    #[serde(default)]
    pub mute_sounds: bool,
    #[serde(default)]
    pub disable_animations: bool,
    #[serde(default = "default_ui_scale")]
    pub ui_scale: f64,
    #[serde(default)]
    pub show_crosshair_guides: bool,
    #[serde(default)]
    pub show_cursor: bool,
    #[serde(default = "default_show_capture_magnifier")]
    pub show_capture_magnifier: bool,
    #[serde(default = "default_overlay_capture_all_monitors")]
    pub overlay_capture_all_monitors: bool,
    #[serde(default = "default_detect_windows")]
    pub detect_windows: bool,
    #[serde(default)]
    pub default_capture_mode: DefaultCaptureMode,
    #[serde(default)]
    pub last_capture_mode: DefaultCaptureMode,
    #[serde(default)]
    pub toast_position: ToastPosition,
    #[serde(default)]
    pub toast_button_layout: Option<Value>,
    #[serde(default)]
    pub ocr_hotkey: Option<String>,
    #[serde(default)]
    pub picker_hotkey: Option<String>,
    #[serde(default)]
    pub scan_hotkey: Option<String>,
    #[serde(default)]
    pub sticker_hotkey: Option<String>,
    #[serde(default)]
    pub upscale_hotkey: Option<String>,
    #[serde(default)]
    pub center_hotkey: Option<String>,
    #[serde(default)]
    pub fullscreen_hotkey: Option<String>,
    #[serde(default)]
    pub active_window_hotkey: Option<String>,
    #[serde(default)]
    pub ruler_hotkey: Option<String>,
    #[serde(default)]
    pub scroll_capture_hotkey: Option<String>,
    #[serde(default)]
    pub ai_redirect_hotkey: Option<String>,
    #[serde(default = "default_ocr_language_tag")]
    pub ocr_language_tag: String,
    #[serde(default)]
    pub ocr_model_quality: u32,
    #[serde(default = "default_auto_language")]
    pub ocr_default_translate_from: String,
    #[serde(default = "default_auto_language")]
    pub ocr_default_translate_to: String,
    #[serde(default)]
    pub google_translate_api_key: Option<String>,
    #[serde(default)]
    pub translation_runtime_installed: bool,
    #[serde(default = "default_translation_model")]
    pub translation_model: u32,
    #[serde(default = "default_annotation_stroke_shadow")]
    pub annotation_stroke_shadow: bool,
    #[serde(default = "default_save_to_file")]
    pub save_to_file: bool,
    #[serde(default)]
    pub ask_for_file_name_on_save: bool,
    #[serde(default)]
    pub style_screenshots: bool,
    #[serde(default)]
    pub add_screenshot_shadow: bool,
    #[serde(default)]
    pub add_screenshot_stroke: bool,
    #[serde(default)]
    pub capture_max_long_edge: u32,
    #[serde(default = "default_window_detection")]
    pub window_detection: String,
    #[serde(default = "default_capture_dock_side")]
    pub capture_dock_side: String,
    #[serde(default = "default_scrolling_capture_mode")]
    pub scrolling_capture_mode: String,
    #[serde(default = "default_interface_language")]
    pub interface_language: String,
    #[serde(default)]
    pub compress_history: bool,
    #[serde(default)]
    pub has_completed_setup: bool,
    #[serde(default = "default_center_selection_aspect_ratio")]
    pub center_selection_aspect_ratio: String,
    #[serde(default = "default_show_tool_number_badges")]
    pub show_tool_number_badges: bool,
    #[serde(default = "default_history_retention")]
    pub history_retention: String,
    #[serde(default = "default_image_search_sources")]
    pub image_search_sources: u32,
    #[serde(default = "default_show_image_search_bar")]
    pub show_image_search_bar: bool,
    #[serde(default)]
    pub image_search_exact_match: bool,
    #[serde(default)]
    pub show_image_search_diagnostics: bool,
    #[serde(default = "default_auto_index_images")]
    pub auto_index_images: bool,
    #[serde(default = "default_auto_upload_screenshots")]
    pub auto_upload_screenshots: bool,
    #[serde(default)]
    pub auto_upload_gifs: bool,
    #[serde(default)]
    pub auto_upload_videos: bool,
    #[serde(default = "default_image_upload_destination")]
    pub image_upload_destination: String,
    #[serde(default)]
    pub image_upload_settings: Option<Value>,
    #[serde(default)]
    pub sticker_upload_settings: Option<Value>,
    #[serde(default)]
    pub upscale_upload_settings: Option<Value>,
    #[serde(default = "default_toast_duration_seconds")]
    pub toast_duration_seconds: f64,
    #[serde(default)]
    pub toast_fade_out_enabled: bool,
    #[serde(default = "default_toast_fade_out_seconds")]
    pub toast_fade_out_seconds: f64,
    #[serde(default)]
    pub auto_pin_previews: bool,
    #[serde(default)]
    pub open_with_apps: BTreeMap<String, String>,
    #[serde(default = "default_sound_pack")]
    pub sound_pack: String,
    #[serde(default)]
    pub enabled_tools: Option<Vec<String>>,
    #[serde(default)]
    pub tool_hotkeys: BTreeMap<String, Vec<u32>>,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            capture_output_directory: None,
            copy_captures_to_clipboard: true,
            save_history: true,
            capture_image_format: CaptureImageFormat::Png,
            jpeg_quality: default_jpeg_quality(),
            save_in_monthly_folders: default_save_in_monthly_folders(),
            file_name_template: default_file_name_template(),
            recording_format: RecordingFormat::Mp4,
            recording_quality: RecordingQuality::Original,
            recording_fps: default_recording_fps(),
            gif_fps: default_gif_fps(),
            record_microphone: false,
            record_desktop_audio: default_record_desktop_audio(),
            microphone_device_id: None,
            desktop_audio_device_id: None,
            capture_hotkey: default_capture_hotkey(),
            recording_hotkey: None,
            start_with_windows: default_start_with_windows(),
            auto_check_for_updates: default_auto_check_for_updates(),
            capture_delay_seconds: 0,
            mute_sounds: false,
            disable_animations: false,
            ui_scale: default_ui_scale(),
            show_crosshair_guides: false,
            show_cursor: false,
            show_capture_magnifier: default_show_capture_magnifier(),
            overlay_capture_all_monitors: default_overlay_capture_all_monitors(),
            detect_windows: default_detect_windows(),
            default_capture_mode: DefaultCaptureMode::Rectangle,
            last_capture_mode: DefaultCaptureMode::Rectangle,
            toast_position: ToastPosition::Right,
            toast_button_layout: None,
            ocr_hotkey: None,
            picker_hotkey: None,
            scan_hotkey: None,
            sticker_hotkey: None,
            upscale_hotkey: None,
            center_hotkey: None,
            fullscreen_hotkey: None,
            active_window_hotkey: None,
            ruler_hotkey: None,
            scroll_capture_hotkey: None,
            ai_redirect_hotkey: None,
            ocr_language_tag: default_ocr_language_tag(),
            ocr_model_quality: 0,
            ocr_default_translate_from: default_auto_language(),
            ocr_default_translate_to: default_auto_language(),
            google_translate_api_key: None,
            translation_runtime_installed: false,
            translation_model: default_translation_model(),
            annotation_stroke_shadow: default_annotation_stroke_shadow(),
            save_to_file: default_save_to_file(),
            ask_for_file_name_on_save: false,
            style_screenshots: false,
            add_screenshot_shadow: false,
            add_screenshot_stroke: false,
            capture_max_long_edge: 0,
            window_detection: default_window_detection(),
            capture_dock_side: default_capture_dock_side(),
            scrolling_capture_mode: default_scrolling_capture_mode(),
            interface_language: default_interface_language(),
            compress_history: false,
            has_completed_setup: false,
            center_selection_aspect_ratio: default_center_selection_aspect_ratio(),
            show_tool_number_badges: default_show_tool_number_badges(),
            history_retention: default_history_retention(),
            image_search_sources: default_image_search_sources(),
            show_image_search_bar: default_show_image_search_bar(),
            image_search_exact_match: false,
            show_image_search_diagnostics: false,
            auto_index_images: default_auto_index_images(),
            auto_upload_screenshots: default_auto_upload_screenshots(),
            auto_upload_gifs: false,
            auto_upload_videos: false,
            image_upload_destination: default_image_upload_destination(),
            image_upload_settings: None,
            sticker_upload_settings: None,
            upscale_upload_settings: None,
            toast_duration_seconds: default_toast_duration_seconds(),
            toast_fade_out_enabled: false,
            toast_fade_out_seconds: default_toast_fade_out_seconds(),
            auto_pin_previews: false,
            open_with_apps: BTreeMap::new(),
            sound_pack: default_sound_pack(),
            enabled_tools: None,
            tool_hotkeys: BTreeMap::new(),
        }
    }
}

impl AppSettings {
    pub fn capture_output_directory_or(&self, fallback: PathBuf) -> PathBuf {
        self.capture_output_directory
            .clone()
            .filter(|path| !path.as_os_str().is_empty())
            .unwrap_or(fallback)
    }
}

fn default_copy_captures_to_clipboard() -> bool {
    true
}

fn default_save_history() -> bool {
    true
}

fn default_jpeg_quality() -> u8 {
    85
}

fn default_save_in_monthly_folders() -> bool {
    true
}

fn default_file_name_template() -> String {
    DEFAULT_FILE_NAME_TEMPLATE.into()
}

fn default_recording_fps() -> u32 {
    30
}

fn default_gif_fps() -> u32 {
    15
}

fn default_record_desktop_audio() -> bool {
    true
}

fn default_capture_hotkey() -> String {
    "Alt+`".into()
}

fn default_start_with_windows() -> bool {
    true
}

fn default_auto_check_for_updates() -> bool {
    true
}

fn default_ui_scale() -> f64 {
    1.0
}

fn default_show_capture_magnifier() -> bool {
    true
}

fn default_overlay_capture_all_monitors() -> bool {
    true
}

fn default_detect_windows() -> bool {
    true
}

fn default_ocr_language_tag() -> String {
    "auto".into()
}

fn default_auto_language() -> String {
    "auto".into()
}

fn default_translation_model() -> u32 {
    2
}

fn default_annotation_stroke_shadow() -> bool {
    true
}

fn default_save_to_file() -> bool {
    true
}

fn default_window_detection() -> String {
    "WindowOnly".into()
}

fn default_capture_dock_side() -> String {
    "Top".into()
}

fn default_scrolling_capture_mode() -> String {
    "Automatic".into()
}

fn default_interface_language() -> String {
    "auto".into()
}

fn default_center_selection_aspect_ratio() -> String {
    "Free".into()
}

fn default_show_tool_number_badges() -> bool {
    true
}

fn default_history_retention() -> String {
    "Never".into()
}

fn default_image_search_sources() -> u32 {
    3
}

fn default_show_image_search_bar() -> bool {
    true
}

fn default_auto_index_images() -> bool {
    true
}

fn default_auto_upload_screenshots() -> bool {
    true
}

fn default_image_upload_destination() -> String {
    "None".into()
}

fn default_toast_duration_seconds() -> f64 {
    2.5
}

fn default_toast_fade_out_seconds() -> f64 {
    1.0
}

fn default_sound_pack() -> String {
    "Default".into()
}

#[derive(Debug, Clone)]
pub struct SettingsStore {
    path: PathBuf,
}

impl SettingsStore {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn load_or_default(&self) -> Result<AppSettings, SettingsStoreError> {
        if !self.path.exists() {
            return Ok(AppSettings::default());
        }

        let bytes = fs::read(&self.path).map_err(SettingsStoreError::Read)?;
        serde_json::from_slice(&bytes).map_err(SettingsStoreError::Parse)
    }

    pub fn save(&self, settings: &AppSettings) -> Result<(), SettingsStoreError> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).map_err(SettingsStoreError::Write)?;
        }

        let bytes = serde_json::to_vec_pretty(settings).map_err(SettingsStoreError::Serialize)?;
        fs::write(&self.path, bytes).map_err(SettingsStoreError::Write)
    }
}

pub fn default_settings_path() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(appdata) = std::env::var_os("APPDATA") {
            return PathBuf::from(appdata)
                .join("OddSnap")
                .join("rust-settings.json");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        if let Some(config_home) = std::env::var_os("XDG_CONFIG_HOME") {
            return PathBuf::from(config_home)
                .join("oddsnap")
                .join("settings.json");
        }

        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join(".config")
                .join("oddsnap")
                .join("settings.json");
        }
    }

    std::env::temp_dir()
        .join("OddSnap")
        .join("rust-settings.json")
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SettingsValueKind {
    Toggle,
    Choice,
    Text,
    Folder,
    Number,
    Duration,
    Action,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SettingsOptionDefinition {
    pub value: String,
    pub label: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SettingDefinition {
    pub key: String,
    pub label: String,
    pub value_kind: SettingsValueKind,
    pub description: String,
    pub binding_path: Option<String>,
    pub options: Vec<SettingsOptionDefinition>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SettingsSectionDefinition {
    pub key: String,
    pub title: String,
    pub description: String,
    pub items: Vec<SettingDefinition>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SettingsPageDefinition {
    pub key: String,
    pub title: String,
    pub description: String,
    pub sections: Vec<SettingsSectionDefinition>,
}

#[cfg(test)]
mod tests {
    use std::{fs, path::PathBuf};

    use super::{
        AppSettings, CaptureImageFormat, DefaultCaptureMode, RecordingFormat, RecordingQuality,
        SettingsStore, ToastPosition,
    };
    use crate::{normalize_file_name_template, DEFAULT_FILE_NAME_TEMPLATE};

    #[test]
    fn app_settings_defaults_copy_captures_to_clipboard() {
        let settings = AppSettings::default();

        assert!(settings.copy_captures_to_clipboard);
        assert!(settings.save_history);
        assert_eq!(settings.capture_image_format, CaptureImageFormat::Png);
        assert_eq!(settings.jpeg_quality, 85);
        assert_eq!(settings.recording_format, RecordingFormat::Mp4);
        assert_eq!(settings.recording_quality, RecordingQuality::Original);
        assert_eq!(settings.recording_fps, 30);
        assert_eq!(settings.gif_fps, 15);
        assert!(!settings.record_microphone);
        assert!(settings.record_desktop_audio);
        assert_eq!(settings.capture_hotkey, "Alt+`");
        assert_eq!(settings.recording_hotkey, None);
        assert!(settings.start_with_windows);
        assert!(settings.auto_check_for_updates);
        assert_eq!(settings.capture_delay_seconds, 0);
        assert!(!settings.mute_sounds);
        assert!(!settings.disable_animations);
        assert_eq!(settings.ui_scale, 1.0);
        assert!(!settings.show_crosshair_guides);
        assert!(!settings.show_cursor);
        assert!(settings.show_capture_magnifier);
        assert!(settings.overlay_capture_all_monitors);
        assert!(settings.detect_windows);
        assert_eq!(settings.default_capture_mode, DefaultCaptureMode::Rectangle);
        assert_eq!(settings.last_capture_mode, DefaultCaptureMode::Rectangle);
        assert_eq!(settings.toast_position, ToastPosition::Right);
        assert_eq!(settings.toast_button_layout, None);
        assert_eq!(settings.ocr_language_tag, "auto");
        assert_eq!(settings.ocr_default_translate_from, "auto");
        assert_eq!(settings.ocr_default_translate_to, "auto");
        assert_eq!(settings.translation_model, 2);
        assert!(settings.annotation_stroke_shadow);
        assert!(settings.save_to_file);
        assert_eq!(settings.window_detection, "WindowOnly");
        assert_eq!(settings.capture_dock_side, "Top");
        assert_eq!(settings.scrolling_capture_mode, "Automatic");
        assert_eq!(settings.interface_language, "auto");
        assert_eq!(settings.center_selection_aspect_ratio, "Free");
        assert!(settings.show_tool_number_badges);
        assert_eq!(settings.history_retention, "Never");
        assert_eq!(settings.image_search_sources, 3);
        assert!(settings.show_image_search_bar);
        assert!(settings.auto_index_images);
        assert!(settings.auto_upload_screenshots);
        assert_eq!(settings.image_upload_destination, "None");
        assert_eq!(settings.toast_duration_seconds, 2.5);
        assert_eq!(settings.toast_fade_out_seconds, 1.0);
        assert_eq!(settings.sound_pack, "Default");
        assert!(settings.capture_output_directory.is_none());
    }

    #[test]
    fn capture_output_directory_uses_fallback_when_empty() {
        let settings = AppSettings {
            capture_output_directory: Some(PathBuf::new()),
            ..AppSettings::default()
        };

        assert_eq!(
            settings.capture_output_directory_or(PathBuf::from("fallback")),
            PathBuf::from("fallback")
        );
    }

    #[test]
    fn settings_store_round_trips_json_file() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-core-settings-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        let store = SettingsStore::new(root.join("settings.json"));
        let settings = AppSettings {
            capture_output_directory: Some(root.join("captures")),
            copy_captures_to_clipboard: false,
            save_history: false,
            capture_image_format: CaptureImageFormat::Jpeg,
            jpeg_quality: 70,
            save_in_monthly_folders: false,
            file_name_template: "Screenshot_{date}".into(),
            recording_format: RecordingFormat::WebM,
            recording_quality: RecordingQuality::P720,
            recording_fps: 60,
            gif_fps: 24,
            record_microphone: true,
            record_desktop_audio: false,
            microphone_device_id: Some("mic".into()),
            desktop_audio_device_id: Some("desktop".into()),
            capture_hotkey: "Ctrl+Shift+S".into(),
            recording_hotkey: Some("Alt+R".into()),
            start_with_windows: false,
            auto_check_for_updates: false,
            capture_delay_seconds: 5,
            mute_sounds: true,
            disable_animations: true,
            ui_scale: 1.25,
            show_crosshair_guides: true,
            show_cursor: true,
            show_capture_magnifier: false,
            overlay_capture_all_monitors: false,
            detect_windows: false,
            default_capture_mode: DefaultCaptureMode::ActiveWindow,
            last_capture_mode: DefaultCaptureMode::ColorPicker,
            toast_position: ToastPosition::TopRight,
            toast_button_layout: Some(serde_json::json!({"Copy": false})),
            ..AppSettings::default()
        };

        store.save(&settings).expect("save settings");
        let loaded = store.load_or_default().expect("load settings");

        assert_eq!(loaded, settings);
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn settings_store_missing_file_uses_defaults() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-core-settings-missing-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        let store = SettingsStore::new(root.join("settings.json"));

        let loaded = store.load_or_default().expect("load defaults");

        assert_eq!(loaded, AppSettings::default());
    }

    #[test]
    fn advanced_settings_round_trip_json() {
        let mut settings = AppSettings {
            ocr_hotkey: Some("Alt+Shift+O".into()),
            ocr_language_tag: "en-US".into(),
            translation_model: 1,
            image_upload_destination: "Imgur".into(),
            image_upload_settings: Some(serde_json::json!({"ClientId": "abc"})),
            enabled_tools: Some(vec!["rect".into(), "ocr".into()]),
            ..AppSettings::default()
        };
        settings
            .open_with_apps
            .insert("paint".into(), "mspaint.exe".into());
        settings.tool_hotkeys.insert("arrow".into(), vec![0, 49]);

        let json = serde_json::to_string(&settings).expect("serialize settings");
        let loaded: AppSettings = serde_json::from_str(&json).expect("deserialize settings");

        assert_eq!(loaded, settings);
        assert_eq!(
            loaded
                .image_upload_settings
                .expect("image upload settings should round trip")["ClientId"],
            "abc"
        );
    }

    #[test]
    fn capture_image_format_uses_pascal_case_json() {
        let settings = AppSettings {
            capture_output_directory: None,
            capture_image_format: CaptureImageFormat::Bmp,
            ..AppSettings::default()
        };

        let json = serde_json::to_string(&settings).expect("serialize settings");
        let loaded: AppSettings = serde_json::from_str(&json).expect("deserialize settings");

        assert!(json.contains(r#""capture_image_format":"Bmp""#));
        assert_eq!(loaded.capture_image_format, CaptureImageFormat::Bmp);
    }

    #[test]
    fn recording_format_extensions_match_legacy_outputs() {
        assert_eq!(RecordingFormat::Gif.extension(), "gif");
        assert_eq!(RecordingFormat::Mp4.extension(), "mp4");
        assert_eq!(RecordingFormat::WebM.extension(), "webm");
        assert_eq!(RecordingFormat::Mkv.extension(), "mkv");
        assert_eq!(RecordingQuality::P1080.max_height(), Some(1080));
        assert_eq!(RecordingQuality::Original.max_height(), None);
    }

    #[test]
    fn capture_mode_and_toast_position_labels_are_stable() {
        assert_eq!(DefaultCaptureMode::Rectangle.label(), "Rectangle");
        assert_eq!(DefaultCaptureMode::ActiveWindow.label(), "Active window");
        assert_eq!(ToastPosition::Right.label(), "Right");
        assert_eq!(ToastPosition::TopLeft.label(), "Top left");
    }

    #[test]
    fn app_settings_defaults_save_naming_preferences() {
        let settings = AppSettings::default();

        assert!(settings.save_in_monthly_folders);
        assert_eq!(settings.file_name_template, DEFAULT_FILE_NAME_TEMPLATE);
    }

    #[test]
    fn normalize_file_name_template_maps_legacy_default() {
        assert_eq!(
            normalize_file_name_template("oddsnap_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}"),
            DEFAULT_FILE_NAME_TEMPLATE
        );
    }
}
