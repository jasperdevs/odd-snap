#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use gpui::{
    div, px, rgb, size, App, AppContext, Bounds, Context, InteractiveElement, IntoElement,
    ParentElement, Render, SharedString, StatefulInteractiveElement, Styled, TitlebarOptions,
    Window, WindowBackgroundAppearance, WindowBounds, WindowDecorations, WindowOptions,
};
use gpui_platform::application;
use oddsnap_core::{
    build_available_capture_path, default_history_path, default_settings_path,
    discover_ffmpeg_tools, format_file_name_template, AppSettings, CapabilityState, HistoryEntry,
    HistoryIndex, HistoryKind, HistoryStore, PlatformCapability, SettingsStore,
};
use oddsnap_platform::{
    default_capture_directory, persist_capture_to_path_as, ClipboardImageService, PlatformAdapter,
    ScreenCaptureService, WindowCaptureService,
};

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
    settings: AppSettings,
    settings_path: String,
    history_store: HistoryStore,
    history_path: String,
    media_status: String,
    hotkey_status: String,
    capture_history: Vec<CaptureHistoryEntry>,
    focus_handle: gpui::FocusHandle,
    #[cfg(target_os = "windows")]
    _hotkey_listener: Option<oddsnap_platform_windows::WindowsHotkeyListener>,
}

struct CaptureHistoryEntry {
    mode: CaptureMode,
    path: String,
    width: u32,
    height: u32,
}

#[derive(Clone, Copy)]
enum CaptureMode {
    FullScreen,
    ActiveWindow,
}

impl CaptureMode {
    fn label(self) -> &'static str {
        match self {
            Self::FullScreen => "Full screen",
            Self::ActiveWindow => "Active window",
        }
    }
}

impl OddSnapRustApp {
    fn new(cx: &mut Context<Self>) -> Self {
        let platform = host_platform();
        let profile = platform.native_ui_profile();
        let capabilities = platform.capabilities().items;
        let settings_store = SettingsStore::new(default_settings_path());
        let settings_path = settings_store.path().display().to_string();
        let migration_status = import_legacy_settings_if_needed(&settings_store);
        let (settings, capture_status) = match settings_store.load_or_default() {
            Ok(settings) => (
                settings,
                migration_status.unwrap_or_else(|| "No capture run in this session.".into()),
            ),
            Err(error) => (
                AppSettings::default(),
                format!("Settings load failed, using defaults: {error}"),
            ),
        };
        let history_store = HistoryStore::new(default_history_path());
        let history_path = history_store.path().display().to_string();
        let capture_history = history_store
            .load_or_default()
            .map(history_entries_to_capture_history)
            .unwrap_or_default();
        let media_status = match discover_ffmpeg_tools() {
            Some(tools) => {
                let probe = tools
                    .ffprobe
                    .as_ref()
                    .map(|path| path.display().to_string())
                    .unwrap_or_else(|| "ffprobe not found".into());
                format!("FFmpeg: {}; ffprobe: {probe}", tools.ffmpeg.display())
            }
            None => "FFmpeg: not found on PATH".into(),
        };
        let (hotkey_status, hotkey_listener, hotkey_events) = start_capture_hotkey_listener();

        let app = Self {
            platform_name: platform.name().into(),
            native_ui_goal: profile.visual_goal,
            capabilities,
            capture_status,
            settings,
            settings_path,
            history_store,
            history_path,
            media_status,
            hotkey_status,
            capture_history,
            focus_handle: cx.focus_handle(),
            #[cfg(target_os = "windows")]
            _hotkey_listener: hotkey_listener,
        };

        app.start_hotkey_event_pump(hotkey_events, cx);
        app
    }

    fn focus_handle(&self, _: &App) -> gpui::FocusHandle {
        self.focus_handle.clone()
    }
}

fn import_legacy_settings_if_needed(settings_store: &SettingsStore) -> Option<String> {
    if settings_store.path().exists() {
        return None;
    }

    let legacy_paths = oddsnap_migrate::LegacyOddSnapPaths::from_current_environment()?;
    let imported = oddsnap_migrate::read_legacy_settings(&legacy_paths.settings_path).ok()?;
    let settings = oddsnap_migrate::import_app_settings(&imported);

    match settings_store.save(&settings) {
        Ok(()) => Some(format!(
            "Imported legacy settings from {}.",
            imported.source_path.display()
        )),
        Err(error) => Some(format!("Legacy settings import failed: {error}")),
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
                    .child(self.capture_panel(cx))
                    .child(self.capability_panel()),
            )
    }
}

