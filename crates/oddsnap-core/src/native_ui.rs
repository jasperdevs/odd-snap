use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub enum NativeMaterial {
    WinUi3,
    LiquidGlass,
    FreedesktopAdaptive,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct NativeUiProfile {
    pub platform: String,
    pub material: NativeMaterial,
    pub visual_goal: String,
}

impl NativeUiProfile {
    pub fn for_target(target_os: &str) -> Self {
        match target_os {
            "windows" => Self {
                platform: "Windows".into(),
                material: NativeMaterial::WinUi3,
                visual_goal: "WinUI 3 aligned controls, density, acrylic-like surfaces, and existing OddSnap visual proportions.".into(),
            },
            "macos" => Self {
                platform: "macOS".into(),
                material: NativeMaterial::LiquidGlass,
                visual_goal: "Liquid Glass aligned materials, native-feeling spacing, traffic-light window behavior, and macOS permission flows.".into(),
            },
            _ => Self {
                platform: "Linux".into(),
                material: NativeMaterial::FreedesktopAdaptive,
                visual_goal: "Freedesktop-friendly GTK/KDE-adaptive layout, portal-aware capture permissions, and desktop-theme respectful surfaces.".into(),
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{NativeMaterial, NativeUiProfile};

    #[test]
    fn maps_requested_native_ui_targets() {
        assert_eq!(
            NativeUiProfile::for_target("windows").material,
            NativeMaterial::WinUi3
        );
        assert_eq!(
            NativeUiProfile::for_target("macos").material,
            NativeMaterial::LiquidGlass
        );
        assert_eq!(
            NativeUiProfile::for_target("linux").material,
            NativeMaterial::FreedesktopAdaptive
        );
    }
}
