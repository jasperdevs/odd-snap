use gpui::{px, rgb, Rgba, StatefulInteractiveElement, Styled};

pub mod skin {
    use super::{rgb, Rgba};

    pub const WINDOW_WIDTH: f32 = 1120.0;
    pub const WINDOW_HEIGHT: f32 = 720.0;
    pub const MIN_WINDOW_WIDTH: f32 = 880.0;
    pub const MIN_WINDOW_HEIGHT: f32 = 560.0;

    pub const APP_BG: u32 = 0x101114;
    pub const APP_TEXT: u32 = 0xf4f6f8;
    pub const HEADER_BORDER: u32 = 0x24272d;
    pub const PANEL_BG: u32 = 0x17191f;
    pub const PANEL_BORDER: u32 = 0x272b33;
    pub const SURFACE_BG: u32 = 0x1d2027;
    pub const MUTED_TEXT: u32 = 0xaab0ba;
    pub const BODY_TEXT: u32 = 0xc6ccd6;
    pub const BRIGHT_TEXT: u32 = 0xd8dde6;

    pub fn color(hex: u32) -> Rgba {
        rgb(hex)
    }
}

#[derive(Clone, Copy)]
pub enum ButtonVariant {
    Capture,
    Setting,
    History,
    Recording,
    RecordingSecondary,
}

impl ButtonVariant {
    fn border(self) -> Rgba {
        skin::color(match self {
            Self::Capture => 0x4a5262,
            Self::Setting => 0x3d4654,
            Self::History => 0x354052,
            Self::Recording => 0x5a3d45,
            Self::RecordingSecondary => 0x4f4436,
        })
    }

    fn background(self) -> Rgba {
        skin::color(match self {
            Self::Capture => 0x242936,
            Self::Setting => 0x202631,
            Self::History => 0x202733,
            Self::Recording => 0x3a2229,
            Self::RecordingSecondary => 0x31281f,
        })
    }

    fn hover_background(self) -> Rgba {
        skin::color(match self {
            Self::Capture => 0x303746,
            Self::Setting => 0x2a3240,
            Self::History => 0x2a3342,
            Self::Recording => 0x4a2a33,
            Self::RecordingSecondary => 0x3d3124,
        })
    }

    fn text(self) -> Option<Rgba> {
        match self {
            Self::History => Some(skin::color(skin::BRIGHT_TEXT)),
            Self::Capture | Self::Setting | Self::Recording | Self::RecordingSecondary => None,
        }
    }

    fn radius(self) -> f32 {
        match self {
            Self::History => 6.0,
            Self::Capture | Self::Setting | Self::Recording | Self::RecordingSecondary => 7.0,
        }
    }

    fn padding(self) -> (f32, f32) {
        match self {
            Self::History => (8.0, 4.0),
            Self::Capture | Self::Setting | Self::Recording | Self::RecordingSecondary => {
                (10.0, 6.0)
            }
        }
    }
}

pub fn panel_style<T: Styled>(element: T) -> T {
    element
        .rounded(px(8.0))
        .border_1()
        .border_color(skin::color(skin::PANEL_BORDER))
        .bg(skin::color(skin::PANEL_BG))
        .p(px(16.0))
}

pub fn surface_style<T: Styled>(element: T) -> T {
    element
        .rounded(px(6.0))
        .bg(skin::color(skin::SURFACE_BG))
        .px(px(10.0))
        .py(px(8.0))
}

pub fn action_button_style<T>(element: T, variant: ButtonVariant) -> T
where
    T: Styled + StatefulInteractiveElement,
{
    let (px_x, px_y) = variant.padding();
    let element = element
        .rounded(px(variant.radius()))
        .border_1()
        .border_color(variant.border())
        .bg(variant.background())
        .hover(move |this| this.bg(variant.hover_background()))
        .px(px(px_x))
        .py(px(px_y))
        .text_size(px(11.0));

    if let Some(color) = variant.text() {
        element.text_color(color)
    } else {
        element
    }
}