impl OddSnapRustApp {
    fn capture_panel(&self, cx: &mut Context<Self>) -> impl IntoElement {
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
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(14.0)).child("Capture"))
                    .child(
                        div()
                            .flex()
                            .gap(px(8.0))
                            .child(self.capture_button(cx, "capture-full-button", CaptureMode::FullScreen))
                            .child(self.capture_button(cx, "capture-window-button", CaptureMode::ActiveWindow)),
                    ),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xc6ccd6))
                    .child(SharedString::from(self.native_ui_goal.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child("Windows: WinUI 3 aligned; macOS: Liquid Glass aligned; Linux: freedesktop adaptive."),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!("Settings: {}", self.settings_path))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!("History: {}", self.history_path))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!(
                        "Output: {}",
                        self.capture_output_directory().display()
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!(
                        "Image format: {} · JPEG quality {}",
                        self.settings.capture_image_format.label(),
                        self.settings.jpeg_quality
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!(
                        "File naming: {}{}",
                        self.settings.file_name_template,
                        if self.settings.save_in_monthly_folders {
                            " · monthly folders"
                        } else {
                            ""
                        }
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(self.media_status.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(self.hotkey_status.clone())),
            )
            .child(
                div()
                    .rounded(px(6.0))
                    .bg(rgb(0x1d2027))
                    .px(px(10.0))
                    .py(px(8.0))
                    .text_size(px(12.0))
                    .text_color(rgb(0xd8dde6))
                    .child(SharedString::from(self.capture_status.clone())),
            );

        body = body.child(div().text_size(px(13.0)).child("Recent captures"));

        if self.capture_history.is_empty() {
            return body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0x8b93a3))
                    .child("No saved captures yet."),
            );
        }

        for entry in &self.capture_history {
            body = body.child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(3.0))
                    .rounded(px(6.0))
                    .bg(rgb(0x1d2027))
                    .px(px(10.0))
                    .py(px(8.0))
                    .child(div().text_size(px(12.0)).child(SharedString::from(format!(
                        "{} · {}x{}",
                        entry.mode.label(),
                        entry.width,
                        entry.height
                    ))))
                    .child(
                        div()
                            .text_size(px(11.0))
                            .text_color(rgb(0x9ba3af))
                            .child(SharedString::from(entry.path.clone())),
                    ),
            );
        }

        body
    }

    fn capability_panel(&self) -> impl IntoElement {
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
                    .child(
                        div()
                            .text_size(px(11.0))
                            .text_color(rgb(0x8b93a3))
                            .child("Local"),
                    ),
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

    fn capture_button(
        &self,
        cx: &mut Context<Self>,
        element_id: &'static str,
        mode: CaptureMode,
    ) -> impl IntoElement {
        div()
            .id(element_id)
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x4a5262))
            .bg(rgb(0x242936))
            .hover(|this| this.bg(rgb(0x303746)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child(mode.label())
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_capture(mode);
                cx.notify();
            }))
    }

    fn run_capture(&mut self, mode: CaptureMode) {
        let platform = host_platform();
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            let capture = match mode {
                CaptureMode::FullScreen => adapter.capture_all_screens(),
                CaptureMode::ActiveWindow => adapter.capture_active_window(),
            };
            capture.and_then(|capture| {
                let destination = self.capture_destination(&capture);
                let saved = persist_capture_to_path_as(
                    &capture,
                    &destination,
                    self.settings.capture_image_format,
                    self.settings.jpeg_quality,
                )?;
                if self.settings.copy_captures_to_clipboard {
                    adapter.copy_image_to_clipboard(&saved.image_path)?;
                }
                Ok(saved)
            })
        };

        #[cfg(not(target_os = "windows"))]
        let result = Err(oddsnap_platform::PlatformError::Unsupported(
            "capture smoke is only wired on Windows so far",
        ));

        self.capture_status = match result {
            Ok(capture) => {
                let path = capture.image_path.display().to_string();
                let history_status = self.save_capture_history(&capture, mode);
                let copy_status = if self.settings.copy_captures_to_clipboard {
                    "copied and saved"
                } else {
                    "saved"
                };
                format!(
                    "{} {} {copy_status} {path}{history_status}",
                    platform.name(),
                    mode.label()
                )
            }
            Err(error) => format!("{} capture failed: {error}", platform.name()),
        };
    }

    #[cfg(target_os = "windows")]
    fn start_hotkey_event_pump(
        &self,
        receiver: Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsHotkeyEvent>>,
        cx: &mut Context<Self>,
    ) {
        let Some(receiver) = receiver else {
            return;
        };

        cx.spawn(async move |this, cx| loop {
            while let Ok(event) = receiver.try_recv() {
                let _ = this.update(cx, |app, cx| {
                    app.handle_hotkey_event(event);
                    cx.notify();
                });
            }

            cx.background_executor()
                .timer(std::time::Duration::from_millis(100))
                .await;
        })
        .detach();
    }

    #[cfg(not(target_os = "windows"))]
    fn start_hotkey_event_pump(&self, _: Option<()>, _: &mut Context<Self>) {}

    #[cfg(target_os = "windows")]
    fn handle_hotkey_event(&mut self, event: oddsnap_platform_windows::WindowsHotkeyEvent) {
        match event {
            oddsnap_platform_windows::WindowsHotkeyEvent::Capture => {
                self.capture_status = "Capture hotkey received.".into();
                self.run_capture(CaptureMode::FullScreen);
            }
        }
    }

    fn capture_output_directory(&self) -> std::path::PathBuf {
        self.settings
            .capture_output_directory_or(default_capture_directory())
    }

    fn capture_destination(&self, capture: &oddsnap_platform::CaptureResult) -> std::path::PathBuf {
        let stem = format_file_name_template(
            &self.settings.file_name_template,
            capture.region.width,
            capture.region.height,
        );
        let file_name = format!(
            "{}.{}",
            stem,
            self.settings.capture_image_format.extension()
        );
        build_available_capture_path(
            &self.capture_output_directory(),
            &file_name,
            self.settings.save_in_monthly_folders,
        )
    }

    fn save_capture_history(
        &mut self,
        capture: &oddsnap_platform::CaptureResult,
        mode: CaptureMode,
    ) -> String {
        if !self.settings.save_history {
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode,
                    path: capture.image_path.display().to_string(),
                    width: capture.region.width,
                    height: capture.region.height,
                },
            );
            self.capture_history.truncate(6);
            return String::new();
        }

        let entry = match HistoryEntry::from_capture_file(
            capture.image_path.clone(),
            capture.region.width,
            capture.region.height,
            HistoryKind::Image,
        ) {
            Ok(entry) => entry,
            Err(error) => return format!("; history failed: {error}"),
        };

        match self.history_store.append_entry(entry) {
            Ok(index) => {
                self.capture_history = history_entries_to_capture_history(index);
                String::new()
            }
            Err(error) => format!("; history failed: {error}"),
        }
    }
}

