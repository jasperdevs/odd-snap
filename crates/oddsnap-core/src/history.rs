use std::{
    fs,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use serde::{Deserialize, Serialize};
use thiserror::Error;

#[derive(Debug, Error)]
pub enum HistoryStoreError {
    #[error("failed to read history: {0}")]
    Read(#[source] std::io::Error),
    #[error("failed to write history: {0}")]
    Write(#[source] std::io::Error),
    #[error("failed to parse history: {0}")]
    Parse(#[source] serde_json::Error),
    #[error("failed to serialize history: {0}")]
    Serialize(#[source] serde_json::Error),
    #[error("history file path has no file name")]
    MissingFileName,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum HistoryKind {
    Image,
    Gif,
    Video,
    Sticker,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct HistoryEntry {
    pub file_path: PathBuf,
    pub file_name: String,
    pub captured_at_unix_ms: u64,
    pub width: u32,
    pub height: u32,
    pub file_size_bytes: u64,
    pub kind: HistoryKind,
    #[serde(default)]
    pub upload_url: Option<String>,
    #[serde(default)]
    pub upload_provider: Option<String>,
    #[serde(default)]
    pub upload_error: Option<String>,
    #[serde(default)]
    pub thumbnail_path: Option<PathBuf>,
}

impl HistoryEntry {
    pub fn from_capture_file(
        file_path: PathBuf,
        width: u32,
        height: u32,
        kind: HistoryKind,
    ) -> Result<Self, HistoryStoreError> {
        let file_name = file_path
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or(HistoryStoreError::MissingFileName)?
            .to_string();
        let file_size_bytes = fs::metadata(&file_path)
            .map_err(HistoryStoreError::Read)?
            .len();

        Ok(Self {
            file_path,
            file_name,
            captured_at_unix_ms: unix_millis_now(),
            width,
            height,
            file_size_bytes,
            kind,
            upload_url: None,
            upload_provider: None,
            upload_error: None,
            thumbnail_path: None,
        })
    }
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct HistoryIndex {
    #[serde(default)]
    pub entries: Vec<HistoryEntry>,
}

#[derive(Debug, Clone)]
pub struct HistoryStore {
    path: PathBuf,
}

impl HistoryStore {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn load_or_default(&self) -> Result<HistoryIndex, HistoryStoreError> {
        if !self.path.exists() {
            return Ok(HistoryIndex::default());
        }

        let bytes = fs::read(&self.path).map_err(HistoryStoreError::Read)?;
        serde_json::from_slice(&bytes).map_err(HistoryStoreError::Parse)
    }

    pub fn save(&self, index: &HistoryIndex) -> Result<(), HistoryStoreError> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).map_err(HistoryStoreError::Write)?;
        }

        let bytes = serde_json::to_vec_pretty(index).map_err(HistoryStoreError::Serialize)?;
        fs::write(&self.path, bytes).map_err(HistoryStoreError::Write)
    }

    pub fn append_entry(&self, entry: HistoryEntry) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index
            .entries
            .retain(|existing| existing.file_path != entry.file_path);
        index.entries.insert(0, entry);
        self.save(&index)?;
        Ok(index)
    }
}

pub fn default_history_path() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(profile) = std::env::var_os("USERPROFILE") {
            return PathBuf::from(profile)
                .join("Pictures")
                .join("OddSnap History")
                .join("rust-history.json");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join("Pictures")
                .join("OddSnap History")
                .join("rust-history.json");
        }
    }

    std::env::temp_dir()
        .join("OddSnap History")
        .join("rust-history.json")
}

fn unix_millis_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64)
        .unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use std::fs;

    use super::{HistoryEntry, HistoryKind, HistoryStore};

    #[test]
    fn history_entry_reads_file_metadata() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-history-entry-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let image = root.join("capture.bmp");
        fs::write(&image, b"BMhistory").expect("write capture");

        let entry = HistoryEntry::from_capture_file(image.clone(), 12, 34, HistoryKind::Image)
            .expect("build history entry");

        assert_eq!(entry.file_path, image);
        assert_eq!(entry.file_name, "capture.bmp");
        assert_eq!(entry.width, 12);
        assert_eq!(entry.height, 34);
        assert_eq!(entry.file_size_bytes, 9);
        assert_eq!(entry.kind, HistoryKind::Image);
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_appends_newest_first_and_dedupes_paths() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-history-store-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));
        let first_path = root.join("first.bmp");
        let second_path = root.join("second.bmp");
        fs::write(&first_path, b"first").expect("write first");
        fs::write(&second_path, b"second").expect("write second");
        let first = HistoryEntry::from_capture_file(first_path.clone(), 1, 1, HistoryKind::Image)
            .expect("build first history entry");
        let second = HistoryEntry::from_capture_file(second_path, 2, 2, HistoryKind::Image)
            .expect("build second history entry");
        let updated_first =
            HistoryEntry::from_capture_file(first_path.clone(), 3, 3, HistoryKind::Image)
                .expect("build updated first history entry");

        store.append_entry(first).expect("append first");
        store.append_entry(second).expect("append second");
        let index = store
            .append_entry(updated_first)
            .expect("append updated first");

        assert_eq!(index.entries.len(), 2);
        assert_eq!(index.entries[0].file_path, first_path);
        assert_eq!(index.entries[0].width, 3);
        assert_eq!(index.entries[1].file_name, "second.bmp");
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_missing_file_uses_empty_index() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-history-missing-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        let store = HistoryStore::new(root.join("history.json"));

        let index = store.load_or_default().expect("load missing history");

        assert!(index.entries.is_empty());
    }
}
