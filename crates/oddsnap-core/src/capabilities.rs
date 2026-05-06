use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum CapabilityState {
    Planned,
    InProgress,
    Available,
    Blocked,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum PlatformCapability {
    ScreenCapture,
    RegionOverlay,
    WindowCapture,
    ScreenshotExclusion,
    GlobalHotkeys,
    Tray,
    Clipboard,
    FileDialogs,
    MicrophoneAudio,
    SystemAudio,
    Notifications,
    AutoStart,
    AppUpdater,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PlatformCapabilities {
    pub os: String,
    pub items: Vec<(PlatformCapability, CapabilityState)>,
}

impl PlatformCapabilities {
    pub fn state(&self, capability: PlatformCapability) -> CapabilityState {
        self.items
            .iter()
            .find_map(|(candidate, state)| (*candidate == capability).then_some(*state))
            .unwrap_or(CapabilityState::Planned)
    }
}

#[cfg(test)]
mod tests {
    use super::{CapabilityState, PlatformCapabilities, PlatformCapability};

    #[test]
    fn missing_capabilities_default_to_planned() {
        let capabilities = PlatformCapabilities {
            os: "test".into(),
            items: vec![(PlatformCapability::Tray, CapabilityState::Available)],
        };

        assert_eq!(
            capabilities.state(PlatformCapability::ScreenCapture),
            CapabilityState::Planned
        );
    }
}
