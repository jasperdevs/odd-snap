use std::path::Path;
#[cfg(target_os = "macos")]
use std::{
    io::Write,
    path::PathBuf,
    process::{Command, Stdio},
    time::{SystemTime, UNIX_EPOCH},
};

use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService,
    ColorPickerService, ColorSample, HotkeyService, MonitorInfo, OverlayWindowHandle,
    OverlayWindowRequest, PlatformAdapter, PlatformError, RegionOverlayService,
    RegionSelectionService, ScreenCaptureService, VideoRecordingRequest, VideoRecordingService,
    WindowInfo, WindowPickerService,
};

#[derive(Debug, Default)]
pub struct MacosPlatform;

impl PlatformAdapter for MacosPlatform {
    fn name(&self) -> &'static str {
        "macOS"
    }

    fn native_ui_profile(&self) -> NativeUiProfile {
        NativeUiProfile::for_target("macos")
    }

    fn capabilities(&self) -> PlatformCapabilities {
        PlatformCapabilities {
            os: "macos".into(),
            items: vec![
                (
                    PlatformCapability::ScreenCapture,
                    CapabilityState::InProgress,
                ),
                (PlatformCapability::RegionOverlay, CapabilityState::Planned),
                (PlatformCapability::WindowCapture, CapabilityState::Planned),
                (
                    PlatformCapability::ScreenshotExclusion,
                    CapabilityState::Planned,
                ),
                (PlatformCapability::GlobalHotkeys, CapabilityState::Planned),
                (PlatformCapability::Tray, CapabilityState::Planned),
                (PlatformCapability::Clipboard, CapabilityState::InProgress),
                (PlatformCapability::FileDialogs, CapabilityState::Planned),
                (
                    PlatformCapability::MicrophoneAudio,
                    CapabilityState::Planned,
                ),
                (PlatformCapability::SystemAudio, CapabilityState::Planned),
                (PlatformCapability::Notifications, CapabilityState::Planned),
                (PlatformCapability::AutoStart, CapabilityState::Planned),
                (PlatformCapability::AppUpdater, CapabilityState::Planned),
            ],
        }
    }
}

impl ScreenCaptureService for MacosPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        Err(PlatformError::Unsupported(
            "macOS monitor enumeration is not implemented yet",
        ))
    }

    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        self.capture_region_with_options(CaptureRequest {
            region,
            include_cursor: false,
        })
    }

    fn capture_region_with_options(
        &self,
        request: CaptureRequest,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_screencapture(Some(&request.region), request.include_cursor)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "macOS region capture is only available on macOS",
            ))
        }
    }

    fn capture_all_screens_with_cursor(
        &self,
        include_cursor: bool,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_screencapture(None, include_cursor)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = include_cursor;
            Err(PlatformError::Unsupported(
                "macOS full-screen capture is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn run_macos_screencapture(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    let output_path = macos_capture_output_path();
    let args = macos_screencapture_args(region, include_cursor, &output_path);
    let status = Command::new("screencapture")
        .args(&args)
        .status()
        .map_err(|source| {
            PlatformError::Failed(format!("failed to start macOS screencapture: {source}"))
        })?;
    if !status.success() {
        return Err(PlatformError::Failed(format!(
            "macOS screencapture exited with status {status}"
        )));
    }

    let capture_region = match region {
        Some(region) => region.clone(),
        None => {
            let (width, height) = oddsnap_platform::image_file_dimensions(&output_path)?;
            CaptureRegion {
                x: 0,
                y: 0,
                width,
                height,
            }
        }
    };

    Ok(CaptureResult {
        image_path: output_path,
        region: capture_region,
    })
}

#[cfg(any(target_os = "macos", test))]
fn macos_screencapture_args(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
    output_path: &Path,
) -> Vec<String> {
    let mut args = vec!["-x".to_string()];
    if include_cursor {
        args.push("-C".into());
    }
    if let Some(region) = region {
        args.push("-R".into());
        args.push(format!(
            "{},{},{},{}",
            region.x, region.y, region.width, region.height
        ));
    }
    args.push(output_path.display().to_string());
    args
}

#[cfg(target_os = "macos")]
fn macos_capture_output_path() -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-macos-capture-{}-{:09}.png",
        duration.as_secs(),
        duration.subsec_nanos()
    ))
}

impl WindowPickerService for MacosPlatform {
    fn active_window(&self) -> Result<WindowInfo, PlatformError> {
        Err(PlatformError::Unsupported(
            "macOS active-window detection is not implemented yet",
        ))
    }
}

impl ClipboardImageService for MacosPlatform {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError> {
        let _ = image_path;
        Err(PlatformError::Unsupported(
            "macOS image clipboard is not implemented yet",
        ))
    }
}

