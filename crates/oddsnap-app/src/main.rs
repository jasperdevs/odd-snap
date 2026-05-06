#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use gpui::{
    div, px, rgb, size, App, AppContext, Bounds, Context, InteractiveElement, IntoElement,
    ParentElement, Render, SharedString, StatefulInteractiveElement, Styled, TitlebarOptions,
    Window, WindowBackgroundAppearance, WindowBounds, WindowDecorations, WindowOptions,
};
use gpui_platform::application;
use oddsnap_core::{CapabilityState, PlatformCapability};
use oddsnap_platform::{PlatformAdapter, ScreenCaptureService};

fn main() {
    application().run(|cx: &mut App| {
        let bounds = Bounds::centered(None, size(px(1120.0), px(720.0)), cx);
        let window = cx
            .open_window(
                WindowOptions {
                    window_bounds: Some(WindowBounds::Windowed(bounds)),
                    titlebar: Some(TitlebarOptions {
                        title: Some("OddSnap Rust".into()),
                        appears_transparent: true,
                        traffic_light_position: None,
                    }),
                    window_background: WindowBackgroundAppearance::Transparent,
                    window_decorations: Some(WindowDecorations::Client),
                    app_id: Some("dev.jasper.oddsnap.rust".into()),
                    window_min_size: Some(size(px(880.0), px(560.0))),
                    ..Default::default()
                },
                |_, cx| cx.new(OddSnapRustApp::new),
            )
            .expect("open OddSnap Rust window");

        window
            .update(cx, |app, window, cx| {
                window.focus(&app.focus_handle(cx), cx)
            })
            .expect("focus OddSnap Rust window");
        cx.activate(true);
    });
}

struct OddSnapRustApp {
    platform_name: String,
    native_ui_goal: String,
    capabilities: Vec<(PlatformCapability, CapabilityState)>,
    capture_status: String,
    focus_handle: gpui::FocusHandle,
}

impl OddSnapRustApp {
    fn new(cx: &mut Context<Self>) -> Self {
        let platform = host_platform();
        let profile = platform.native_ui_profile();
        let capabilities = platform.capabilities().items;

        Self {
            platform_name: platform.name().into(),
            native_ui_goal: profile.visual_goal,
            capabilities,
            capture_status: "No capture run in this session.".into(),
            focus_handle: cx.focus_handle(),
        }
    }

    fn focus_handle(&self, _: &App) -> gpui::FocusHandle {
        self.focus_handle.clone()
    }
}

fn host_platform() -> Box<dyn PlatformAdapter> {
    #[cfg(target_os = "windows")]
    {
        Box::new(oddsnap_platform_windows::WindowsPlatform)
    }
    #[cfg(target_os = "macos")]
    {
        Box::new(oddsnap_platform_macos::MacosPlatform)
    }
    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        Box::new(oddsnap_platform_linux::LinuxPlatform)
    }
}

impl gpui::Focusable for OddSnapRustApp {
    fn focus_handle(&self, cx: &App) -> gpui::FocusHandle {
        self.focus_handle(cx)
    }
}

impl Render for OddSnapRustApp {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .flex()
            .flex_col()
            .size_full()
            .bg(rgb(0x101114))
            .text_color(rgb(0xf4f6f8))
            .font_family("Segoe UI")
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .h(px(54.0))
                    .px(px(18.0))
                    .border_b_1()
                    .border_color(rgb(0x24272d))
                    .child(
                        div()
                            .flex()
                            .flex_col()
                            .child(div().text_size(px(17.0)).child("OddSnap Rust"))
                            .child(
                                div()
                                    .text_size(px(11.0))
                                    .text_color(rgb(0xaab0ba))
                                    .child("GPUI rewrite foundation"),
                            ),
                    )
                    .child(
                        div()
                            .rounded(px(8.0))
                            .border_1()
                            .border_color(rgb(0x343842))
                            .px(px(10.0))
                            .py(px(5.0))
                            .text_size(px(12.0))
                            .child(SharedString::from(self.platform_name.clone())),
                    ),
            )
            .child(
                div()
                    .flex()
                    .flex_1()
                    .gap(px(14.0))
                    .p(px(18.0))
                    .child(self.panel(
                        "Native UI target",
                        vec![
                            self.native_ui_goal.clone(),
                            "Windows: WinUI 3 aligned; macOS: Liquid Glass aligned; Linux: freedesktop adaptive.".into(),
                            self.capture_status.clone(),
                        ],
                    ))
                    .child(self.capability_panel(cx)),
            )
    }
}

impl OddSnapRustApp {
    fn panel(&self, title: &'static str, lines: Vec<String>) -> impl IntoElement {
        let mut body = div()
            .flex()
            .flex_col()
            .gap(px(9.0))
            .flex_1()
            .rounded(px(8.0))
            .border_1()
            .border_color(rgb(0x272b33))
            .bg(rgb(0x17191f))
            .p(px(16.0))
            .child(div().text_size(px(14.0)).child(title));

        for line in lines {
            body = body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xc6ccd6))
                    .child(SharedString::from(line)),
            );
        }

        body
    }

    fn capability_panel(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let mut body = div()
            .flex()
            .flex_col()
            .gap(px(8.0))
            .flex_1()
            .rounded(px(8.0))
            .border_1()
            .border_color(rgb(0x272b33))
            .bg(rgb(0x17191f))
            .p(px(16.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(14.0)).child("Platform parity tracker"))
                    .child(self.capture_button(cx)),
            );

        for (capability, state) in &self.capabilities {
            body = body.child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .rounded(px(6.0))
                    .bg(rgb(0x1d2027))
                    .px(px(10.0))
                    .py(px(7.0))
                    .child(
                        div()
                            .text_size(px(12.0))
                            .child(SharedString::from(format!("{capability:?}"))),
                    )
                    .child(
                        div()
                            .text_size(px(11.0))
                            .text_color(rgb(state_color(*state)))
                            .child(SharedString::from(format!("{state:?}"))),
                    ),
            );
        }

        body
    }

    fn capture_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .id("capture-smoke-button")
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x4a5262))
            .bg(rgb(0x242936))
            .hover(|this| this.bg(rgb(0x303746)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child("Test capture")
            .on_click(cx.listener(|this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_capture_smoke();
                cx.notify();
            }))
    }

    fn run_capture_smoke(&mut self) {
        let platform = host_platform();
        #[cfg(target_os = "windows")]
        let result = oddsnap_platform_windows::WindowsPlatform.capture_all_screens();

        #[cfg(not(target_os = "windows"))]
        let result = Err(oddsnap_platform::PlatformError::Unsupported(
            "capture smoke is only wired on Windows so far",
        ));

        self.capture_status = match result {
            Ok(capture) => format!(
                "{} capture wrote {}",
                platform.name(),
                capture.image_path.display()
            ),
            Err(error) => format!("{} capture failed: {error}", platform.name()),
        };
    }
}

fn state_color(state: CapabilityState) -> u32 {
    match state {
        CapabilityState::Available => 0x5be49b,
        CapabilityState::InProgress => 0xf4cf65,
        CapabilityState::Blocked => 0xff7a7a,
        CapabilityState::Planned => 0x8b93a3,
    }
}
