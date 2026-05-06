use std::path::Path;

use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureResult, ClipboardImageService, ClipboardTextService, MonitorInfo,
    PlatformAdapter, PlatformError, ScreenCaptureService, WindowInfo, WindowPickerService,
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
                (PlatformCapability::Clipboard, CapabilityState::Planned),
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
        let _ = text;
        Err(PlatformError::Unsupported(
            "Linux text clipboard is not implemented yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::NativeMaterial;
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, PlatformAdapter, ScreenCaptureService,
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
    fn linux_text_clipboard_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("Linux text clipboard pending");

        assert!(error.to_string().contains("not implemented yet"));
    }
}