impl ClipboardTextService for MacosPlatform {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            copy_text_to_macos_clipboard(text)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = text;
            Err(PlatformError::Unsupported(
                "macOS text clipboard is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn copy_text_to_macos_clipboard(text: &str) -> Result<(), PlatformError> {
    let mut child = Command::new("pbcopy")
        .stdin(Stdio::piped())
        .spawn()
        .map_err(|source| PlatformError::Failed(format!("failed to start pbcopy: {source}")))?;

    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| PlatformError::Failed("failed to open pbcopy stdin".into()))?;
    stdin.write_all(text.as_bytes()).map_err(|source| {
        PlatformError::Failed(format!("failed to write clipboard text: {source}"))
    })?;
    drop(stdin);

    let status = child
        .wait()
        .map_err(|source| PlatformError::Failed(format!("failed to wait for pbcopy: {source}")))?;
    if status.success() {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "pbcopy exited with status {status}"
        )))
    }
}

impl ColorPickerService for MacosPlatform {
    fn sample_cursor_color(&self) -> Result<ColorSample, PlatformError> {
        Err(PlatformError::Unsupported(
            "macOS color picker is not implemented yet",
        ))
    }
}

impl HotkeyService for MacosPlatform {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError> {
        let _ = accelerator;
        Err(PlatformError::Unsupported(
            "macOS global hotkey registration is not implemented yet",
        ))
    }
}

impl VideoRecordingService for MacosPlatform {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn oddsnap_platform::VideoRecordingHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "macOS desktop recording is not implemented yet",
        ))
    }
}

impl RegionOverlayService for MacosPlatform {
    fn create_overlay_window(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Box<dyn OverlayWindowHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "macOS region overlay is not implemented yet",
        ))
    }
}

impl RegionSelectionService for MacosPlatform {
    fn select_region(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Option<CaptureRegion>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "macOS region selection is not implemented yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{NativeMaterial, RecordingFormat, RecordingQuality};
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, ColorPickerService, HotkeyService,
        OverlayWindowRequest, PlatformAdapter, RegionOverlayService, RegionSelectionService,
        ScreenCaptureService, VideoRecordingRequest, VideoRecordingService,
    };

    use super::MacosPlatform;

    #[test]
    fn macos_adapter_uses_liquid_glass_profile() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter.native_ui_profile().material,
            NativeMaterial::LiquidGlass
        );
    }

    #[test]
    fn macos_screen_capture_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::ScreenCapture),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_capture_services_report_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter.monitors().expect_err("macOS capture pending");

        assert!(error.to_string().contains("not implemented yet"));

        let error = adapter
            .capture_region(oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            })
            .expect_err("macOS capture wrong host");
        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_screencapture_args_include_region_and_cursor_options() {
        let path = std::path::Path::new("/tmp/oddsnap-test.png");

        let args = super::macos_screencapture_args(
            Some(&oddsnap_platform::CaptureRegion {
                x: -10,
                y: 20,
                width: 30,
                height: 40,
            }),
            true,
            path,
        );

        assert_eq!(
            args,
            vec!["-x", "-C", "-R", "-10,20,30,40", "/tmp/oddsnap-test.png"]
        );
    }

    #[test]
    #[ignore = "writes a screenshot file through the local macOS screencapture command"]
    #[cfg(target_os = "macos")]
    fn macos_full_screen_capture_writes_png_file() {
        let adapter = MacosPlatform;
        let capture = adapter
            .capture_all_screens_with_cursor(false)
            .expect("capture screen");

        assert!(capture.image_path.exists());
        assert!(capture.region.width > 0);
        assert!(capture.region.height > 0);
        std::fs::remove_file(capture.image_path).expect("remove captured png");
    }

    #[test]
    fn macos_clipboard_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter
            .copy_image_to_clipboard(std::path::Path::new("capture.bmp"))
            .expect_err("macOS clipboard pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_clipboard_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::Clipboard),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_text_clipboard_reports_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("macOS text clipboard pending");

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    #[ignore = "writes text to the local macOS clipboard"]
    #[cfg(target_os = "macos")]
    fn macos_text_clipboard_can_copy_text() {
        let adapter = MacosPlatform;

        adapter
            .copy_text_to_clipboard("OddSnap macOS clipboard smoke")
            .expect("copy text");
    }

    #[test]
    fn macos_color_picker_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter
            .sample_cursor_color()
            .expect_err("macOS color picker pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_hotkey_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter
            .register_capture_hotkey("Alt+`")
            .expect_err("macOS hotkey pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_recording_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let result = adapter.start_desktop_recording(VideoRecordingRequest {
            output_path: std::path::PathBuf::from("capture.mp4"),
            region: None,
            format: RecordingFormat::Mp4,
            quality: RecordingQuality::Original,
            fps: 30,
            record_microphone: false,
            record_desktop_audio: false,
            microphone_device_id: None,
            desktop_audio_device_id: None,
        });
        let error = match result {
            Ok(_) => panic!("macOS recording should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_region_overlay_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;
        let error = match adapter.create_overlay_window(OverlayWindowRequest {
            bounds: oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            },
            opacity: 1,
            click_through: true,
            show_crosshair_guides: false,
            detect_windows: false,
        }) {
            Ok(_) => panic!("macOS overlay should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_region_selection_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;
        let error = match adapter.select_region(OverlayWindowRequest {
            bounds: oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            },
            opacity: 1,
            click_through: false,
            show_crosshair_guides: false,
            detect_windows: false,
        }) {
            Ok(_) => panic!("macOS region selection should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }
}
