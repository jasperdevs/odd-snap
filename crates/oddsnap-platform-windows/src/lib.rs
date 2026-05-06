use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureResult, MonitorInfo, PlatformAdapter, PlatformError, ScreenCaptureService,
};

#[cfg(target_os = "windows")]
use windows::Win32::UI::WindowsAndMessaging::{
    GetSystemMetrics, SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN,
};

#[derive(Debug, Default)]
pub struct WindowsPlatform;

impl PlatformAdapter for WindowsPlatform {
    fn name(&self) -> &'static str {
        "Windows"
    }

    fn native_ui_profile(&self) -> NativeUiProfile {
        NativeUiProfile::for_target("windows")
    }

    fn capabilities(&self) -> PlatformCapabilities {
        PlatformCapabilities {
            os: "windows".into(),
            items: vec![
                (
                    PlatformCapability::ScreenCapture,
                    CapabilityState::InProgress,
                ),
                (
                    PlatformCapability::RegionOverlay,
                    CapabilityState::InProgress,
                ),
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

impl ScreenCaptureService for WindowsPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        #[cfg(target_os = "windows")]
        {
            let x = unsafe { GetSystemMetrics(SM_XVIRTUALSCREEN) };
            let y = unsafe { GetSystemMetrics(SM_YVIRTUALSCREEN) };
            let width = unsafe { GetSystemMetrics(SM_CXVIRTUALSCREEN) };
            let height = unsafe { GetSystemMetrics(SM_CYVIRTUALSCREEN) };

            if width <= 0 || height <= 0 {
                return Err(PlatformError::Failed(
                    "Windows returned an empty virtual screen".into(),
                ));
            }

            Ok(vec![MonitorInfo {
                id: "windows-virtual-screen".into(),
                name: "Virtual screen".into(),
                x,
                y,
                width: width as u32,
                height: height as u32,
                scale_percent: 100,
            }])
        }

        #[cfg(not(target_os = "windows"))]
        {
            Err(PlatformError::Unsupported(
                "Windows monitor enumeration is only available on Windows",
            ))
        }
    }

    fn capture_region(&self, _: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        Err(PlatformError::Unsupported(
            "Windows region capture is not implemented in the Rust rewrite yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{CapabilityState, PlatformCapability};
    use oddsnap_platform::PlatformAdapter;

    use super::WindowsPlatform;

    #[test]
    fn windows_adapter_tracks_early_capture_work() {
        let adapter = WindowsPlatform;
        let capabilities = adapter.capabilities();

        assert_eq!(
            capabilities.state(PlatformCapability::ScreenCapture),
            CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn windows_monitor_enumeration_returns_virtual_screen() {
        use oddsnap_platform::ScreenCaptureService;

        let adapter = WindowsPlatform;
        let monitors = adapter.monitors().expect("enumerate monitors");

        assert_eq!(monitors.len(), 1);
        assert!(monitors[0].width > 0);
        assert!(monitors[0].height > 0);
    }

    #[test]
    #[cfg(not(target_os = "windows"))]
    fn windows_monitor_enumeration_is_gated_off_windows() {
        use oddsnap_platform::ScreenCaptureService;

        let adapter = WindowsPlatform;
        let error = adapter.monitors().expect_err("non-Windows should be gated");

        assert!(error.to_string().contains("only available on Windows"));
    }
}
