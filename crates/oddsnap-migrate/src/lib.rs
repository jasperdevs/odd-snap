use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use oddsnap_core::AppSettings;
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
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LegacyOddSnapPaths {
    pub roaming_dir: PathBuf,
    pub settings_path: PathBuf,
    pub history_dir: PathBuf,
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

    settings
}

fn legacy_after_capture_copies(value: &Value) -> bool {
    match value {
        Value::Number(number) => number.as_u64().is_none_or(|index| index != 2),
        Value::String(name) => !name.eq_ignore_ascii_case("PreviewOnly"),
        _ => AppSettings::default().copy_captures_to_clipboard,
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::{import_app_settings, read_legacy_settings, LegacyOddSnapPaths};

    #[test]
    fn builds_paths_from_roaming_directory() {
        let paths = LegacyOddSnapPaths::from_roaming_dir("C:/Users/test/AppData/Roaming/OddSnap");

        assert!(paths.settings_path.ends_with("settings.json"));
        assert!(paths.history_dir.ends_with("history"));
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
            r#"{"SaveDirectory":"C:\\Users\\test\\Pictures\\OddSnap","SaveHistory":false,"AfterCapture":2}"#,
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
}
