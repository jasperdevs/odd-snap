use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use oddsnap_core::{
    normalize_file_name_template, AppSettings, CaptureImageFormat, HistoryEntry, HistoryIndex,
    HistoryKind,
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

    settings
}

pub fn import_existing_history(paths: &LegacyOddSnapPaths) -> Result<HistoryIndex, MigrationError> {
    if paths.current_history_database_path.exists() {
        let index = import_history_database(&paths.current_history_database_path)?;
        if !index.entries.is_empty() {
            return Ok(index);
        }
    }

    let json_paths = [
        paths.current_history_index_path.clone(),
        paths.history_dir.join("index.json"),
    ];
    for path in json_paths {
        if path.exists() {
            let index = import_history_json(&path)?;
            if !index.entries.is_empty() {
                return Ok(index);
            }
        }
    }

    Ok(HistoryIndex::default())
}

fn import_history_database(path: &Path) -> Result<HistoryIndex, MigrationError> {
    let connection =
        Connection::open(path).map_err(|source| MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        })?;
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

    let mut entries = Vec::new();
    for row in rows {
        let entry = row.map_err(|source| MigrationError::ReadHistoryDatabase {
            path: path.to_path_buf(),
            source,
        })?;
        if is_supported_history_file(&entry.file_path) && entry.file_path.exists() {
            entries.push(entry);
        }
    }

    Ok(HistoryIndex { entries })
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

    Ok(HistoryIndex { entries })
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

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::{import_app_settings, read_legacy_settings, LegacyOddSnapPaths};
    use oddsnap_core::CaptureImageFormat;

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
            r#"{"SaveDirectory":"C:\\Users\\test\\Pictures\\OddSnap","SaveHistory":false,"AfterCapture":2,"CaptureImageFormat":1,"JpegQuality":92,"SaveInMonthlyFolders":false,"FileNameTemplate":"Screenshot_{date}"}"#,
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
                );",
            )
            .expect("create table");
        connection
            .execute(
                "INSERT INTO history_entries(file_path, file_name, captured_at_ticks, width, height, file_size_bytes, kind, upload_url, upload_provider, upload_error)
                 VALUES (?1, 'capture.png', 123, 12, 34, 3, 0, NULL, NULL, NULL)",
                [image.display().to_string()],
            )
            .expect("insert row");

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
        let _ = fs::remove_dir_all(root);
    }
}
