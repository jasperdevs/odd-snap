use std::{
    fs,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

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

pub fn default_capture_directory() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(profile) = std::env::var_os("USERPROFILE") {
            return PathBuf::from(profile).join("Pictures").join("OddSnap");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home).join("Pictures").join("OddSnap");
        }
    }

    std::env::temp_dir().join("OddSnap")
}

pub fn persist_capture_to_directory(
    capture: &CaptureResult,
    output_dir: &Path,
) -> Result<CaptureResult, PlatformError> {
    fs::create_dir_all(output_dir).map_err(|source| {
        PlatformError::Failed(format!("failed to create capture directory: {source}"))
    })?;

    let extension = capture
        .image_path
        .extension()
        .and_then(|extension| extension.to_str())
        .filter(|extension| !extension.is_empty())
        .unwrap_or("bmp");
    let destination = unique_capture_path(output_dir, extension);

    fs::copy(&capture.image_path, &destination)
        .map_err(|source| PlatformError::Failed(format!("failed to save capture: {source}")))?;

    Ok(CaptureResult {
        image_path: destination,
        region: capture.region.clone(),
    })
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

fn unique_capture_path(output_dir: &Path, extension: &str) -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    let base = format!(
        "OddSnap-{}-{:09}",
        duration.as_secs(),
        duration.subsec_nanos()
    );

    for suffix in 0..1000 {
        let file_name = if suffix == 0 {
            format!("{base}.{extension}")
        } else {
            format!("{base}-{suffix}.{extension}")
        };
        let candidate = output_dir.join(file_name);
        if !candidate.exists() {
            return candidate;
        }
    }

    output_dir.join(format!("{base}-{}.{}", std::process::id(), extension))
}

#[cfg(test)]
mod tests {
    use std::{fs, path::PathBuf};

    use super::{
        persist_capture_to_directory, virtual_screen_region, CaptureRegion, CaptureResult,
        MonitorInfo,
    };

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

    #[test]
    fn persist_capture_to_directory_copies_file_and_region() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-platform-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp test root");
        let source = root.join("source.bmp");
        let output = root.join("saved");
        fs::write(&source, b"BMtest").expect("write source capture");

        let capture = CaptureResult {
            image_path: source.clone(),
            region: CaptureRegion {
                x: -10,
                y: 20,
                width: 30,
                height: 40,
            },
        };

        let saved = persist_capture_to_directory(&capture, &output).expect("persist capture");

        assert_ne!(saved.image_path, source);
        assert_eq!(saved.region, capture.region);
        assert_eq!(saved.image_path.parent(), Some(output.as_path()));
        assert_eq!(
            saved.image_path.extension(),
            Some(std::ffi::OsStr::new("bmp"))
        );
        assert_eq!(
            fs::read(&saved.image_path).expect("read saved capture"),
            b"BMtest"
        );

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn persist_capture_to_directory_reports_missing_source() {
        let missing = PathBuf::from("does-not-exist.bmp");
        let output = std::env::temp_dir().join("oddsnap-platform-missing-source-test");
        let capture = CaptureResult {
            image_path: missing,
            region: CaptureRegion {
                x: 0,
                y: 0,
                width: 1,
                height: 1,
            },
        };

        let error = persist_capture_to_directory(&capture, &output).expect_err("missing source");

        assert!(error.to_string().contains("failed to save capture"));
        let _ = fs::remove_dir_all(output);
    }
}