#[cfg(target_os = "windows")]
fn start_capture_hotkey_listener() -> (
    String,
    Option<oddsnap_platform_windows::WindowsHotkeyListener>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsHotkeyEvent>>,
) {
    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_windows::start_capture_hotkey_listener("Alt+`", sender) {
        Ok(listener) => (
            "Hotkey: Alt+` listener ready.".into(),
            Some(listener),
            Some(receiver),
        ),
        Err(error) => (format!("Hotkey listener unavailable: {error}"), None, None),
    }
}

#[cfg(not(target_os = "windows"))]
fn start_capture_hotkey_listener() -> (String, Option<()>, Option<()>) {
    (
        "Hotkey listener: pending on this platform.".into(),
        None,
        None,
    )
}

fn history_entries_to_capture_history(index: HistoryIndex) -> Vec<CaptureHistoryEntry> {
    index
        .entries
        .into_iter()
        .take(6)
        .map(|entry| CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            path: entry.file_path.display().to_string(),
            width: entry.width,
            height: entry.height,
        })
        .collect()
}

fn state_color(state: CapabilityState) -> u32 {
    match state {
        CapabilityState::Available => 0x5be49b,
        CapabilityState::InProgress => 0xf4cf65,
        CapabilityState::Blocked => 0xff7a7a,
        CapabilityState::Planned => 0x8b93a3,
    }
}
