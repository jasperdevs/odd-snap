#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use std::{
    fs,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    thread,
    time::Duration,
};

use gpui::{
    div, img, px, rgb, size, App, AppContext, Bounds, Context, InteractiveElement, IntoElement,
    ObjectFit, ParentElement, Render, SharedString, StatefulInteractiveElement, Styled,
    StyledImage, TitlebarOptions, Window, WindowBackgroundAppearance, WindowBounds,
    WindowDecorations, WindowOptions,
};
use gpui_platform::application;
use oddsnap_core::{
    build_available_capture_path, build_video_thumbnail_args, build_video_thumbnail_fallback_args,
    default_history_path, default_settings_path, discover_ffmpeg_tools, format_file_name_template,
    AppSettings, CapabilityState, CaptureImageFormat, DefaultCaptureMode, FfmpegThumbnailRequest,
    HistoryEntry, HistoryIndex, HistoryKind, HistoryStore, PlatformCapability, RecordingFormat,
    RecordingQuality, SettingsStore,
};
use oddsnap_platform::{
    default_capture_directory, persist_capture_to_path_as, virtual_screen_region, CaptureRequest,
    ClipboardImageService, PlatformAdapter, ScreenCaptureService, VideoRecordingHandle,
    VideoRecordingRequest, VideoRecordingService, WindowPickerService,
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
    settings_store: SettingsStore,
    settings_path: String,
    history_store: HistoryStore,
    history_path: String,
    media_status: String,
    hotkey_status: String,
    recording_status: String,
    active_recording: Option<ActiveRecording>,
    capture_history: Vec<CaptureHistoryEntry>,
    focus_handle: gpui::FocusHandle,
    #[cfg(target_os = "windows")]
    _hotkey_listener: Option<oddsnap_platform_windows::WindowsHotkeyListener>,
}

struct CaptureHistoryEntry {
    mode: CaptureMode,
    path: String,
    preview_path: Option<PathBuf>,
    width: u32,
    height: u32,
}

struct ActiveRecording {
    handle: Box<dyn VideoRecordingHandle>,
    width: u32,
    height: u32,
}

#[derive(Clone, Copy)]
enum CaptureMode {
    FullScreen,
    ActiveWindow,
}

#[derive(Clone, Copy)]
enum SettingsAction {
    CaptureImageFormat,
    ToggleClipboardCopy,
    ToggleCursor,
    RecordingFormat,
    RecordingQuality,
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
        let history_migration_status = import_legacy_history_if_needed(&history_store);
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
        let (hotkey_status, hotkey_listener, hotkey_events) = start_capture_hotkey_listener(
            &settings.capture_hotkey,
            settings.recording_hotkey.as_deref(),
        );

