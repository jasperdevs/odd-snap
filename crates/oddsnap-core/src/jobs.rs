use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AppJobArea {
    Runtime,
    Indexing,
    Upload,
    SettingsMigration,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AppJobSnapshot {
    pub key: String,
    pub label: String,
    pub area: AppJobArea,
    pub is_running: bool,
    pub status: String,
    pub last_succeeded: Option<bool>,
    pub last_error: Option<String>,
}

impl AppJobSnapshot {
    pub fn pending(key: impl Into<String>, label: impl Into<String>, area: AppJobArea) -> Self {
        Self {
            key: key.into(),
            label: label.into(),
            area,
            is_running: false,
            status: "Pending".into(),
            last_succeeded: None,
            last_error: None,
        }
    }
}
