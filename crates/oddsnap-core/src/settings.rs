use std::{
    fs,
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};
use thiserror::Error;

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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
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
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            capture_output_directory: None,
            copy_captures_to_clipboard: true,
            save_history: true,
            capture_image_format: CaptureImageFormat::Png,
            jpeg_quality: default_jpeg_quality(),
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

    use super::{AppSettings, CaptureImageFormat, SettingsStore};

    #[test]
    fn app_settings_defaults_copy_captures_to_clipboard() {
        let settings = AppSettings::default();

        assert!(settings.copy_captures_to_clipboard);
        assert!(settings.save_history);
        assert_eq!(settings.capture_image_format, CaptureImageFormat::Png);
        assert_eq!(settings.jpeg_quality, 85);
        assert!(settings.capture_output_directory.is_none());
    }

    #[test]
    fn capture_output_directory_uses_fallback_when_empty() {
        let settings = AppSettings {
            capture_output_directory: Some(PathBuf::new()),
            copy_captures_to_clipboard: true,
            save_history: true,
            capture_image_format: CaptureImageFormat::Png,
            jpeg_quality: 85,
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
    fn capture_image_format_uses_pascal_case_json() {
        let settings = AppSettings {
            capture_output_directory: None,
            copy_captures_to_clipboard: true,
            save_history: true,
            capture_image_format: CaptureImageFormat::Bmp,
            jpeg_quality: 85,
        };

        let json = serde_json::to_string(&settings).expect("serialize settings");
        let loaded: AppSettings = serde_json::from_str(&json).expect("deserialize settings");

        assert!(json.contains(r#""capture_image_format":"Bmp""#));
        assert_eq!(loaded.capture_image_format, CaptureImageFormat::Bmp);
    }
}
