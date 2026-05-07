use std::{
    collections::HashSet,
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ColorHistoryEntry {
    pub hex: String,
    pub captured_at_unix_ms: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OcrHistoryEntry {
    pub text: String,
    pub captured_at_unix_ms: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CodeHistoryEntry {
    pub text: String,
    pub format: String,
    pub captured_at_unix_ms: u64,
}

impl ColorHistoryEntry {
    pub fn new(hex: impl Into<String>) -> Self {
        Self {
            hex: hex.into(),
            captured_at_unix_ms: unix_millis_now(),
        }
    }
}

impl OcrHistoryEntry {
    pub fn new(text: impl Into<String>) -> Self {
        Self {
            text: text.into(),
            captured_at_unix_ms: unix_millis_now(),
        }
    }
}

impl CodeHistoryEntry {
    pub fn new(text: impl Into<String>, format: impl Into<String>) -> Self {
        Self {
            text: text.into(),
            format: format.into(),
            captured_at_unix_ms: unix_millis_now(),
        }
    }
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
    #[serde(default)]
    pub colors: Vec<ColorHistoryEntry>,
    #[serde(default)]
    pub ocr_entries: Vec<OcrHistoryEntry>,
    #[serde(default)]
    pub code_entries: Vec<CodeHistoryEntry>,
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

    pub fn remove_entry(&self, file_path: &Path) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index
            .entries
            .retain(|existing| existing.file_path != file_path);
        self.save(&index)?;
        Ok(index)
    }

    pub fn remove_entries<I, P>(&self, file_paths: I) -> Result<HistoryIndex, HistoryStoreError>
    where
        I: IntoIterator<Item = P>,
        P: AsRef<Path>,
    {
        let paths = file_paths
            .into_iter()
            .map(|path| path.as_ref().to_path_buf())
            .collect::<HashSet<_>>();
        let mut index = self.load_or_default()?;
        if paths.is_empty() {
            return Ok(index);
        }

        index
            .entries
            .retain(|existing| !paths.contains(&existing.file_path));
        self.save(&index)?;
        Ok(index)
    }

    pub fn update_entry_upload(
        &self,
        file_path: &Path,
        upload_url: Option<String>,
        upload_provider: Option<String>,
        upload_error: Option<String>,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        if let Some(entry) = index
            .entries
            .iter_mut()
            .find(|entry| entry.file_path == file_path)
        {
            entry.upload_url = upload_url;
            entry.upload_provider = upload_provider;
            entry.upload_error = upload_error;
            self.save(&index)?;
        }
        Ok(index)
    }

    pub fn append_color_entry(
        &self,
        entry: ColorHistoryEntry,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.colors.insert(0, entry);
        index.colors.truncate(200);
        self.save(&index)?;
        Ok(index)
    }

    pub fn remove_color_entry(
        &self,
        hex: &str,
        captured_at_unix_ms: u64,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.colors.retain(|existing| {
            !(existing.hex.eq_ignore_ascii_case(hex)
                && existing.captured_at_unix_ms == captured_at_unix_ms)
        });
        self.save(&index)?;
        Ok(index)
    }

    pub fn append_ocr_entry(
        &self,
        entry: OcrHistoryEntry,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.ocr_entries.insert(0, entry);
        index.ocr_entries.truncate(500);
        self.save(&index)?;
        Ok(index)
    }

    pub fn remove_ocr_entry(
        &self,
        text: &str,
        captured_at_unix_ms: u64,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.ocr_entries.retain(|existing| {
            !(existing.text == text && existing.captured_at_unix_ms == captured_at_unix_ms)
        });
        self.save(&index)?;
        Ok(index)
    }

    pub fn append_code_entry(
        &self,
        entry: CodeHistoryEntry,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.code_entries.retain(|existing| {
            !(existing.text == entry.text && existing.format.eq_ignore_ascii_case(&entry.format))
        });
        index.code_entries.insert(0, entry);
        index.code_entries.truncate(200);
        self.save(&index)?;
        Ok(index)
    }

    pub fn remove_code_entry(
        &self,
        text: &str,
        format: &str,
        captured_at_unix_ms: u64,
    ) -> Result<HistoryIndex, HistoryStoreError> {
        let mut index = self.load_or_default()?;
        index.code_entries.retain(|existing| {
            !(existing.text == text
                && existing.format.eq_ignore_ascii_case(format)
                && existing.captured_at_unix_ms == captured_at_unix_ms)
        });
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

    use super::{
        CodeHistoryEntry, ColorHistoryEntry, HistoryEntry, HistoryKind, HistoryStore,
        OcrHistoryEntry,
    };

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
    fn history_store_removes_entries_without_deleting_files() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-history-remove-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));
        let first_path = root.join("first.bmp");
        let second_path = root.join("second.bmp");
        fs::write(&first_path, b"first").expect("write first");
        fs::write(&second_path, b"second").expect("write second");
        let first = HistoryEntry::from_capture_file(first_path.clone(), 1, 1, HistoryKind::Image)
            .expect("build first history entry");
        let second = HistoryEntry::from_capture_file(second_path.clone(), 2, 2, HistoryKind::Image)
            .expect("build second history entry");

        store.append_entry(first).expect("append first");
        store.append_entry(second).expect("append second");
        let index = store.remove_entry(&first_path).expect("remove first");

        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.entries[0].file_path, second_path);
        assert!(first_path.exists());
        assert!(second_path.exists());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_removes_multiple_entries_without_deleting_files() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-history-remove-bulk-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));
        let first_path = root.join("first.bmp");
        let second_path = root.join("second.bmp");
        let third_path = root.join("third.bmp");
        fs::write(&first_path, b"first").expect("write first");
        fs::write(&second_path, b"second").expect("write second");
        fs::write(&third_path, b"third").expect("write third");

        for path in [&first_path, &second_path, &third_path] {
            let entry = HistoryEntry::from_capture_file(path.clone(), 1, 1, HistoryKind::Image)
                .expect("build history entry");
            store.append_entry(entry).expect("append");
        }

        let index = store
            .remove_entries([first_path.as_path(), third_path.as_path()])
            .expect("remove entries");

        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.entries[0].file_path, second_path);
        assert!(first_path.exists());
        assert!(second_path.exists());
        assert!(third_path.exists());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_updates_upload_metadata_without_renaming_entry() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-history-upload-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));
        let image = root.join("capture.png");
        fs::write(&image, b"png").expect("write capture");
        let entry = HistoryEntry::from_capture_file(image.clone(), 1, 1, HistoryKind::Image)
            .expect("build history entry");
        let original_name = entry.file_name.clone();
        store.append_entry(entry).expect("append");

        let index = store
            .update_entry_upload(
                &image,
                Some("https://files.example.test/capture.png".into()),
                Some("Catbox".into()),
                None,
            )
            .expect("update upload metadata");

        assert_eq!(index.entries[0].file_name, original_name);
        assert_eq!(
            index.entries[0].upload_url.as_deref(),
            Some("https://files.example.test/capture.png")
        );
        assert_eq!(index.entries[0].upload_provider.as_deref(), Some("Catbox"));
        assert_eq!(index.entries[0].upload_error, None);
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_appends_color_entries_newest_first_and_caps_list() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-color-history-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));

        for index in 0..205 {
            store
                .append_color_entry(ColorHistoryEntry::new(format!("{index:06X}")))
                .expect("append color");
        }

        let index = store.load_or_default().expect("load history");
        assert_eq!(index.colors.len(), 200);
        assert_eq!(index.colors[0].hex, "0000CC");
        assert_eq!(index.colors[199].hex, "000005");
        assert!(index.entries.is_empty());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_appends_ocr_entries_newest_first_and_caps_list() {
        let root = std::env::temp_dir().join(format!("oddsnap-ocr-history-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));

        for index in 0..505 {
            store
                .append_ocr_entry(OcrHistoryEntry::new(format!("text {index}")))
                .expect("append OCR");
        }

        let index = store.load_or_default().expect("load history");
        assert_eq!(index.ocr_entries.len(), 500);
        assert_eq!(index.ocr_entries[0].text, "text 504");
        assert_eq!(index.ocr_entries[499].text, "text 5");
        assert!(index.entries.is_empty());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn history_store_removes_generated_entries_by_stable_identity() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-generated-history-remove-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let store = HistoryStore::new(root.join("history.json"));

        let mut color = ColorHistoryEntry::new("AABBCC");
        color.captured_at_unix_ms = 10;
        let mut other_color = ColorHistoryEntry::new("AABBCC");
        other_color.captured_at_unix_ms = 11;
        let mut ocr = OcrHistoryEntry::new("recognized text");
        ocr.captured_at_unix_ms = 20;
        let mut other_ocr = OcrHistoryEntry::new("recognized text");
        other_ocr.captured_at_unix_ms = 21;
        let mut code = CodeHistoryEntry::new("https://example.test", "QR_CODE");
        code.captured_at_unix_ms = 30;
        let mut other_code = CodeHistoryEntry::new("https://example.test", "QR_CODE");
        other_code.captured_at_unix_ms = 31;

        store.append_color_entry(color).expect("append color");
        store
            .append_color_entry(other_color)
            .expect("append other color");
        store.append_ocr_entry(ocr).expect("append OCR");
        store.append_ocr_entry(other_ocr).expect("append other OCR");
        store.append_code_entry(code).expect("append code");
        store
            .append_code_entry(other_code)
            .expect("append other code");

        store
            .remove_color_entry("aabbcc", 10)
            .expect("remove color");
        store
            .remove_ocr_entry("recognized text", 20)
            .expect("remove OCR");
        let index = store
            .remove_code_entry("https://example.test", "qr_code", 30)
            .expect("remove code");

        assert_eq!(index.colors.len(), 1);
        assert_eq!(index.colors[0].captured_at_unix_ms, 11);
        assert_eq!(index.ocr_entries.len(), 1);
        assert_eq!(index.ocr_entries[0].captured_at_unix_ms, 21);
        assert_eq!(index.code_entries.len(), 1);
        assert_eq!(index.code_entries[0].captured_at_unix_ms, 31);
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