        let app = Self {
            platform_name: platform.name().into(),
            native_ui_goal: profile.visual_goal,
            capabilities,
            capture_status: combine_startup_status(capture_status, history_migration_status),
            settings,
            settings_store,
            settings_path,
            history_store,
            history_path,
            media_status,
            hotkey_status,
            recording_status: "No recording running.".into(),
            active_recording: None,
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

fn import_legacy_history_if_needed(history_store: &HistoryStore) -> Option<String> {
    if history_store.path().exists() {
        return None;
    }

    let legacy_paths = oddsnap_migrate::LegacyOddSnapPaths::from_current_environment()?;
    match oddsnap_migrate::import_existing_history(&legacy_paths) {
        Ok(index) if index.entries.is_empty() => None,
        Ok(index) => {
            let count = index.entries.len();
            match history_store.save(&index) {
                Ok(()) => Some(format!("Imported {count} existing history entries.")),
                Err(error) => Some(format!("Existing history import failed: {error}")),
            }
        }
        Err(error) => Some(format!("Existing history import failed: {error}")),
    }
}

fn combine_startup_status(base: String, extra: Option<String>) -> String {
    match extra {
        Some(extra) if !extra.is_empty() => format!("{base} {extra}"),
        _ => base,
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
                            .child(self.capture_button(
                                cx,
                                "capture-full-button",
                                CaptureMode::FullScreen,
                            ))
                            .child(self.capture_button(
                                cx,
                                "capture-window-button",
                                CaptureMode::ActiveWindow,
                            ))
                            .child(self.recording_button(cx)),
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
                    .child(SharedString::from(self.capture_preferences_summary())),
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
                    .child(SharedString::from(self.recording_status_summary())),
            )
            .child(
                div()
                    .flex()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "capture-format-button",
                        format!(
                            "Image {}",
                            self.settings.capture_image_format.label()
                        ),
                        SettingsAction::CaptureImageFormat,
                    ))
                    .child(self.settings_button(
                        cx,
                        "copy-capture-button",
                        format!(
                            "Copy {}",
                            on_off(self.settings.copy_captures_to_clipboard)
                        ),
                        SettingsAction::ToggleClipboardCopy,
                    ))
                    .child(self.settings_button(
                        cx,
                        "cursor-capture-button",
                        format!("Cursor {}", on_off(self.settings.show_cursor)),
                        SettingsAction::ToggleCursor,
                    )),
            )
            .child(
                div()
                    .flex()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "recording-format-button",
                        format!(
                            "Record {}",
                            self.settings.recording_format.label()
                        ),
                        SettingsAction::RecordingFormat,
                    ))
                    .child(self.settings_button(
                        cx,
                        "recording-quality-button",
                        format!(
                            "Quality {}",
                            self.settings.recording_quality.label()
                        ),
                        SettingsAction::RecordingQuality,
                    )),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(self.recording_status.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(self.hotkey_status.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0xaab0ba))
                    .child(SharedString::from(format!(
                        "Recording hotkey: {}",
                        self.settings
                            .recording_hotkey
                            .as_deref()
                            .unwrap_or("disabled")
                    ))),
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

        if let Some(preview_path) = self
            .capture_history
            .iter()
            .find_map(|entry| entry.preview_path.clone())
        {
            body = body.child(self.capture_preview(preview_path));
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

    fn capture_preview(&self, preview_path: PathBuf) -> impl IntoElement {
        div()
            .h(px(180.0))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x2b3039))
            .bg(rgb(0x0f1116))
            .p(px(6.0))
            .child(
                img(preview_path)
                    .size_full()
                    .object_fit(ObjectFit::Contain)
                    .with_loading(|| {
                        div()
                            .flex()
                            .items_center()
                            .justify_center()
                            .size_full()
                            .text_size(px(12.0))
                            .text_color(rgb(0x8b93a3))
                            .child("Loading preview")
                            .into_any_element()
                    })
                    .with_fallback(|| {
                        div()
                            .flex()
                            .items_center()
                            .justify_center()
                            .size_full()
                            .text_size(px(12.0))
                            .text_color(rgb(0x8b93a3))
                            .child("Preview unavailable")
                            .into_any_element()
                    }),
            )
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

    fn settings_button(
        &self,
        cx: &mut Context<Self>,
        element_id: &'static str,
        label: String,
        action: SettingsAction,
    ) -> impl IntoElement {
        div()
            .id(element_id)
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x3d4654))
            .bg(rgb(0x202631))
            .hover(|this| this.bg(rgb(0x2a3240)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child(SharedString::from(label))
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.apply_settings_action(action);
                cx.notify();
            }))
    }

    fn recording_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let label = if self.active_recording.is_some() {
            "Stop recording"
        } else {
            "Start recording"
        };

        div()
            .id("recording-toggle-button")
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x5a3d45))
            .bg(rgb(0x3a2229))
            .hover(|this| this.bg(rgb(0x4a2a33)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child(label)
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.toggle_recording();
                cx.notify();
            }))
    }

    fn run_capture(&mut self, mode: CaptureMode) {
        if self.settings.capture_delay_seconds > 0 {
            self.capture_status = format!(
                "Waiting {}s before capture.",
                self.settings.capture_delay_seconds
            );
            thread::sleep(Duration::from_secs(
                self.settings.capture_delay_seconds as u64,
            ));
        }

        let platform = host_platform();
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            let capture = match mode {
                CaptureMode::FullScreen => {
                    adapter.capture_all_screens_with_cursor(self.settings.show_cursor)
                }
                CaptureMode::ActiveWindow => adapter.active_window().and_then(|window| {
                    adapter.capture_region_with_options(CaptureRequest {
                        region: window.bounds,
                        include_cursor: self.settings.show_cursor,
                    })
                }),
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

    fn apply_settings_action(&mut self, action: SettingsAction) {
        match action {
            SettingsAction::CaptureImageFormat => {
                self.settings.capture_image_format =
                    next_capture_image_format(self.settings.capture_image_format);
                self.persist_capture_settings(format!(
                    "Image format set to {}",
                    self.settings.capture_image_format.label()
                ));
            }
            SettingsAction::ToggleClipboardCopy => {
                self.settings.copy_captures_to_clipboard =
                    !self.settings.copy_captures_to_clipboard;
                self.persist_capture_settings(format!(
                    "Copy captures {}",
                    on_off(self.settings.copy_captures_to_clipboard)
                ));
            }
            SettingsAction::ToggleCursor => {
                self.settings.show_cursor = !self.settings.show_cursor;
                self.persist_capture_settings(format!(
                    "Cursor capture {}",
                    on_off(self.settings.show_cursor)
                ));
            }
            SettingsAction::RecordingFormat => {
                self.settings.recording_format =
                    next_recording_format(self.settings.recording_format);
                self.persist_recording_settings(format!(
                    "Recording format set to {}",
                    self.settings.recording_format.label()
                ));
            }
            SettingsAction::RecordingQuality => {
                self.settings.recording_quality =
                    next_recording_quality(self.settings.recording_quality);
                self.persist_recording_settings(format!(
                    "Recording quality set to {}",
                    self.settings.recording_quality.label()
                ));
            }
        }
    }

    fn persist_capture_settings(&mut self, message: String) {
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}."),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn persist_recording_settings(&mut self, message: String) {
        self.recording_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}."),
            Err(error) => format!("Settings save failed: {error}"),
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
                self.run_capture(hotkey_capture_mode(self.settings.default_capture_mode));
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Recording => {
                self.recording_status = "Recording hotkey received.".into();
                self.toggle_recording();
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

    fn recording_destination(&self, width: u32, height: u32) -> std::path::PathBuf {
        let stem = format_file_name_template(&self.settings.file_name_template, width, height);
        let file_name = format!("{}.{}", stem, self.settings.recording_format.extension());
        let output_root = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
            self.capture_output_directory()
        } else {
            self.capture_output_directory().join("Videos")
        };
        build_available_capture_path(
            &output_root,
            &file_name,
            self.settings.save_in_monthly_folders,
        )
    }

    fn toggle_recording(&mut self) {
        if self.active_recording.is_some() {
            self.stop_recording();
        } else {
            self.start_recording();
        }
    }

    fn start_recording(&mut self) {
        #[cfg(target_os = "windows")]
        let result = (|| {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            let monitors = adapter.monitors()?;
            let region = virtual_screen_region(&monitors).ok_or_else(|| {
                oddsnap_platform::PlatformError::Failed(
                    "no monitors available for recording".into(),
                )
            })?;
            let output_path = self.recording_destination(region.width, region.height);
            let fps = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
                self.settings.gif_fps
            } else {
                self.settings.recording_fps
            };
            let handle = adapter.start_desktop_recording(VideoRecordingRequest {
                output_path,
                region: Some(region.clone()),
                format: self.settings.recording_format,
                quality: self.settings.recording_quality,
                fps,
                record_microphone: self.settings.record_microphone,
                record_desktop_audio: self.settings.record_desktop_audio,
                microphone_device_id: self.settings.microphone_device_id.clone(),
                desktop_audio_device_id: self.settings.desktop_audio_device_id.clone(),
            })?;
            Ok::<_, oddsnap_platform::PlatformError>((handle, region.width, region.height))
        })();

        #[cfg(not(target_os = "windows"))]
        let result: Result<
            (Box<dyn VideoRecordingHandle>, u32, u32),
            oddsnap_platform::PlatformError,
        > = Err(oddsnap_platform::PlatformError::Unsupported(
            "desktop recording is not implemented on this platform yet",
        ));

        self.recording_status = match result {
            Ok((handle, width, height)) => {
                let path = handle.output_path().display().to_string();
                self.active_recording = Some(ActiveRecording {
                    handle,
                    width,
                    height,
                });
                format!("Recording started: {path}")
            }
            Err(error) => format!("Recording failed to start: {error}"),
        };
    }

    fn stop_recording(&mut self) {
        let Some(mut active) = self.active_recording.take() else {
            self.recording_status = "No recording running.".into();
            return;
        };

        self.recording_status = match active.handle.stop() {
            Ok(result) => {
                let path = result.output_path.display().to_string();
                let history_status =
                    self.save_recording_history(result.output_path, active.width, active.height);
                format!("Recording saved: {path}{history_status}")
            }
            Err(error) => format!("Recording failed to stop: {error}"),
        };
    }

    fn recording_status_summary(&self) -> String {
        let fps = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
            self.settings.gif_fps
        } else {
            self.settings.recording_fps
        };
        let base = format!(
            "Recording config: {} · {} · {fps} FPS",
            self.settings.recording_format.label(),
            self.settings.recording_quality.label()
        );

        if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
            return base;
        }

        let microphone = if self.settings.record_microphone {
            "mic configured, capture pending"
        } else {
            "mic off"
        };
        let desktop_audio = if self.settings.record_desktop_audio {
            "desktop audio configured, capture pending"
        } else {
            "desktop audio off"
        };
        format!("{base} · {microphone} · {desktop_audio}")
    }

    fn capture_preferences_summary(&self) -> String {
        format!(
            "Capture prefs: default {} · delay {}s · cursor {} · crosshair {} · magnifier {} · toasts {} · UI scale {:.2}x",
            self.settings.default_capture_mode.label(),
            self.settings.capture_delay_seconds,
            on_off(self.settings.show_cursor),
            on_off(self.settings.show_crosshair_guides),
            on_off(self.settings.show_capture_magnifier),
            self.settings.toast_position.label(),
            self.settings.ui_scale
        )
    }

    fn save_recording_history(&mut self, path: PathBuf, width: u32, height: u32) -> String {
        if !self.settings.save_history {
            let preview_path = create_video_thumbnail(&self.history_store, &path)
                .or_else(|| preview_path_for_capture(&path));
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode: CaptureMode::FullScreen,
                    path: path.display().to_string(),
                    preview_path,
                    width,
                    height,
                },
            );
            self.capture_history.truncate(6);
            return String::new();
        }

        let kind = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
            HistoryKind::Gif
        } else {
            HistoryKind::Video
        };
        let thumbnail_path = create_video_thumbnail(&self.history_store, &path);
        let mut entry = match HistoryEntry::from_capture_file(path, width, height, kind) {
            Ok(entry) => entry,
            Err(error) => return format!("; history failed: {error}"),
        };
        entry.thumbnail_path = thumbnail_path;

        match self.history_store.append_entry(entry) {
            Ok(index) => {
                self.capture_history = history_entries_to_capture_history(index);
                String::new()
            }
            Err(error) => format!("; history failed: {error}"),
        }
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
                    preview_path: preview_path_for_capture(&capture.image_path),
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
fn start_capture_hotkey_listener(
    accelerator: &str,
    recording_accelerator: Option<&str>,
) -> (
    String,
    Option<oddsnap_platform_windows::WindowsHotkeyListener>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsHotkeyEvent>>,
) {
    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_windows::start_capture_and_recording_hotkey_listener(
        accelerator,
        recording_accelerator,
        sender,
    ) {
        Ok(listener) => (
            match recording_accelerator {
                Some(recording) => {
                    format!("Hotkeys: {accelerator} capture, {recording} recording.")
                }
                None => format!("Hotkey: {accelerator} listener ready."),
            },
            Some(listener),
            Some(receiver),
        ),
        Err(error) => (format!("Hotkey listener unavailable: {error}"), None, None),
    }
}

