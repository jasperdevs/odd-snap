use std::{
    fs,
    io::BufWriter,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use image::{codecs::jpeg::JpegEncoder, ImageFormat};
use oddsnap_core::{
    CaptureImageFormat, NativeUiProfile, PlatformCapabilities, RecordingFormat, RecordingQuality,
};
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
pub struct CaptureRequest {
    pub region: CaptureRegion,
    pub include_cursor: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ColorSample {
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

impl ColorSample {
    pub fn hex_rgb(self) -> String {
        format!("#{:02X}{:02X}{:02X}", self.r, self.g, self.b)
    }

    pub fn bare_hex_rgb(self) -> String {
        format!("{:02X}{:02X}{:02X}", self.r, self.g, self.b)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WindowInfo {
    pub id: String,
    pub title: String,
    pub bounds: CaptureRegion,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VideoRecordingRequest {
    pub output_path: PathBuf,
    pub region: Option<CaptureRegion>,
    pub format: RecordingFormat,
    pub quality: RecordingQuality,
    pub fps: u32,
    pub record_microphone: bool,
    pub record_desktop_audio: bool,
    pub microphone_device_id: Option<String>,
    pub desktop_audio_device_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VideoRecordingResult {
    pub output_path: PathBuf,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OverlayWindowRequest {
    pub bounds: CaptureRegion,
    pub opacity: u8,
    pub click_through: bool,
    pub show_crosshair_guides: bool,
}

pub trait PlatformAdapter: Send + Sync {
    fn name(&self) -> &'static str;
    fn native_ui_profile(&self) -> NativeUiProfile;
    fn capabilities(&self) -> PlatformCapabilities;
}

pub trait ScreenCaptureService: Send + Sync {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError>;
    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError>;
    fn capture_region_with_options(
        &self,
        request: CaptureRequest,
    ) -> Result<CaptureResult, PlatformError> {
        self.capture_region(request.region)
    }

    fn capture_all_screens(&self) -> Result<CaptureResult, PlatformError> {
        let monitors = self.monitors()?;
        let region = virtual_screen_region(&monitors)
            .ok_or_else(|| PlatformError::Failed("no monitors available for capture".into()))?;
        self.capture_region(region)
    }

    fn capture_all_screens_with_cursor(
        &self,
        include_cursor: bool,
    ) -> Result<CaptureResult, PlatformError> {
        let monitors = self.monitors()?;
        let region = virtual_screen_region(&monitors)
            .ok_or_else(|| PlatformError::Failed("no monitors available for capture".into()))?;
        self.capture_region_with_options(CaptureRequest {
            region,
            include_cursor,
        })
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

pub trait ClipboardTextService: Send + Sync {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError>;
}

pub trait ColorPickerService: Send + Sync {
    fn sample_cursor_color(&self) -> Result<ColorSample, PlatformError>;
}

pub trait VideoRecordingHandle: Send {
    fn output_path(&self) -> &Path;
    fn stop(&mut self) -> Result<VideoRecordingResult, PlatformError>;
    fn cancel(&mut self);
}

pub trait VideoRecordingService: Send + Sync {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn VideoRecordingHandle>, PlatformError>;
}

pub trait OverlayWindowHandle: Send {
    fn native_window_handle(&self) -> isize;
}

pub trait RegionOverlayService: Send + Sync {
    fn create_overlay_window(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Box<dyn OverlayWindowHandle>, PlatformError>;
}

pub trait RegionSelectionService: Send + Sync {
    fn select_region(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Option<CaptureRegion>, PlatformError>;
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

pub fn persist_capture_to_directory_as(
    capture: &CaptureResult,
    output_dir: &Path,
    format: CaptureImageFormat,
    jpeg_quality: u8,
) -> Result<CaptureResult, PlatformError> {
    fs::create_dir_all(output_dir).map_err(|source| {
        PlatformError::Failed(format!("failed to create capture directory: {source}"))
    })?;

    let destination = unique_capture_path(output_dir, format.extension());
    persist_capture_to_path_as(capture, &destination, format, jpeg_quality)
}

pub fn persist_capture_to_path_as(
    capture: &CaptureResult,
    destination: &Path,
    format: CaptureImageFormat,
    jpeg_quality: u8,
) -> Result<CaptureResult, PlatformError> {
    if let Some(parent) = destination.parent() {
        fs::create_dir_all(parent).map_err(|source| {
            PlatformError::Failed(format!("failed to create capture directory: {source}"))
        })?;
    }
    save_capture_file_as(&capture.image_path, destination, format, jpeg_quality)?;

    Ok(CaptureResult {
        image_path: destination.to_path_buf(),
        region: capture.region.clone(),
    })
}

fn save_capture_file_as(
    source: &Path,
    destination: &Path,
    format: CaptureImageFormat,
    jpeg_quality: u8,
) -> Result<(), PlatformError> {
    if format == CaptureImageFormat::Bmp
        && source
            .extension()
            .and_then(|extension| extension.to_str())
            .is_some_and(|extension| extension.eq_ignore_ascii_case("bmp"))
    {
        fs::copy(source, destination)
            .map_err(|source| PlatformError::Failed(format!("failed to save capture: {source}")))?;
        return Ok(());
    }

    let image = image::open(source)
        .map_err(|source| PlatformError::Failed(format!("failed to decode capture: {source}")))?;

    match format {
        CaptureImageFormat::Png => image
            .save_with_format(destination, ImageFormat::Png)
            .map_err(|source| {
                PlatformError::Failed(format!("failed to save PNG capture: {source}"))
            }),
        CaptureImageFormat::Bmp => image
            .save_with_format(destination, ImageFormat::Bmp)
            .map_err(|source| {
                PlatformError::Failed(format!("failed to save BMP capture: {source}"))
            }),
        CaptureImageFormat::Jpeg => {
            let file = fs::File::create(destination).map_err(|source| {
                PlatformError::Failed(format!("failed to create JPEG capture: {source}"))
            })?;
            let rgb = image.to_rgb8();
            let mut encoder =
                JpegEncoder::new_with_quality(BufWriter::new(file), jpeg_quality.clamp(1, 100));
            encoder.encode_image(&rgb).map_err(|source| {
                PlatformError::Failed(format!("failed to save JPEG capture: {source}"))
            })
        }
    }
}

pub trait HotkeyService: Send + Sync {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError>;
}

pub trait TrayService: Send + Sync {
    fn install_tray_icon(&self) -> Result<(), PlatformError>;
}

pub trait ScreenshotExclusionService: Send + Sync {
    fn set_window_capture_excluded(
        &self,
        native_window_handle: isize,
        excluded: bool,
    ) -> Result<(), PlatformError>;
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
        persist_capture_to_directory, persist_capture_to_directory_as, persist_capture_to_path_as,
        virtual_screen_region, CaptureRegion, CaptureResult, ColorSample, MonitorInfo,
    };
    use oddsnap_core::CaptureImageFormat;

    #[test]
    fn color_sample_formats_hash_and_bare_hex() {
        let sample = ColorSample {
            r: 3,
            g: 172,
            b: 255,
        };

        assert_eq!(sample.hex_rgb(), "#03ACFF");
        assert_eq!(sample.bare_hex_rgb(), "03ACFF");
    }

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

    #[test]
    fn persist_capture_to_directory_as_writes_requested_formats() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-platform-format-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp test root");
        let source = root.join("source.bmp");
        let output = root.join("saved");
        let image = image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
            2,
            1,
            image::Rgba([200, 40, 20, 255]),
        ));
        image
            .save_with_format(&source, image::ImageFormat::Bmp)
            .expect("write source bmp");
        let capture = CaptureResult {
            image_path: source,
            region: CaptureRegion {
                x: 0,
                y: 0,
                width: 2,
                height: 1,
            },
        };

        for (format, extension) in [
            (CaptureImageFormat::Png, "png"),
            (CaptureImageFormat::Jpeg, "jpg"),
            (CaptureImageFormat::Bmp, "bmp"),
        ] {
            let saved =
                persist_capture_to_directory_as(&capture, &output, format, 85).expect("persist");

            assert_eq!(
                saved.image_path.extension(),
                Some(std::ffi::OsStr::new(extension))
            );
            assert!(saved.image_path.exists());
            assert_eq!(saved.region, capture.region);
        }

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn persist_capture_to_path_as_uses_exact_destination_path() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-platform-path-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp test root");
        let source = root.join("source.bmp");
        let destination = root.join("2026-01").join("Custom.png");
        let image = image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
            1,
            1,
            image::Rgba([10, 20, 30, 255]),
        ));
        image
            .save_with_format(&source, image::ImageFormat::Bmp)
            .expect("write source bmp");
        let capture = CaptureResult {
            image_path: source,
            region: CaptureRegion {
                x: 0,
                y: 0,
                width: 1,
                height: 1,
            },
        };

        let saved = persist_capture_to_path_as(&capture, &destination, CaptureImageFormat::Png, 85)
            .expect("persist to path");

        assert_eq!(saved.image_path, destination);
        assert!(saved.image_path.exists());
        let _ = fs::remove_dir_all(root);
    }
}
