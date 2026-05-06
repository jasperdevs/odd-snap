use std::path::PathBuf;

use oddsnap_core::{NativeUiProfile, PlatformCapabilities};
use thiserror::Error;

#[derive(Debug, Error)]
pub enum PlatformError {
    #[error("{0}")]
    Unsupported(&'static str),
    #[error("{0}")]
    Failed(String),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MonitorInfo {
    pub id: String,
    pub name: String,
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
    pub scale_percent: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CaptureRegion {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CaptureResult {
    pub image_path: PathBuf,
    pub region: CaptureRegion,
}

pub trait PlatformAdapter: Send + Sync {
    fn name(&self) -> &'static str;
    fn native_ui_profile(&self) -> NativeUiProfile;
    fn capabilities(&self) -> PlatformCapabilities;
}

pub trait ScreenCaptureService: Send + Sync {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError>;
    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError>;
}

pub trait HotkeyService: Send + Sync {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError>;
}

pub trait TrayService: Send + Sync {
    fn install_tray_icon(&self) -> Result<(), PlatformError>;
}

pub trait PermissionsService: Send + Sync {
    fn missing_permissions(&self) -> Vec<String>;
}
