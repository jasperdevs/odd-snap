use std::path::{Path, PathBuf};

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

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WindowInfo {
    pub id: String,
    pub title: String,
    pub bounds: CaptureRegion,
}

pub trait PlatformAdapter: Send + Sync {
    fn name(&self) -> &'static str;
    fn native_ui_profile(&self) -> NativeUiProfile;
    fn capabilities(&self) -> PlatformCapabilities;
}

pub trait ScreenCaptureService: Send + Sync {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError>;
    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError>;

    fn capture_all_screens(&self) -> Result<CaptureResult, PlatformError> {
        let monitors = self.monitors()?;
        let region = virtual_screen_region(&monitors)
            .ok_or_else(|| PlatformError::Failed("no monitors available for capture".into()))?;
        self.capture_region(region)
    }
}

pub trait WindowPickerService: Send + Sync {
    fn active_window(&self) -> Result<WindowInfo, PlatformError>;
}

pub trait WindowCaptureService: ScreenCaptureService + WindowPickerService {
    fn capture_active_window(&self) -> Result<CaptureResult, PlatformError> {
        let window = self.active_window()?;
        self.capture_region(window.bounds)
    }
}

impl<T> WindowCaptureService for T where T: ScreenCaptureService + WindowPickerService {}

pub trait ClipboardImageService: Send + Sync {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError>;
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

pub fn virtual_screen_region(monitors: &[MonitorInfo]) -> Option<CaptureRegion> {
    let first = monitors.first()?;
    let mut left = first.x as i64;
    let mut top = first.y as i64;
    let mut right = left + first.width as i64;
    let mut bottom = top + first.height as i64;

    for monitor in &monitors[1..] {
        let monitor_left = monitor.x as i64;
        let monitor_top = monitor.y as i64;
        let monitor_right = monitor_left + monitor.width as i64;
        let monitor_bottom = monitor_top + monitor.height as i64;

        left = left.min(monitor_left);
        top = top.min(monitor_top);
        right = right.max(monitor_right);
        bottom = bottom.max(monitor_bottom);
    }

    let width = u32::try_from(right - left).ok()?;
    let height = u32::try_from(bottom - top).ok()?;
    Some(CaptureRegion {
        x: i32::try_from(left).ok()?,
        y: i32::try_from(top).ok()?,
        width,
        height,
    })
}

#[cfg(test)]
mod tests {
    use super::{virtual_screen_region, MonitorInfo};

    #[test]
    fn virtual_screen_region_combines_negative_and_positive_monitors() {
        let monitors = vec![
            MonitorInfo {
                id: "left".into(),
                name: "Left".into(),
                x: -1920,
                y: 0,
                width: 1920,
                height: 1080,
                scale_percent: 100,
            },
            MonitorInfo {
                id: "main".into(),
                name: "Main".into(),
                x: 0,
                y: -120,
                width: 2560,
                height: 1440,
                scale_percent: 100,
            },
        ];

        let region = virtual_screen_region(&monitors).expect("virtual region");

        assert_eq!(region.x, -1920);
        assert_eq!(region.y, -120);
        assert_eq!(region.width, 4480);
        assert_eq!(region.height, 1440);
    }
}
