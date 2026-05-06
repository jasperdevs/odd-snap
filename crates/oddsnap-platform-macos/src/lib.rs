use std::path::Path;

use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureResult, ClipboardImageService, ClipboardTextService, MonitorInfo,
    PlatformAdapter, PlatformError, ScreenCaptureService, WindowInfo, WindowPickerService,
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

impl ScreenCaptureService for MacosPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        Err(PlatformError::Unsupported(
            "macOS monitor enumeration is not implemented yet",
        ))
    }

    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        let _ = region;
        Err(PlatformError::Unsupported(
            "macOS region capture is not implemented yet",
        ))
    }
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
        let _ = text;
        Err(PlatformError::Unsupported(
            "macOS text clipboard is not implemented yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::NativeMaterial;
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, PlatformAdapter, ScreenCaptureService,
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
    fn macos_capture_services_are_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter.monitors().expect_err("macOS capture pending");

        assert!(error.to_string().contains("not implemented yet"));
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
    fn macos_text_clipboard_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("macOS text clipboard pending");

        assert!(error.to_string().contains("not implemented yet"));
    }
}