#[cfg(not(target_os = "windows"))]
fn start_capture_hotkey_listener(
    accelerator: &str,
    recording_accelerator: Option<&str>,
) -> (String, Option<()>, Option<()>) {
    let _ = accelerator;
    let _ = recording_accelerator;
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
            preview_path: entry
                .thumbnail_path
                .filter(|path| path.exists())
                .or_else(|| preview_path_for_capture(&entry.file_path)),
            width: entry.width,
            height: entry.height,
        })
        .collect()
}

fn create_video_thumbnail(history_store: &HistoryStore, media_path: &Path) -> Option<PathBuf> {
    let tools = discover_ffmpeg_tools()?;
    let output_path = video_thumbnail_path(history_store, media_path)?;
    if let Some(parent) = output_path.parent() {
        fs::create_dir_all(parent).ok()?;
    }
    let _ = fs::remove_file(&output_path);

    for seek_seconds in ["0.40", "1.00", "2.00"] {
        let request = FfmpegThumbnailRequest {
            input_path: media_path.to_path_buf(),
            output_path: output_path.clone(),
            seek_seconds: Some(seek_seconds.into()),
        };
        if run_ffmpeg_thumbnail(
            &tools.ffmpeg,
            build_video_thumbnail_args(&request),
            &output_path,
        ) {
            return Some(output_path);
        }
    }

    let request = FfmpegThumbnailRequest {
        input_path: media_path.to_path_buf(),
        output_path: output_path.clone(),
        seek_seconds: None,
    };
    if run_ffmpeg_thumbnail(
        &tools.ffmpeg,
        build_video_thumbnail_fallback_args(&request),
        &output_path,
    ) {
        Some(output_path)
    } else {
        let _ = fs::remove_file(&output_path);
        None
    }
}

