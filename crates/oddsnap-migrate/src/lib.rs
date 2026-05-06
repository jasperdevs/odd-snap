use std::env;
use std::fs;
use std::path::{Path, PathBuf};

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

#[cfg(test)]
mod tests {
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::{read_legacy_settings, LegacyOddSnapPaths};

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
}
