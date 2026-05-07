use std::path::Path;
#[cfg(target_os = "linux")]
use std::{
    io::Write,
    process::{Command, Stdio},
};

use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureResult, ClipboardImageService, ClipboardTextService, ColorPickerService,
    ColorSample, HotkeyService, MonitorInfo, OverlayWindowHandle, OverlayWindowRequest,
    PlatformAdapter, PlatformError, RegionOverlayService, RegionSelectionService,
    ScreenCaptureService, VideoRecordingRequest, VideoRecordingService, WindowInfo,
    WindowPickerService,
};

#[derive(Debug, Default)]
pub struct LinuxPlatform;

impl PlatformAdapter for LinuxPlatform {
    fn name(&self) -> &'static str {
        "Linux"
    }

    fn native_ui_profile(&self) -> NativeUiProfile {
        NativeUiProfile::for_target("linux")
    }

    fn capabilities(&self) -> PlatformCapabilities {
        PlatformCapabilities {
            os: "linux".into(),
            items: vec![
                (PlatformCapability::ScreenCapture, CapabilityState::Planned),
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

impl ScreenCaptureService for LinuxPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        Err(PlatformError::Unsupported(
            "Linux monitor enumeration is not implemented yet",
        ))
    }

    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        let _ = region;
        Err(PlatformError::Unsupported(
            "Linux region capture is not implemented yet",
        ))
    }
}

impl WindowPickerService for LinuxPlatform {
    fn active_window(&self) -> Result<WindowInfo, PlatformError> {
        Err(PlatformError::Unsupported(
            "Linux active-window detection is not implemented yet",
        ))
    }
}

impl ClipboardImageService for LinuxPlatform {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError> {
        let _ = image_path;
        Err(PlatformError::Unsupported(
            "Linux image clipboard is not implemented yet",
        ))
    }
}

impl ClipboardTextService for LinuxPlatform {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "linux")]
        {
            copy_text_to_linux_clipboard(text)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = text;
            Err(PlatformError::Unsupported(
                "Linux text clipboard is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn copy_text_to_linux_clipboard(text: &str) -> Result<(), PlatformError> {
    let mut errors = Vec::new();

    for (program, args) in [
        ("wl-copy", &[] as &[&str]),
        ("xclip", &["-selection", "clipboard"] as &[&str]),
        ("xsel", &["--clipboard", "--input"] as &[&str]),
    ] {
        match run_clipboard_command(program, args, text) {
            Ok(()) => return Ok(()),
            Err(error) => errors.push(format!("{program}: {error}")),
        }
    }

    Err(PlatformError::Failed(format!(
        "no Linux clipboard command succeeded: {}",
        errors.join("; ")
    )))
}

#[cfg(target_os = "linux")]
fn run_clipboard_command(program: &str, args: &[&str], text: &str) -> Result<(), PlatformError> {
    let mut child = Command::new(program)
        .args(args)
        .stdin(Stdio::piped())
        .spawn()
        .map_err(|source| PlatformError::Failed(format!("failed to start command: {source}")))?;

    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| PlatformError::Failed("failed to open clipboard command stdin".into()))?;
    stdin.write_all(text.as_bytes()).map_err(|source| {
        PlatformError::Failed(format!("failed to write clipboard text: {source}"))
    })?;
    drop(stdin);

    let status = child
        .wait()
        .map_err(|source| PlatformError::Failed(format!("failed to wait for command: {source}")))?;
    if status.success() {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "exited with status {status}"
        )))
    }
}

impl ColorPickerService for LinuxPlatform {
    fn sample_cursor_color(&self) -> Result<ColorSample, PlatformError> {
        Err(PlatformError::Unsupported(
            "Linux color picker is not implemented yet",
        ))
    }
}

impl HotkeyService for LinuxPlatform {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError> {
        let _ = accelerator;
        Err(PlatformError::Unsupported(
            "Linux global hotkey registration is not implemented yet",
        ))
    }
}

impl VideoRecordingService for LinuxPlatform {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn oddsnap_platform::VideoRecordingHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux desktop recording is not implemented yet",
        ))
    }
}

impl RegionOverlayService for LinuxPlatform {
    fn create_overlay_window(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Box<dyn OverlayWindowHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux region overlay is not implemented yet",
        ))
    }
}

impl RegionSelectionService for LinuxPlatform {
    fn select_region(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Option<CaptureRegion>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux region selection is not implemented yet",
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

    use super::LinuxPlatform;

    #[test]
    fn linux_adapter_uses_freedesktop_profile() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter.native_ui_profile().material,
            NativeMaterial::FreedesktopAdaptive
        );
    }

    #[test]
    fn linux_capture_services_are_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter.monitors().expect_err("Linux capture pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_clipboard_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .copy_image_to_clipboard(std::path::Path::new("capture.bmp"))
            .expect_err("Linux clipboard pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_clipboard_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::Clipboard),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_text_clipboard_reports_wrong_host() {
        let adapter = LinuxPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("Linux text clipboard pending");

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    #[ignore = "writes text to the local Linux clipboard through wl-copy, xclip, or xsel"]
    #[cfg(target_os = "linux")]
    fn linux_text_clipboard_can_copy_text() {
        let adapter = LinuxPlatform;

        adapter
            .copy_text_to_clipboard("OddSnap Linux clipboard smoke")
            .expect("copy text");
    }

    #[test]
    fn linux_color_picker_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .sample_cursor_color()
            .expect_err("Linux color picker pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_hotkey_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .register_capture_hotkey("Alt+`")
            .expect_err("Linux hotkey pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_recording_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

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
            Ok(_) => panic!("Linux recording should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_region_overlay_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;
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
            Ok(_) => panic!("Linux overlay should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn linux_region_selection_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;
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
            Ok(_) => panic!("Linux region selection should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }
}