fn video_thumbnail_path(history_store: &HistoryStore, media_path: &Path) -> Option<PathBuf> {
    let directory = history_store.path().parent()?.join("thumbs");
    Some(directory.join(format!("{}.jpg", stable_path_key(media_path))))
}

fn stable_path_key(path: &Path) -> String {
    let mut hash = 0xcbf29ce484222325u64;
    for byte in path.display().to_string().to_ascii_lowercase().bytes() {
        hash ^= byte as u64;
        hash = hash.wrapping_mul(0x100000001b3);
    }
    format!("{hash:016x}")
}

fn run_ffmpeg_thumbnail(ffmpeg: &Path, args: Vec<String>, output_path: &Path) -> bool {
    let status = Command::new(ffmpeg)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status();
    if !matches!(status, Ok(status) if status.success()) {
        return false;
    }
    fs::metadata(output_path)
        .map(|metadata| metadata.len() > 0)
        .unwrap_or(false)
}

fn preview_path_for_capture(path: &Path) -> Option<PathBuf> {
    let extension = path.extension()?.to_str()?;
    let is_supported = gpui::Img::extensions()
        .iter()
        .any(|supported| supported.eq_ignore_ascii_case(extension));
    if is_supported && path.exists() {
        Some(path.to_path_buf())
    } else {
        None
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

fn next_capture_image_format(format: CaptureImageFormat) -> CaptureImageFormat {
    match format {
        CaptureImageFormat::Png => CaptureImageFormat::Jpeg,
        CaptureImageFormat::Jpeg => CaptureImageFormat::Bmp,
        CaptureImageFormat::Bmp => CaptureImageFormat::Png,
    }
}

fn next_recording_format(format: RecordingFormat) -> RecordingFormat {
    match format {
        RecordingFormat::Gif => RecordingFormat::Mp4,
        RecordingFormat::Mp4 => RecordingFormat::WebM,
        RecordingFormat::WebM => RecordingFormat::Mkv,
        RecordingFormat::Mkv => RecordingFormat::Gif,
    }
}

fn next_recording_quality(quality: RecordingQuality) -> RecordingQuality {
    match quality {
        RecordingQuality::Original => RecordingQuality::P1080,
        RecordingQuality::P1080 => RecordingQuality::P720,
        RecordingQuality::P720 => RecordingQuality::P480,
        RecordingQuality::P480 => RecordingQuality::Original,
    }
}

fn hotkey_capture_mode(default_mode: DefaultCaptureMode) -> CaptureMode {
    match default_mode {
        DefaultCaptureMode::ActiveWindow => CaptureMode::ActiveWindow,
        DefaultCaptureMode::Fullscreen => CaptureMode::FullScreen,
        DefaultCaptureMode::Rectangle
        | DefaultCaptureMode::ColorPicker
        | DefaultCaptureMode::Ocr
        | DefaultCaptureMode::Scan
        | DefaultCaptureMode::Sticker
        | DefaultCaptureMode::Upscale
        | DefaultCaptureMode::Center
        | DefaultCaptureMode::Ruler => CaptureMode::FullScreen,
    }
}

fn on_off(value: bool) -> &'static str {
    if value {
        "on"
    } else {
        "off"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cycles_implemented_capture_setting_options() {
        assert_eq!(
            next_capture_image_format(CaptureImageFormat::Png),
            CaptureImageFormat::Jpeg
        );
        assert_eq!(
            next_capture_image_format(CaptureImageFormat::Jpeg),
            CaptureImageFormat::Bmp
        );
        assert_eq!(
            next_capture_image_format(CaptureImageFormat::Bmp),
            CaptureImageFormat::Png
        );
    }

    #[test]
    fn cycles_implemented_recording_setting_options() {
        assert_eq!(
            next_recording_format(RecordingFormat::Gif),
            RecordingFormat::Mp4
        );
        assert_eq!(
            next_recording_format(RecordingFormat::Mkv),
            RecordingFormat::Gif
        );
        assert_eq!(
            next_recording_quality(RecordingQuality::Original),
            RecordingQuality::P1080
        );
        assert_eq!(
            next_recording_quality(RecordingQuality::P480),
            RecordingQuality::Original
        );
    }

    #[test]
    fn hotkey_uses_supported_imported_default_capture_modes() {
        assert!(matches!(
            hotkey_capture_mode(DefaultCaptureMode::ActiveWindow),
            CaptureMode::ActiveWindow
        ));
        assert!(matches!(
            hotkey_capture_mode(DefaultCaptureMode::Fullscreen),
            CaptureMode::FullScreen
        ));
        assert!(matches!(
            hotkey_capture_mode(DefaultCaptureMode::Rectangle),
            CaptureMode::FullScreen
        ));
    }
}
