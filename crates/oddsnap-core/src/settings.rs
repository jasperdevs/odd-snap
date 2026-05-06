use serde::{Deserialize, Serialize};

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
