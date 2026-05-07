#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use std::{
    fs,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    thread,
    time::Duration,
};

use gpui::prelude::FluentBuilder;
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
    AiChatProvider, AppSettings, CapabilityState, CaptureImageFormat, ColorHistoryEntry,
    DefaultCaptureMode, FfmpegThumbnailRequest, HistoryEntry, HistoryIndex, HistoryKind,
    HistoryStore, PlatformCapability, RecordingFormat, RecordingQuality, SettingsStore,
    UploadDestination, UploadPreflight, UploadSettings,
};
use oddsnap_platform::{
    default_capture_directory, persist_capture_to_path_as, virtual_screen_region, CaptureRegion,
    CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService, ColorPickerService,
    OverlayWindowRequest, PlatformAdapter, RegionSelectionService, ScreenCaptureService,
    VideoRecordingHandle, VideoRecordingRequest, VideoRecordingService, WindowPickerService,
};

#[cfg(any(target_os = "windows", test))]
const WINDOWS_TRAY_FOUNDATION_STATUS: &str =
    "Tray: Windows icon and menu foundation active; OCR and scroll capture still pending.";
#[cfg(any(target_os = "macos", test))]
const MACOS_MENU_BAR_FOUNDATION_STATUS: &str =
    "Menu bar: macOS status item foundation active; OCR and scroll capture still pending.";

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
    tray_status: String,
    recording_status: String,
    active_recording: Option<ActiveRecording>,
    capture_history: Vec<CaptureHistoryEntry>,
    color_history: Vec<ColorHistoryEntry>,
    focus_handle: gpui::FocusHandle,
    #[cfg(target_os = "windows")]
    _hotkey_listener: Option<oddsnap_platform_windows::WindowsHotkeyListener>,
    #[cfg(not(target_os = "windows"))]
    _hotkey_listener: Option<CrossPlatformHotkeyListener>,
    #[cfg(target_os = "windows")]
    tray_icon: Option<oddsnap_platform_windows::WindowsTrayIcon>,
    #[cfg(target_os = "macos")]
    tray_icon: Option<oddsnap_platform_macos::MacosTrayIcon>,
}

struct CaptureHistoryEntry {
    mode: CaptureMode,
    kind: HistoryKind,
    path: String,
    preview_path: Option<PathBuf>,
    width: u32,
    height: u32,
    upload_url: Option<String>,
    upload_provider: Option<String>,
    upload_error: Option<String>,
}

struct CaptureRunResult {
    capture: oddsnap_platform::CaptureResult,
    copy_error: Option<String>,
}

struct ActiveRecording {
    handle: Box<dyn VideoRecordingHandle>,
    width: u32,
    height: u32,
}

struct RecordingStart {
    handle: Box<dyn VideoRecordingHandle>,
    width: u32,
    height: u32,
    target: RecordingTarget,
    note: Option<&'static str>,
}

#[derive(Clone, Copy)]
struct ImportedHotkeyAccelerators<'a> {
    capture: &'a str,
    recording: Option<&'a str>,
    fullscreen: Option<&'a str>,
    active_window: Option<&'a str>,
    picker: Option<&'a str>,
    ocr: Option<&'a str>,
    scan: Option<&'a str>,
    sticker: Option<&'a str>,
    upscale: Option<&'a str>,
    center: Option<&'a str>,
    ruler: Option<&'a str>,
    scroll_capture: Option<&'a str>,
    ai_redirect: Option<&'a str>,
}

impl<'a> ImportedHotkeyAccelerators<'a> {
    fn pending_tool_hotkey(self, tool: PendingTool) -> Option<&'a str> {
        match tool {
            PendingTool::Ocr => self.ocr,
            PendingTool::Scan => self.scan,
            PendingTool::Sticker => self.sticker,
            PendingTool::Upscale => self.upscale,
            PendingTool::Center => self.center,
            PendingTool::Ruler => self.ruler,
            PendingTool::ScrollCapture => self.scroll_capture,
            PendingTool::AiRedirect => self.ai_redirect,
        }
    }
}

#[cfg(any(test, not(target_os = "windows")))]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum CrossPlatformHotkeyEvent {
    Capture,
    Recording,
    FullScreenCapture,
    ActiveWindowCapture,
    ColorPicker,
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
    ScrollCapture,
    AiRedirect,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum PendingTool {
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
    ScrollCapture,
    AiRedirect,
}

impl PendingTool {
    const ALL: [Self; 8] = [
        Self::Ocr,
        Self::Scan,
        Self::Sticker,
        Self::Upscale,
        Self::Center,
        Self::Ruler,
        Self::ScrollCapture,
        Self::AiRedirect,
    ];

    fn label(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "Scan",
            Self::Sticker => "Sticker",
            Self::Upscale => "Upscale",
            Self::Center => "Center",
            Self::Ruler => "Ruler",
            Self::ScrollCapture => "Scroll capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    fn hotkey_summary_label(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "scan",
            Self::Sticker => "sticker",
            Self::Upscale => "upscale",
            Self::Center => "center",
            Self::Ruler => "ruler",
            Self::ScrollCapture => "scroll-capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    fn parity_item(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "scan",
            Self::Sticker => "sticker/background removal",
            Self::Upscale => "upscale",
            Self::Center => "center selection",
            Self::Ruler => "ruler",
            Self::ScrollCapture => "scroll capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    fn default_capture_mode(self) -> Option<DefaultCaptureMode> {
        match self {
            Self::Ocr => Some(DefaultCaptureMode::Ocr),
            Self::Scan => Some(DefaultCaptureMode::Scan),
            Self::Sticker => Some(DefaultCaptureMode::Sticker),
            Self::Upscale => Some(DefaultCaptureMode::Upscale),
            Self::Center => Some(DefaultCaptureMode::Center),
            Self::Ruler => Some(DefaultCaptureMode::Ruler),
            Self::ScrollCapture | Self::AiRedirect => None,
        }
    }

    fn from_default_capture_mode(mode: DefaultCaptureMode) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|tool| tool.default_capture_mode() == Some(mode))
    }

    #[cfg(any(test, not(target_os = "windows")))]
    fn cross_platform_hotkey_event(self) -> CrossPlatformHotkeyEvent {
        match self {
            Self::Ocr => CrossPlatformHotkeyEvent::Ocr,
            Self::Scan => CrossPlatformHotkeyEvent::Scan,
            Self::Sticker => CrossPlatformHotkeyEvent::Sticker,
            Self::Upscale => CrossPlatformHotkeyEvent::Upscale,
            Self::Center => CrossPlatformHotkeyEvent::Center,
            Self::Ruler => CrossPlatformHotkeyEvent::Ruler,
            Self::ScrollCapture => CrossPlatformHotkeyEvent::ScrollCapture,
            Self::AiRedirect => CrossPlatformHotkeyEvent::AiRedirect,
        }
    }
}

#[cfg(not(target_os = "windows"))]
struct CrossPlatformHotkeyListener {
    manager: global_hotkey::GlobalHotKeyManager,
    hotkeys: Vec<global_hotkey::hotkey::HotKey>,
    stop_sender: std::sync::mpsc::Sender<()>,
    join_handle: Option<std::thread::JoinHandle<()>>,
}

#[cfg(not(target_os = "windows"))]
impl Drop for CrossPlatformHotkeyListener {
    fn drop(&mut self) {
        let _ = self.stop_sender.send(());
        let _ = self.manager.unregister_all(&self.hotkeys);
        if let Some(join_handle) = self.join_handle.take() {
            let _ = join_handle.join();
        }
    }
}

#[derive(Clone, Copy)]
enum RecordingTarget {
    FullScreen,
    ActiveWindow,
}

impl RecordingTarget {
    fn label(self) -> &'static str {
        match self {
            Self::FullScreen => "desktop",
            Self::ActiveWindow => "active window",
        }
    }
}

#[derive(Clone, Copy)]
enum CaptureMode {
    Rectangle,
    FullScreen,
    ActiveWindow,
}

#[derive(Clone, Copy)]
enum SettingsAction {
    CaptureImageFormat,
    ToggleClipboardCopy,
    ToggleCursor,
    DefaultCaptureMode,
    CaptureDelay,
    ToggleCrosshair,
    ToggleMagnifier,
    ToggleWindowDetection,
    RecordingFormat,
    RecordingQuality,
}

impl CaptureMode {
    fn label(self) -> &'static str {
        match self {
            Self::Rectangle => "Rectangle",
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
        let (history_index, history_load_status) = match history_store.load_or_default() {
            Ok(index) => (index, None),
            Err(error) => (
                HistoryIndex::default(),
                Some(format!("History load failed, using empty history: {error}")),
            ),
        };
        let capture_history = history_entries_to_capture_history(history_index.clone());
        let color_history = history_entries_to_color_history(history_index);
        let permission_status = host_permission_status();
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
        let imported_hotkeys = ImportedHotkeyAccelerators {
            capture: &settings.capture_hotkey,
            recording: settings.recording_hotkey.as_deref(),
            fullscreen: settings.fullscreen_hotkey.as_deref(),
            active_window: settings.active_window_hotkey.as_deref(),
            picker: settings.picker_hotkey.as_deref(),
            ocr: settings.ocr_hotkey.as_deref(),
            scan: settings.scan_hotkey.as_deref(),
            sticker: settings.sticker_hotkey.as_deref(),
            upscale: settings.upscale_hotkey.as_deref(),
            center: settings.center_hotkey.as_deref(),
            ruler: settings.ruler_hotkey.as_deref(),
            scroll_capture: settings.scroll_capture_hotkey.as_deref(),
            ai_redirect: settings.ai_redirect_hotkey.as_deref(),
        };
        let (hotkey_status, hotkey_listener, hotkey_events) =
            start_capture_hotkey_listener(imported_hotkeys);
        let (tray_status, tray_icon, tray_events) = start_tray_icon();
        #[cfg(target_os = "linux")]
        let _ = &tray_icon;

        let app = Self {
            platform_name: platform.name().into(),
            native_ui_goal: profile.visual_goal,
            capabilities,
            capture_status: combine_startup_status(
                combine_startup_status(
                    combine_startup_status(capture_status, history_migration_status),
                    history_load_status,
                ),
                permission_status,
            ),
            settings,
            settings_store,
            settings_path,
            history_store,
            history_path,
            media_status,
            hotkey_status,
            tray_status,
            recording_status: "No recording running.".into(),
            active_recording: None,
            capture_history,
            color_history,
            focus_handle: cx.focus_handle(),
            #[cfg(target_os = "windows")]
            _hotkey_listener: hotkey_listener,
            #[cfg(not(target_os = "windows"))]
            _hotkey_listener: hotkey_listener,
            #[cfg(target_os = "windows")]
            tray_icon,
            #[cfg(target_os = "macos")]
            tray_icon,
        };

        app.start_hotkey_event_pump(hotkey_events, cx);
        app.start_tray_event_pump(tray_events, cx);
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

#[cfg(target_os = "macos")]
fn host_permission_status() -> Option<String> {
    let platform = oddsnap_platform_macos::MacosPlatform;
    let missing = oddsnap_platform::PermissionsService::missing_permissions(&platform);
    if missing.is_empty() {
        None
    } else {
        Some(format!("Missing permissions: {}.", missing.join(", ")))
    }
}

#[cfg(not(target_os = "macos"))]
fn host_permission_status() -> Option<String> {
    None
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
                                "capture-region-button",
                                CaptureMode::Rectangle,
                            ))
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
                            .child(self.color_picker_button(cx))
                            .child(self.recording_button(cx))
                            .child(self.recording_target_button(cx)),
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
                    .child(SharedString::from(self.advanced_settings_summary())),
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
                        "default-capture-mode-button",
                        format!("Default {}", self.settings.default_capture_mode.label()),
                        SettingsAction::DefaultCaptureMode,
                    ))
                    .child(self.settings_button(
                        cx,
                        "capture-delay-button",
                        format!("Delay {}s", self.settings.capture_delay_seconds),
                        SettingsAction::CaptureDelay,
                    ))
                    .child(self.settings_button(
                        cx,
                        "crosshair-guides-button",
                        format!(
                            "Crosshair {}",
                            on_off(self.settings.show_crosshair_guides)
                        ),
                        SettingsAction::ToggleCrosshair,
                    ))
                    .child(self.settings_button(
                        cx,
                        "capture-magnifier-button",
                        format!(
                            "Magnifier {}",
                            on_off(self.settings.show_capture_magnifier)
                        ),
                        SettingsAction::ToggleMagnifier,
                    ))
                    .child(self.settings_button(
                        cx,
                        "window-detection-button",
                        format!("Detect windows {}", on_off(self.settings.detect_windows)),
                        SettingsAction::ToggleWindowDetection,
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
                    .child(SharedString::from(self.tray_status.clone())),
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

        body = body.child(div().text_size(px(13.0)).child("Recent colors"));
        if self.color_history.is_empty() {
            body = body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0x8b93a3))
                    .child("No picked colors yet."),
            );
        } else {
            for entry in self.color_history.iter().take(6) {
                body = body.child(
                    div()
                        .flex()
                        .items_center()
                        .justify_between()
                        .rounded(px(6.0))
                        .bg(rgb(0x1d2027))
                        .px(px(10.0))
                        .py(px(8.0))
                        .child(
                            div()
                                .text_size(px(12.0))
                                .child(SharedString::from(format!("#{}", entry.hex))),
                        )
                        .child(self.copy_color_hex_button(cx, entry.hex.clone())),
                );
            }
        }

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
                    .min_w_0()
                    .w_full()
                    .overflow_hidden()
                    .rounded(px(6.0))
                    .bg(rgb(0x1d2027))
                    .px(px(10.0))
                    .py(px(8.0))
                    .child(div().text_size(px(12.0)).child(SharedString::from(format!(
                        "{} · {} · {}x{}",
                        history_kind_label(entry.kind),
                        entry.mode.label(),
                        entry.width,
                        entry.height
                    ))))
                    .child(
                        div()
                            .min_w_0()
                            .w_full()
                            .truncate()
                            .text_size(px(11.0))
                            .text_color(rgb(0x9ba3af))
                            .child(SharedString::from(entry.path.clone())),
                    )
                    .child(
                        div()
                            .min_w_0()
                            .w_full()
                            .line_clamp(2)
                            .text_size(px(11.0))
                            .text_color(rgb(0x9ba3af))
                            .child(SharedString::from(history_upload_summary(entry))),
                    )
                    .child(
                        div()
                            .flex()
                            .flex_wrap()
                            .gap(px(8.0))
                            .child(self.open_history_button(cx, entry.path.clone()))
                            .child(self.copy_history_path_button(cx, entry.path.clone()))
                            .when(entry.kind == HistoryKind::Image, |row| {
                                row.child(self.copy_history_image_button(cx, entry.path.clone()))
                            })
                            .when_some(entry.upload_url.clone(), |row, url| {
                                row.child(self.copy_upload_url_button(cx, url))
                            })
                            .child(self.remove_history_button(cx, entry.path.clone())),
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

    fn color_picker_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .id("color-picker-button")
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x4a5262))
            .bg(rgb(0x242936))
            .hover(|this| this.bg(rgb(0x303746)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child("Color")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_color_picker();
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

    fn open_history_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("open-history-{path}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Open")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.open_history_path(PathBuf::from(&path));
                cx.notify();
            }))
    }

    fn copy_history_path_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("copy-history-path-{path}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Copy path")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.copy_history_path(path.clone());
                cx.notify();
            }))
    }

    fn copy_history_image_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("copy-history-image-{path}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Copy image")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.copy_history_image(PathBuf::from(&path));
                cx.notify();
            }))
    }

    fn copy_upload_url_button(&self, cx: &mut Context<Self>, url: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("copy-upload-{url}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Copy link")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.copy_history_upload_url(url.clone());
                cx.notify();
            }))
    }

    fn remove_history_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("remove-history-{path}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Remove")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.remove_history_entry(path.clone());
                cx.notify();
            }))
    }

    fn copy_color_hex_button(&self, cx: &mut Context<Self>, hex: String) -> impl IntoElement {
        div()
            .id(SharedString::from(format!("copy-color-{hex}")))
            .rounded(px(6.0))
            .border_1()
            .border_color(rgb(0x354052))
            .bg(rgb(0x202733))
            .hover(|this| this.bg(rgb(0x2a3342)))
            .px(px(8.0))
            .py(px(4.0))
            .text_size(px(11.0))
            .text_color(rgb(0xd8dde6))
            .child("Copy")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.copy_color_history_hex(hex.clone());
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
                this.toggle_recording(RecordingTarget::FullScreen);
                cx.notify();
            }))
    }

    fn recording_target_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let label = if self.active_recording.is_some() {
            "Window busy"
        } else {
            "Record window"
        };

        div()
            .id("recording-window-button")
            .rounded(px(7.0))
            .border_1()
            .border_color(rgb(0x4f4436))
            .bg(rgb(0x31281f))
            .hover(|this| this.bg(rgb(0x3d3124)))
            .px(px(10.0))
            .py(px(6.0))
            .text_size(px(11.0))
            .child(label)
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                if this.active_recording.is_none() {
                    this.start_recording(RecordingTarget::ActiveWindow);
                }
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
            self.run_capture_with_adapter(&adapter, mode)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.run_capture_with_macos_adapter(&adapter, mode)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_capture_with_adapter(&adapter, mode)
        };

        self.capture_status = match result {
            Ok(result) => {
                let path = result.capture.image_path.display().to_string();
                let history_status = self.save_capture_history(&result.capture, mode);
                let copy_status = match (
                    self.settings.copy_captures_to_clipboard,
                    result.copy_error.as_deref(),
                ) {
                    (true, None) => "copied and saved".to_string(),
                    (true, Some(error)) => {
                        format!("saved; clipboard copy failed ({error})")
                    }
                    (false, _) => "saved".to_string(),
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

    #[cfg_attr(target_os = "macos", allow(dead_code))]
    fn run_capture_with_adapter<T>(
        &self,
        adapter: &T,
        mode: CaptureMode,
    ) -> Result<CaptureRunResult, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService
            + WindowPickerService
            + ClipboardImageService
            + RegionSelectionService,
    {
        let capture = match mode {
            CaptureMode::Rectangle => {
                let bounds = match adapter.monitors() {
                    Ok(monitors) => virtual_screen_region(&monitors).ok_or_else(|| {
                        oddsnap_platform::PlatformError::Failed(
                            "no monitors available for region selection".into(),
                        )
                    })?,
                    Err(error) if host_platform().name() == "Linux" => {
                        let _ = error;
                        CaptureRegion {
                            x: 0,
                            y: 0,
                            width: 1,
                            height: 1,
                        }
                    }
                    Err(error) => return Err(error),
                };
                match adapter.select_region(OverlayWindowRequest {
                    bounds,
                    opacity: 24,
                    click_through: false,
                    show_crosshair_guides: self.settings.show_crosshair_guides,
                    detect_windows: self.settings.detect_windows,
                })? {
                    Some(region) => adapter.capture_region_with_options(CaptureRequest {
                        region,
                        include_cursor: self.settings.show_cursor,
                    }),
                    None => Err(oddsnap_platform::PlatformError::Failed(
                        "region selection canceled".into(),
                    )),
                }
            }
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

        capture.and_then(|capture| self.save_and_copy_capture(adapter, capture))
    }

    #[cfg(target_os = "macos")]
    fn run_capture_with_macos_adapter(
        &self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        mode: CaptureMode,
    ) -> Result<CaptureRunResult, oddsnap_platform::PlatformError> {
        let capture = match mode {
            CaptureMode::Rectangle => {
                adapter.capture_interactive_selection(self.settings.show_cursor)
            }
            CaptureMode::FullScreen => {
                adapter.capture_all_screens_with_cursor(self.settings.show_cursor)
            }
            CaptureMode::ActiveWindow => adapter.active_window().and_then(|window| {
                adapter.capture_region_with_options(CaptureRequest {
                    region: window.bounds,
                    include_cursor: self.settings.show_cursor,
                })
            }),
        }?;

        self.save_and_copy_capture(adapter, capture)
    }

    fn save_and_copy_capture<T>(
        &self,
        adapter: &T,
        capture: CaptureResult,
    ) -> Result<CaptureRunResult, oddsnap_platform::PlatformError>
    where
        T: ClipboardImageService,
    {
        let destination = self.capture_destination(&capture);
        let saved = persist_capture_to_path_as(
            &capture,
            &destination,
            self.settings.capture_image_format,
            self.settings.jpeg_quality,
        )?;
        let copy_error = if self.settings.copy_captures_to_clipboard {
            adapter
                .copy_image_to_clipboard(&saved.image_path)
                .err()
                .map(|error| error.to_string())
        } else {
            None
        };
        Ok(CaptureRunResult {
            capture: saved,
            copy_error,
        })
    }

    fn run_color_picker(&mut self) {
        let platform = host_platform();
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            adapter.sample_cursor_color().map(|sample| {
                let bare = sample.bare_hex_rgb();
                let copied = adapter.copy_text_to_clipboard(&bare).is_ok();
                (sample.hex_rgb(), bare, copied)
            })
        };

        #[cfg(target_os = "linux")]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            adapter.sample_cursor_color().map(|sample| {
                let bare = sample.bare_hex_rgb();
                let copied = adapter.copy_text_to_clipboard(&bare).is_ok();
                (sample.hex_rgb(), bare, copied)
            })
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            adapter.sample_cursor_color().map(|sample| {
                let bare = sample.bare_hex_rgb();
                let copied = adapter.copy_text_to_clipboard(&bare).is_ok();
                (sample.hex_rgb(), bare, copied)
            })
        };

        #[cfg(all(
            not(target_os = "windows"),
            not(target_os = "linux"),
            not(target_os = "macos")
        ))]
        let result: Result<(String, String, bool), oddsnap_platform::PlatformError> =
            Err(oddsnap_platform::PlatformError::Unsupported(
                "color picker is pending on this platform",
            ));

        self.capture_status = match result {
            Ok((hex, bare, copied)) => {
                let history_status = self.save_color_history(bare);
                if copied {
                    format!("Picked color {hex} and copied it to clipboard{history_status}.")
                } else {
                    format!("Picked color {hex}; clipboard copy failed{history_status}.")
                }
            }
            Err(error) => format!("{} color picker failed: {error}", platform.name()),
        };
    }

    fn run_default_capture_command(&mut self, trigger: &'static str) {
        match default_capture_action(self.settings.default_capture_mode) {
            DefaultCaptureAction::Capture(mode) => {
                self.capture_status = format!("{trigger} received.");
                self.run_capture(mode);
            }
            DefaultCaptureAction::ColorPicker => {
                self.capture_status = format!("{trigger} received.");
                self.run_color_picker();
            }
            DefaultCaptureAction::Pending(tool) => {
                self.capture_status = pending_default_capture_status(trigger, tool);
            }
        }
    }

    fn save_color_history(&mut self, bare_hex: String) -> String {
        match self
            .history_store
            .append_color_entry(ColorHistoryEntry::new(bare_hex))
        {
            Ok(index) => {
                self.color_history = history_entries_to_color_history(index);
                "; history saved".into()
            }
            Err(error) => format!("; history save failed: {error}"),
        }
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
            SettingsAction::DefaultCaptureMode => {
                self.settings.default_capture_mode =
                    next_default_capture_mode(self.settings.default_capture_mode);
                self.persist_capture_settings(format!(
                    "Default capture mode set to {}",
                    self.settings.default_capture_mode.label()
                ));
            }
            SettingsAction::CaptureDelay => {
                self.settings.capture_delay_seconds =
                    next_capture_delay_seconds(self.settings.capture_delay_seconds);
                self.persist_capture_settings(format!(
                    "Capture delay set to {}s",
                    self.settings.capture_delay_seconds
                ));
            }
            SettingsAction::ToggleCrosshair => {
                self.settings.show_crosshair_guides = !self.settings.show_crosshair_guides;
                self.persist_capture_settings(format!(
                    "Crosshair guides {}",
                    on_off(self.settings.show_crosshair_guides)
                ));
            }
            SettingsAction::ToggleMagnifier => {
                self.settings.show_capture_magnifier = !self.settings.show_capture_magnifier;
                self.persist_capture_settings(format!(
                    "Capture magnifier {}",
                    on_off(self.settings.show_capture_magnifier)
                ));
            }
            SettingsAction::ToggleWindowDetection => {
                self.settings.detect_windows = !self.settings.detect_windows;
                self.persist_capture_settings(format!(
                    "Window detection {}",
                    on_off(self.settings.detect_windows)
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

    fn open_history_path(&mut self, path: PathBuf) {
        self.capture_status = match reveal_history_path(&path) {
            Ok(action) => format!("{action} {}.", path.display()),
            Err(error) => format!("Open failed: {error}"),
        };
    }

    fn copy_history_upload_url(&mut self, url: String) {
        let result = copy_text_to_host_clipboard(&url);

        self.capture_status = match result {
            Ok(()) => "Upload link copied.".into(),
            Err(error) => format!("Copy upload link failed: {error}"),
        };
    }

    fn copy_history_path(&mut self, path: String) {
        let result = copy_text_to_host_clipboard(&path);

        self.capture_status = match result {
            Ok(()) => "Capture path copied.".into(),
            Err(error) => format!("Copy capture path failed: {error}"),
        };
    }

    fn copy_history_image(&mut self, path: PathBuf) {
        let result = copy_image_to_host_clipboard(&path);

        self.capture_status = match result {
            Ok(()) => "Capture image copied.".into(),
            Err(error) => format!("Copy capture image failed: {error}"),
        };
    }

    fn run_ai_redirect(&mut self) {
        let upload_settings =
            UploadSettings::from_json_value(self.settings.image_upload_settings.as_ref());
        let provider = upload_settings.ai_chat_provider;
        if provider == AiChatProvider::None {
            self.capture_status =
                "AI Redirect not configured; choose a provider in Settings -> Uploads.".into();
            return;
        }
        if provider == AiChatProvider::GoogleLens {
            self.capture_status =
                "Google Lens AI Redirect needs Rust upload destination parity first.".into();
            return;
        }

        let Some(path) = newest_history_image_path(&self.capture_history) else {
            self.capture_status =
                "AI Redirect needs a saved image capture in Rust history first.".into();
            return;
        };

        let copy_status = match copy_image_to_host_clipboard(&path) {
            Ok(()) => "copied newest image",
            Err(error) => {
                self.capture_status = format!("AI Redirect image copy failed: {error}");
                return;
            }
        };

        let start_url = provider.start_url();
        self.capture_status = match open_external_url(start_url) {
            Ok(()) => format!(
                "AI Redirect opened {} and {copy_status}.",
                provider.display_name()
            ),
            Err(error) => format!("AI Redirect open failed: {error}"),
        };
    }

    fn remove_history_entry(&mut self, path: String) {
        self.capture_status = match self.history_store.remove_entry(Path::new(&path)) {
            Ok(index) => {
                self.capture_history = history_entries_to_capture_history(index);
                format!("Removed {path} from history.")
            }
            Err(error) => format!("Remove from history failed: {error}"),
        };
    }

    fn copy_color_history_hex(&mut self, hex: String) {
        let result = copy_text_to_host_clipboard(&hex);

        self.capture_status = match result {
            Ok(()) => format!("Color {hex} copied."),
            Err(error) => format!("Copy color failed: {error}"),
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
    fn start_hotkey_event_pump(
        &self,
        receiver: Option<std::sync::mpsc::Receiver<CrossPlatformHotkeyEvent>>,
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

    #[cfg(target_os = "windows")]
    fn start_tray_event_pump(
        &self,
        receiver: Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsTrayEvent>>,
        cx: &mut Context<Self>,
    ) {
        let Some(receiver) = receiver else {
            return;
        };

        cx.spawn(async move |this, cx| loop {
            while let Ok(event) = receiver.try_recv() {
                let _ = this.update(cx, |app, cx| {
                    app.handle_tray_event(event, cx);
                    cx.notify();
                });
            }

            cx.background_executor()
                .timer(std::time::Duration::from_millis(100))
                .await;
        })
        .detach();
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    fn start_tray_event_pump(&self, _: Option<()>, _: &mut Context<Self>) {}

    #[cfg(target_os = "macos")]
    fn start_tray_event_pump(
        &self,
        receiver: Option<std::sync::mpsc::Receiver<oddsnap_platform_macos::MacosTrayEvent>>,
        cx: &mut Context<Self>,
    ) {
        let Some(receiver) = receiver else {
            return;
        };

        cx.spawn(async move |this, cx| loop {
            while let Ok(event) = receiver.try_recv() {
                let _ = this.update(cx, |app, cx| {
                    app.handle_tray_event(event, cx);
                    cx.notify();
                });
            }

            cx.background_executor()
                .timer(std::time::Duration::from_millis(100))
                .await;
        })
        .detach();
    }

    #[cfg(target_os = "windows")]
    fn handle_hotkey_event(&mut self, event: oddsnap_platform_windows::WindowsHotkeyEvent) {
        match event {
            oddsnap_platform_windows::WindowsHotkeyEvent::Capture => {
                self.run_default_capture_command("Capture hotkey");
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Recording => {
                self.recording_status = "Recording hotkey received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::FullScreenCapture => {
                self.capture_status = "Full-screen hotkey received.".into();
                self.run_capture(CaptureMode::FullScreen);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::ActiveWindowCapture => {
                self.capture_status = "Active-window hotkey received.".into();
                self.run_capture(CaptureMode::ActiveWindow);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::ColorPicker => {
                self.capture_status = "Color-picker hotkey received.".into();
                self.run_color_picker();
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Ocr => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Ocr);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Scan => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Scan);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Sticker => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Sticker);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Upscale => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Upscale);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Center => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Center);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Ruler => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Ruler);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::ScrollCapture => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::ScrollCapture);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::AiRedirect => {
                self.run_ai_redirect();
            }
        }
    }

    #[cfg(not(target_os = "windows"))]
    fn handle_hotkey_event(&mut self, event: CrossPlatformHotkeyEvent) {
        match event {
            CrossPlatformHotkeyEvent::Capture => {
                self.run_default_capture_command("Capture hotkey");
            }
            CrossPlatformHotkeyEvent::Recording => {
                self.recording_status = "Recording hotkey received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            CrossPlatformHotkeyEvent::FullScreenCapture => {
                self.capture_status = "Full-screen hotkey received.".into();
                self.run_capture(CaptureMode::FullScreen);
            }
            CrossPlatformHotkeyEvent::ActiveWindowCapture => {
                self.capture_status = "Active-window hotkey received.".into();
                self.run_capture(CaptureMode::ActiveWindow);
            }
            CrossPlatformHotkeyEvent::ColorPicker => {
                self.capture_status = "Color-picker hotkey received.".into();
                self.run_color_picker();
            }
            CrossPlatformHotkeyEvent::Ocr => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Ocr);
            }
            CrossPlatformHotkeyEvent::Scan => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Scan);
            }
            CrossPlatformHotkeyEvent::Sticker => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Sticker);
            }
            CrossPlatformHotkeyEvent::Upscale => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Upscale);
            }
            CrossPlatformHotkeyEvent::Center => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Center);
            }
            CrossPlatformHotkeyEvent::Ruler => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::Ruler);
            }
            CrossPlatformHotkeyEvent::ScrollCapture => {
                self.capture_status = pending_tool_hotkey_status(PendingTool::ScrollCapture);
            }
            CrossPlatformHotkeyEvent::AiRedirect => {
                self.run_ai_redirect();
            }
        }
    }

    #[cfg(target_os = "windows")]
    fn handle_tray_event(
        &mut self,
        event: oddsnap_platform_windows::WindowsTrayEvent,
        cx: &mut Context<Self>,
    ) {
        match event {
            oddsnap_platform_windows::WindowsTrayEvent::Capture => {
                if self.active_recording.is_some() {
                    self.recording_status = "Tray stop recording received.".into();
                    self.stop_recording();
                } else {
                    self.run_default_capture_command("Tray capture");
                }
            }
            oddsnap_platform_windows::WindowsTrayEvent::ToggleRecording => {
                self.recording_status = "Tray recording command received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_windows::WindowsTrayEvent::Ocr => {
                self.capture_status =
                    pending_tool_trigger_status("Tray text capture", PendingTool::Ocr);
            }
            oddsnap_platform_windows::WindowsTrayEvent::ColorPicker => {
                self.capture_status = "Tray color picker received.".into();
                self.run_color_picker();
            }
            oddsnap_platform_windows::WindowsTrayEvent::ScrollCapture => {
                self.capture_status =
                    pending_tool_trigger_status("Tray scroll capture", PendingTool::ScrollCapture);
            }
            oddsnap_platform_windows::WindowsTrayEvent::Settings => {
                self.tray_status = "Tray settings command focused the Rust window.".into();
                cx.activate(true);
            }
            oddsnap_platform_windows::WindowsTrayEvent::History => {
                self.tray_status = "Tray history command focused recent captures.".into();
                cx.activate(true);
            }
            oddsnap_platform_windows::WindowsTrayEvent::Quit => {
                self.tray_status = "Tray quit requested.".into();
                cx.quit();
            }
        }
    }

    #[cfg(target_os = "macos")]
    fn handle_tray_event(
        &mut self,
        event: oddsnap_platform_macos::MacosTrayEvent,
        cx: &mut Context<Self>,
    ) {
        match event {
            oddsnap_platform_macos::MacosTrayEvent::Capture => {
                if self.active_recording.is_some() {
                    self.recording_status = "Menu bar stop recording received.".into();
                    self.stop_recording();
                } else {
                    self.run_default_capture_command("Menu bar capture");
                }
            }
            oddsnap_platform_macos::MacosTrayEvent::ToggleRecording => {
                self.recording_status = "Menu bar recording command received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_macos::MacosTrayEvent::Ocr => {
                self.capture_status =
                    pending_tool_trigger_status("Menu bar text capture", PendingTool::Ocr);
            }
            oddsnap_platform_macos::MacosTrayEvent::ColorPicker => {
                self.capture_status = "Menu bar color picker received.".into();
                self.run_color_picker();
            }
            oddsnap_platform_macos::MacosTrayEvent::ScrollCapture => {
                self.capture_status = pending_tool_trigger_status(
                    "Menu bar scroll capture",
                    PendingTool::ScrollCapture,
                );
            }
            oddsnap_platform_macos::MacosTrayEvent::Settings => {
                self.tray_status = "Menu bar settings command focused the Rust window.".into();
                cx.activate(true);
            }
            oddsnap_platform_macos::MacosTrayEvent::History => {
                self.tray_status = "Menu bar history command focused recent captures.".into();
                cx.activate(true);
            }
            oddsnap_platform_macos::MacosTrayEvent::Quit => {
                self.tray_status = "Menu bar quit requested.".into();
                cx.quit();
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

    #[cfg_attr(
        all(not(target_os = "windows"), not(target_os = "linux")),
        allow(dead_code)
    )]
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

    fn toggle_recording(&mut self, target: RecordingTarget) {
        if self.active_recording.is_some() {
            self.stop_recording();
        } else {
            self.start_recording(target);
        }
    }

    fn sync_tray_recording_state(&mut self) {
        #[cfg(target_os = "windows")]
        {
            if let Some(tray_icon) = &self.tray_icon {
                if let Err(error) = tray_icon.set_recording_state(self.active_recording.is_some()) {
                    self.tray_status = format!("Tray recording state update failed: {error}");
                }
            }
        }
        #[cfg(target_os = "macos")]
        {
            if let Some(tray_icon) = &self.tray_icon {
                if let Err(error) = tray_icon.set_recording_state(self.active_recording.is_some()) {
                    self.tray_status = format!("Menu bar recording state update failed: {error}");
                }
            }
        }
    }

    fn start_recording(&mut self, target: RecordingTarget) {
        #[cfg(target_os = "windows")]
        let result =
            self.start_recording_with_adapter(oddsnap_platform_windows::WindowsPlatform, target);

        #[cfg(target_os = "linux")]
        let result =
            self.start_recording_with_adapter(oddsnap_platform_linux::LinuxPlatform, target);

        #[cfg(target_os = "macos")]
        let result =
            self.start_recording_with_adapter(oddsnap_platform_macos::MacosPlatform, target);

        #[cfg(all(
            not(target_os = "windows"),
            not(target_os = "linux"),
            not(target_os = "macos")
        ))]
        let result: Result<RecordingStart, oddsnap_platform::PlatformError> = {
            let _ = target;
            Err(oddsnap_platform::PlatformError::Unsupported(
                "desktop recording is not implemented on this platform yet",
            ))
        };

        self.recording_status = match result {
            Ok(start) => {
                let path = start.handle.output_path().display().to_string();
                self.active_recording = Some(ActiveRecording {
                    handle: start.handle,
                    width: start.width,
                    height: start.height,
                });
                self.sync_tray_recording_state();
                match start.note {
                    Some(note) => {
                        format!(
                            "Recording {} started: {path} ({note})",
                            start.target.label()
                        )
                    }
                    None => format!("Recording {} started: {path}", start.target.label()),
                }
            }
            Err(error) => format!("Recording failed to start: {error}"),
        };
    }

    #[cfg(any(target_os = "windows", target_os = "linux", target_os = "macos"))]
    fn start_recording_with_adapter<T>(
        &self,
        adapter: T,
        target: RecordingTarget,
    ) -> Result<RecordingStart, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService + WindowPickerService + VideoRecordingService,
    {
        let region = match target {
            RecordingTarget::FullScreen => {
                let monitors = adapter.monitors()?;
                virtual_screen_region(&monitors).ok_or_else(|| {
                    oddsnap_platform::PlatformError::Failed(
                        "no monitors available for recording".into(),
                    )
                })?
            }
            RecordingTarget::ActiveWindow => adapter.active_window()?.bounds,
        };
        let output_path = self.recording_destination(region.width, region.height);
        let fps = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
            self.settings.gif_fps
        } else {
            self.settings.recording_fps
        };
        let (record_microphone, record_desktop_audio, note) =
            recording_audio_request_for_host(&self.settings);

        let handle = adapter.start_desktop_recording(VideoRecordingRequest {
            output_path,
            region: Some(region.clone()),
            format: self.settings.recording_format,
            quality: self.settings.recording_quality,
            fps,
            record_microphone,
            record_desktop_audio,
            microphone_device_id: self.settings.microphone_device_id.clone(),
            desktop_audio_device_id: self.settings.desktop_audio_device_id.clone(),
        })?;

        Ok(RecordingStart {
            handle,
            width: region.width,
            height: region.height,
            target,
            note,
        })
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
                self.sync_tray_recording_state();
                format!("Recording saved: {path}{history_status}")
            }
            Err(error) => {
                self.sync_tray_recording_state();
                format!("Recording failed to stop: {error}")
            }
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

    fn advanced_settings_summary(&self) -> String {
        let upload = if self.settings.auto_upload_screenshots
            || self.settings.auto_upload_gifs
            || self.settings.auto_upload_videos
        {
            upload_settings_summary(&self.settings)
        } else {
            "upload off".into()
        };
        let enabled_tools = self
            .settings
            .enabled_tools
            .as_ref()
            .map_or("all tools".into(), |tools| format!("{} tools", tools.len()));
        format!(
            "Advanced prefs: OCR {} · translate model {} · {} · {} · {} custom hotkeys",
            self.settings.ocr_language_tag,
            self.settings.translation_model,
            upload,
            enabled_tools,
            self.settings.tool_hotkeys.len()
        )
    }

    fn pending_upload_metadata(
        &self,
        kind: HistoryKind,
        path: &Path,
        use_ai_redirect: bool,
    ) -> (Option<String>, Option<String>) {
        match oddsnap_core::upload_preflight_for_media(&self.settings, kind, path, use_ai_redirect)
        {
            UploadPreflight::Disabled => (None, None),
            UploadPreflight::Ready { provider_name, .. } => (
                Some(provider_name.clone()),
                Some(format!(
                    "pending: Rust upload backend for {provider_name} is pending; upload was not attempted."
                )),
            ),
            UploadPreflight::Blocked {
                provider_name,
                error,
                ..
            } => (Some(provider_name), Some(error)),
        }
    }

    fn save_recording_history(&mut self, path: PathBuf, width: u32, height: u32) -> String {
        if !self.settings.save_history {
            let preview_path = create_video_thumbnail(&self.history_store, &path)
                .or_else(|| preview_path_for_capture(&path));
            let kind = if self.settings.recording_format == oddsnap_core::RecordingFormat::Gif {
                HistoryKind::Gif
            } else {
                HistoryKind::Video
            };
            let (upload_provider, upload_error) = self.pending_upload_metadata(kind, &path, false);
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode: CaptureMode::FullScreen,
                    kind,
                    path: path.display().to_string(),
                    preview_path,
                    width,
                    height,
                    upload_url: None,
                    upload_provider,
                    upload_error,
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
        let (upload_provider, upload_error) =
            self.pending_upload_metadata(kind, &entry.file_path, false);
        entry.upload_provider = upload_provider;
        entry.upload_error = upload_error;

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
            let (upload_provider, upload_error) =
                self.pending_upload_metadata(HistoryKind::Image, &capture.image_path, false);
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode,
                    kind: HistoryKind::Image,
                    path: capture.image_path.display().to_string(),
                    preview_path: preview_path_for_capture(&capture.image_path),
                    width: capture.region.width,
                    height: capture.region.height,
                    upload_url: None,
                    upload_provider,
                    upload_error,
                },
            );
            self.capture_history.truncate(6);
            return String::new();
        }

        let mut entry = match HistoryEntry::from_capture_file(
            capture.image_path.clone(),
            capture.region.width,
            capture.region.height,
            HistoryKind::Image,
        ) {
            Ok(entry) => entry,
            Err(error) => return format!("; history failed: {error}"),
        };
        let (upload_provider, upload_error) =
            self.pending_upload_metadata(HistoryKind::Image, &entry.file_path, false);
        entry.upload_provider = upload_provider;
        entry.upload_error = upload_error;

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
    accelerators: ImportedHotkeyAccelerators<'_>,
) -> (
    String,
    Option<oddsnap_platform_windows::WindowsHotkeyListener>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsHotkeyEvent>>,
) {
    if let Err(error) = validate_unique_hotkey_bindings(accelerators) {
        return (format!("Hotkey listener unavailable: {error}"), None, None);
    }

    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_windows::start_oddsnap_hotkey_listener(
        oddsnap_platform_windows::WindowsHotkeyAccelerators {
            capture: accelerators.capture,
            recording: accelerators.recording,
            fullscreen: accelerators.fullscreen,
            active_window: accelerators.active_window,
            picker: accelerators.picker,
            ocr: accelerators.ocr,
            scan: accelerators.scan,
            sticker: accelerators.sticker,
            upscale: accelerators.upscale,
            center: accelerators.center,
            ruler: accelerators.ruler,
            scroll_capture: accelerators.scroll_capture,
            ai_redirect: accelerators.ai_redirect,
        },
        sender,
    ) {
        Ok(listener) => (
            hotkey_status_summary(accelerators),
            Some(listener),
            Some(receiver),
        ),
        Err(error) => (format!("Hotkey listener unavailable: {error}"), None, None),
    }
}

#[cfg(not(target_os = "windows"))]
fn start_capture_hotkey_listener(
    accelerators: ImportedHotkeyAccelerators<'_>,
) -> (
    String,
    Option<CrossPlatformHotkeyListener>,
    Option<std::sync::mpsc::Receiver<CrossPlatformHotkeyEvent>>,
) {
    if let Err(error) = validate_unique_hotkey_bindings(accelerators) {
        return (format!("Hotkey listener unavailable: {error}"), None, None);
    }

    match start_cross_platform_hotkey_listener(accelerators) {
        Ok((listener, receiver)) => (
            cross_platform_hotkey_status_summary(accelerators),
            Some(listener),
            Some(receiver),
        ),
        Err(error) => (format!("Hotkey listener unavailable: {error}"), None, None),
    }
}

#[cfg(not(target_os = "windows"))]
fn start_cross_platform_hotkey_listener(
    accelerators: ImportedHotkeyAccelerators<'_>,
) -> Result<
    (
        CrossPlatformHotkeyListener,
        std::sync::mpsc::Receiver<CrossPlatformHotkeyEvent>,
    ),
    String,
> {
    #[cfg(target_os = "linux")]
    validate_linux_hotkey_session()?;

    let registrations = cross_platform_hotkey_registrations(accelerators);
    let mut id_to_event = std::collections::HashMap::new();
    let mut hotkeys = Vec::new();
    for (accelerator, event) in registrations {
        let hotkey = parse_cross_platform_hotkey(accelerator)?;
        id_to_event.insert(hotkey.id(), event);
        hotkeys.push(hotkey);
    }

    let manager = global_hotkey::GlobalHotKeyManager::new()
        .map_err(|error| format!("failed to initialize global-hotkey manager: {error}"))?;
    manager
        .register_all(&hotkeys)
        .map_err(|error| format!("failed to register hotkeys: {error}"))?;

    let (event_sender, event_receiver) = std::sync::mpsc::channel();
    let (stop_sender, stop_receiver) = std::sync::mpsc::channel();
    let global_receiver = global_hotkey::GlobalHotKeyEvent::receiver().clone();
    let join_handle = std::thread::spawn(move || loop {
        if stop_receiver.try_recv().is_ok() {
            break;
        }

        match global_receiver.recv_timeout(Duration::from_millis(50)) {
            Ok(event) if event.state == global_hotkey::HotKeyState::Released => {
                if let Some(mapped) = id_to_event.get(&event.id()).copied() {
                    let _ = event_sender.send(mapped);
                }
            }
            Ok(_) => {}
            Err(_) => {}
        }
    });

    Ok((
        CrossPlatformHotkeyListener {
            manager,
            hotkeys,
            stop_sender,
            join_handle: Some(join_handle),
        },
        event_receiver,
    ))
}

#[cfg(any(test, not(target_os = "windows")))]
fn cross_platform_hotkey_registrations(
    accelerators: ImportedHotkeyAccelerators<'_>,
) -> Vec<(&str, CrossPlatformHotkeyEvent)> {
    let mut registrations = vec![(accelerators.capture, CrossPlatformHotkeyEvent::Capture)];
    if let Some(recording) = non_empty_hotkey(accelerators.recording) {
        registrations.push((recording, CrossPlatformHotkeyEvent::Recording));
    }
    if let Some(fullscreen) = non_empty_hotkey(accelerators.fullscreen) {
        registrations.push((fullscreen, CrossPlatformHotkeyEvent::FullScreenCapture));
    }
    if let Some(active_window) = non_empty_hotkey(accelerators.active_window) {
        registrations.push((active_window, CrossPlatformHotkeyEvent::ActiveWindowCapture));
    }
    if let Some(picker) = non_empty_hotkey(accelerators.picker) {
        registrations.push((picker, CrossPlatformHotkeyEvent::ColorPicker));
    }
    for tool in PendingTool::ALL {
        if let Some(accelerator) = non_empty_hotkey(accelerators.pending_tool_hotkey(tool)) {
            registrations.push((accelerator, tool.cross_platform_hotkey_event()));
        }
    }
    registrations
}

#[cfg(any(test, not(target_os = "windows")))]
fn parse_cross_platform_hotkey(accelerator: &str) -> Result<global_hotkey::hotkey::HotKey, String> {
    let hotkey = accelerator
        .parse::<global_hotkey::hotkey::HotKey>()
        .map_err(|error| format!("invalid hotkey {accelerator:?}: {error}"))?;
    if hotkey.mods.is_empty() {
        return Err(format!(
            "invalid hotkey {accelerator:?}: global hotkeys must include a modifier"
        ));
    }
    Ok(hotkey)
}

fn non_empty_hotkey(value: Option<&str>) -> Option<&str> {
    value.filter(|value| !value.trim().is_empty())
}

fn validate_unique_hotkey_bindings(
    accelerators: ImportedHotkeyAccelerators<'_>,
) -> Result<(), String> {
    let mut seen = std::collections::HashMap::<String, &'static str>::new();
    let mut bindings = vec![(accelerators.capture, "capture")];
    if let Some(recording) = non_empty_hotkey(accelerators.recording) {
        bindings.push((recording, "recording"));
    }
    if let Some(fullscreen) = non_empty_hotkey(accelerators.fullscreen) {
        bindings.push((fullscreen, "full-screen"));
    }
    if let Some(active_window) = non_empty_hotkey(accelerators.active_window) {
        bindings.push((active_window, "active-window"));
    }
    if let Some(picker) = non_empty_hotkey(accelerators.picker) {
        bindings.push((picker, "color-picker"));
    }
    for tool in PendingTool::ALL {
        if let Some(accelerator) = non_empty_hotkey(accelerators.pending_tool_hotkey(tool)) {
            bindings.push((accelerator, tool.hotkey_summary_label()));
        }
    }

    for (accelerator, label) in bindings {
        let normalized = normalize_hotkey_for_duplicate_check(accelerator);
        if let Some(previous) = seen.insert(normalized, label) {
            return Err(format!(
                "hotkey {accelerator} is assigned to both {previous} and {label}"
            ));
        }
    }
    Ok(())
}

fn normalize_hotkey_for_duplicate_check(accelerator: &str) -> String {
    let mut parts = accelerator
        .split('+')
        .map(|part| part.trim().to_ascii_lowercase())
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>();
    parts.sort();
    parts.join("+")
}

#[cfg(target_os = "linux")]
fn validate_linux_hotkey_session() -> Result<(), String> {
    linux_hotkey_session_error(
        std::env::var("XDG_SESSION_TYPE").ok().as_deref(),
        std::env::var("DISPLAY").ok().as_deref(),
        std::env::var("WAYLAND_DISPLAY").ok().as_deref(),
    )
    .map_or(Ok(()), |error| Err(error.into()))
}

#[cfg(any(test, target_os = "linux"))]
fn linux_hotkey_session_error(
    session_type: Option<&str>,
    display: Option<&str>,
    wayland_display: Option<&str>,
) -> Option<&'static str> {
    let session_type = session_type.unwrap_or_default().trim();
    let display = display.unwrap_or_default().trim();
    let wayland_display = wayland_display.unwrap_or_default().trim();

    if session_type.eq_ignore_ascii_case("wayland")
        || (!wayland_display.is_empty() && display.is_empty())
    {
        return Some(
            "Linux Wayland global hotkeys need portal or compositor support; OddSnap Rust currently supports global hotkeys only on X11.",
        );
    }

    if display.is_empty() {
        return Some("Linux X11 global hotkeys need DISPLAY; Wayland portal hotkeys are not implemented yet.");
    }

    None
}

#[cfg(not(target_os = "windows"))]
fn cross_platform_hotkey_status_summary(accelerators: ImportedHotkeyAccelerators<'_>) -> String {
    let status = hotkey_status_summary(accelerators);
    #[cfg(target_os = "linux")]
    {
        let mut status = status;
        status.push_str(" Linux global hotkeys use X11; Wayland support is still pending.");
        status
    }
    #[cfg(not(target_os = "linux"))]
    {
        status
    }
}

#[cfg(target_os = "windows")]
fn start_tray_icon() -> (
    String,
    Option<oddsnap_platform_windows::WindowsTrayIcon>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_windows::WindowsTrayEvent>>,
) {
    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_windows::start_oddsnap_tray_icon(sender) {
        Ok(tray_icon) => (
            WINDOWS_TRAY_FOUNDATION_STATUS.into(),
            Some(tray_icon),
            Some(receiver),
        ),
        Err(error) => (format!("Tray unavailable: {error}"), None, None),
    }
}

#[cfg(target_os = "macos")]
fn start_tray_icon() -> (
    String,
    Option<oddsnap_platform_macos::MacosTrayIcon>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_macos::MacosTrayEvent>>,
) {
    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_macos::start_oddsnap_tray_icon(sender) {
        Ok(tray_icon) => (
            MACOS_MENU_BAR_FOUNDATION_STATUS.into(),
            Some(tray_icon),
            Some(receiver),
        ),
        Err(error) => (format!("Menu bar unavailable: {error}"), None, None),
    }
}

#[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
fn start_tray_icon() -> (String, Option<()>, Option<()>) {
    ("Tray: pending on this platform.".into(), None, None)
}

fn hotkey_status_summary(accelerators: ImportedHotkeyAccelerators<'_>) -> String {
    let mut parts = vec![format!("{} capture", accelerators.capture)];
    if let Some(recording) = accelerators.recording {
        parts.push(format!("{recording} recording"));
    }
    if let Some(fullscreen) = accelerators.fullscreen {
        parts.push(format!("{fullscreen} full-screen"));
    }
    if let Some(active_window) = accelerators.active_window {
        parts.push(format!("{active_window} active-window"));
    }
    if let Some(picker) = accelerators.picker {
        parts.push(format!("{picker} color-picker"));
    }
    for tool in PendingTool::ALL {
        if let Some(accelerator) = accelerators.pending_tool_hotkey(tool) {
            parts.push(format!("{accelerator} {}", tool.hotkey_summary_label()));
        }
    }
    format!("Hotkeys: {}.", parts.join(", "))
}

fn history_entries_to_capture_history(index: HistoryIndex) -> Vec<CaptureHistoryEntry> {
    index
        .entries
        .into_iter()
        .take(6)
        .map(|entry| CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: entry.kind,
            path: entry.file_path.display().to_string(),
            preview_path: entry
                .thumbnail_path
                .filter(|path| path.exists())
                .or_else(|| preview_path_for_capture(&entry.file_path)),
            width: entry.width,
            height: entry.height,
            upload_url: entry.upload_url,
            upload_provider: entry.upload_provider,
            upload_error: entry.upload_error,
        })
        .collect()
}

fn history_entries_to_color_history(index: HistoryIndex) -> Vec<ColorHistoryEntry> {
    index.colors.into_iter().take(12).collect()
}

fn history_kind_label(kind: HistoryKind) -> &'static str {
    match kind {
        HistoryKind::Image => "Image",
        HistoryKind::Gif => "GIF",
        HistoryKind::Video => "Video",
        HistoryKind::Sticker => "Sticker",
    }
}

fn history_upload_summary(entry: &CaptureHistoryEntry) -> String {
    if let Some(error) = entry
        .upload_error
        .as_deref()
        .filter(|error| !error.is_empty())
    {
        if let Some(pending) = error.strip_prefix("pending: ") {
            return format!("Upload pending: {pending}");
        }
        return format!("Upload failed: {error}");
    }

    if let Some(url) = entry.upload_url.as_deref().filter(|url| !url.is_empty()) {
        let provider = entry
            .upload_provider
            .as_deref()
            .filter(|provider| !provider.is_empty())
            .unwrap_or("uploaded");
        return format!("{provider}: {url}");
    }

    "No upload link".into()
}

fn newest_history_image_path(history: &[CaptureHistoryEntry]) -> Option<PathBuf> {
    history
        .iter()
        .filter(|entry| matches!(entry.kind, HistoryKind::Image | HistoryKind::Sticker))
        .map(|entry| PathBuf::from(&entry.path))
        .find(|path| path.exists())
}

fn upload_settings_summary(settings: &AppSettings) -> String {
    let destination = UploadDestination::from_legacy_name(&settings.image_upload_destination);
    let upload_settings = UploadSettings::from_json_value(settings.image_upload_settings.as_ref());
    let effective_destination = if destination == UploadDestination::AiChat {
        upload_settings.ai_chat_upload_destination()
    } else {
        destination
    };
    let label = effective_destination.display_name();

    match effective_destination.configuration_error(&upload_settings) {
        Some(error) => format!("upload {label} blocked ({error})"),
        None => format!("upload {label} configured; backend pending"),
    }
}

fn external_url_command(url: &str) -> (&'static str, Vec<String>) {
    #[cfg(target_os = "windows")]
    {
        (
            "rundll32.exe",
            vec!["url.dll,FileProtocolHandler".into(), url.into()],
        )
    }

    #[cfg(target_os = "macos")]
    {
        ("open", vec![url.into()])
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    {
        ("xdg-open", vec![url.into()])
    }

    #[cfg(all(not(target_os = "windows"), not(unix)))]
    {
        ("", Vec::new())
    }
}

fn open_external_url(url: &str) -> Result<(), String> {
    if !(url.starts_with("https://") || url.starts_with("http://")) {
        return Err("external URL must be absolute HTTP(S)".into());
    }

    let (program, args) = external_url_command(url);
    if program.is_empty() {
        return Err("opening browser URLs is unsupported on this platform".into());
    }

    let status = Command::new(program)
        .args(&args)
        .status()
        .map_err(|source| format!("failed to start browser command: {source}"))?;

    if status.success() {
        Ok(())
    } else {
        Err(format!("browser command exited with status {status}"))
    }
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
    valid_thumbnail_output(output_path)
}

fn valid_thumbnail_output(output_path: &Path) -> bool {
    oddsnap_platform::image_file_dimensions(output_path).is_ok()
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

fn next_default_capture_mode(mode: DefaultCaptureMode) -> DefaultCaptureMode {
    match mode {
        DefaultCaptureMode::Rectangle => DefaultCaptureMode::Fullscreen,
        DefaultCaptureMode::Fullscreen => DefaultCaptureMode::ActiveWindow,
        DefaultCaptureMode::ActiveWindow
        | DefaultCaptureMode::ColorPicker
        | DefaultCaptureMode::Ocr
        | DefaultCaptureMode::Scan
        | DefaultCaptureMode::Sticker
        | DefaultCaptureMode::Upscale
        | DefaultCaptureMode::Center
        | DefaultCaptureMode::Ruler => DefaultCaptureMode::Rectangle,
    }
}

fn next_capture_delay_seconds(delay: u32) -> u32 {
    match delay {
        0 => 1,
        1 => 3,
        3 => 5,
        5 => 10,
        _ => 0,
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

#[cfg(target_os = "linux")]
fn recording_audio_request_for_host(settings: &AppSettings) -> (bool, bool, Option<&'static str>) {
    if settings.record_microphone || settings.record_desktop_audio {
        (
            false,
            false,
            Some("audio capture pending on Linux; recording video only"),
        )
    } else {
        (false, false, None)
    }
}

#[cfg(target_os = "macos")]
fn recording_audio_request_for_host(settings: &AppSettings) -> (bool, bool, Option<&'static str>) {
    if settings.record_desktop_audio {
        (
            settings.record_microphone,
            false,
            Some("system audio capture pending on macOS; recording without system audio"),
        )
    } else {
        (settings.record_microphone, false, None)
    }
}

#[cfg(target_os = "windows")]
fn recording_audio_request_for_host(settings: &AppSettings) -> (bool, bool, Option<&'static str>) {
    if settings.record_microphone || settings.record_desktop_audio {
        (
            false,
            false,
            Some("audio capture pending on Windows; recording video only"),
        )
    } else {
        (false, false, None)
    }
}

#[cfg(all(
    not(target_os = "linux"),
    not(target_os = "macos"),
    not(target_os = "windows")
))]
fn recording_audio_request_for_host(_: &AppSettings) -> (bool, bool, Option<&'static str>) {
    (false, false, None)
}

#[derive(Clone, Copy)]
enum DefaultCaptureAction {
    Capture(CaptureMode),
    ColorPicker,
    Pending(PendingTool),
}

fn default_capture_action(default_mode: DefaultCaptureMode) -> DefaultCaptureAction {
    match default_mode {
        DefaultCaptureMode::ActiveWindow => {
            DefaultCaptureAction::Capture(CaptureMode::ActiveWindow)
        }
        DefaultCaptureMode::Fullscreen => DefaultCaptureAction::Capture(CaptureMode::FullScreen),
        DefaultCaptureMode::Rectangle => DefaultCaptureAction::Capture(CaptureMode::Rectangle),
        DefaultCaptureMode::ColorPicker => DefaultCaptureAction::ColorPicker,
        DefaultCaptureMode::Ocr
        | DefaultCaptureMode::Scan
        | DefaultCaptureMode::Sticker
        | DefaultCaptureMode::Upscale
        | DefaultCaptureMode::Center
        | DefaultCaptureMode::Ruler => DefaultCaptureAction::Pending(
            PendingTool::from_default_capture_mode(default_mode)
                .expect("advanced default capture mode has pending tool spec"),
        ),
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum RevealAction {
    RevealFile,
    #[cfg(all(unix, not(target_os = "macos")))]
    OpenContainingFolder,
}

impl RevealAction {
    fn past_tense(self) -> &'static str {
        match self {
            Self::RevealFile => "Revealed",
            #[cfg(all(unix, not(target_os = "macos")))]
            Self::OpenContainingFolder => "Opened containing folder for",
        }
    }
}

fn reveal_file_command(path: &Path) -> (&'static str, Vec<String>, RevealAction) {
    #[cfg(target_os = "windows")]
    {
        (
            "explorer.exe",
            vec![format!("/select,{}", path.display())],
            RevealAction::RevealFile,
        )
    }

    #[cfg(target_os = "macos")]
    {
        (
            "open",
            vec!["-R".into(), path.display().to_string()],
            RevealAction::RevealFile,
        )
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    {
        let target = path.parent().unwrap_or(path);
        (
            "xdg-open",
            vec![target.display().to_string()],
            RevealAction::OpenContainingFolder,
        )
    }
}

fn reveal_history_path(path: &Path) -> Result<&'static str, String> {
    if !path.exists() {
        return Err(format!("{} does not exist", path.display()));
    }

    let (program, args, action) = reveal_file_command(path);
    let status = Command::new(program)
        .args(args)
        .status()
        .map_err(|error| format!("failed to run {program}: {error}"))?;
    if status.success() {
        Ok(action.past_tense())
    } else {
        Err(format!("{program} exited with status {status}"))
    }
}

fn copy_text_to_host_clipboard(text: &str) -> Result<(), oddsnap_platform::PlatformError> {
    #[cfg(target_os = "windows")]
    {
        oddsnap_platform_windows::WindowsPlatform.copy_text_to_clipboard(text)
    }

    #[cfg(target_os = "macos")]
    {
        oddsnap_platform_macos::MacosPlatform.copy_text_to_clipboard(text)
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        oddsnap_platform_linux::LinuxPlatform.copy_text_to_clipboard(text)
    }
}

fn copy_image_to_host_clipboard(path: &Path) -> Result<(), oddsnap_platform::PlatformError> {
    #[cfg(target_os = "windows")]
    {
        oddsnap_platform_windows::WindowsPlatform.copy_image_to_clipboard(path)
    }

    #[cfg(target_os = "macos")]
    {
        oddsnap_platform_macos::MacosPlatform.copy_image_to_clipboard(path)
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        oddsnap_platform_linux::LinuxPlatform.copy_image_to_clipboard(path)
    }
}

fn pending_tool_hotkey_status(tool: PendingTool) -> String {
    pending_tool_trigger_status(&format!("{} hotkey", tool.label()), tool)
}

fn pending_tool_trigger_status(trigger: &str, tool: PendingTool) -> String {
    format!(
        "{trigger} received; Rust {} parity is pending.",
        tool.parity_item()
    )
}

fn pending_default_capture_status(trigger: &str, tool: PendingTool) -> String {
    format!(
        "{trigger} received; default capture mode '{}' needs Rust {} parity.",
        tool.label(),
        tool.parity_item()
    )
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
        assert_eq!(
            next_default_capture_mode(DefaultCaptureMode::Rectangle),
            DefaultCaptureMode::Fullscreen
        );
        assert_eq!(
            next_default_capture_mode(DefaultCaptureMode::Fullscreen),
            DefaultCaptureMode::ActiveWindow
        );
        assert_eq!(
            next_default_capture_mode(DefaultCaptureMode::ActiveWindow),
            DefaultCaptureMode::Rectangle
        );
        assert_eq!(
            next_default_capture_mode(DefaultCaptureMode::Ocr),
            DefaultCaptureMode::Rectangle
        );
        assert_eq!(next_capture_delay_seconds(0), 1);
        assert_eq!(next_capture_delay_seconds(1), 3);
        assert_eq!(next_capture_delay_seconds(3), 5);
        assert_eq!(next_capture_delay_seconds(5), 10);
        assert_eq!(next_capture_delay_seconds(10), 0);
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
    fn partial_foundation_statuses_do_not_claim_ready() {
        for status in [
            WINDOWS_TRAY_FOUNDATION_STATUS,
            MACOS_MENU_BAR_FOUNDATION_STATUS,
        ] {
            assert!(status.contains("foundation active"));
            assert!(status.contains("pending"));
            assert!(!status.to_ascii_lowercase().contains("ready"));
        }
    }

    #[test]
    fn default_capture_action_routes_imported_default_modes_without_false_fallbacks() {
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::ActiveWindow),
            DefaultCaptureAction::Capture(CaptureMode::ActiveWindow)
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Fullscreen),
            DefaultCaptureAction::Capture(CaptureMode::FullScreen)
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Rectangle),
            DefaultCaptureAction::Capture(CaptureMode::Rectangle)
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::ColorPicker),
            DefaultCaptureAction::ColorPicker
        ));

        for (mode, tool) in [
            (DefaultCaptureMode::Ocr, PendingTool::Ocr),
            (DefaultCaptureMode::Scan, PendingTool::Scan),
            (DefaultCaptureMode::Sticker, PendingTool::Sticker),
            (DefaultCaptureMode::Upscale, PendingTool::Upscale),
            (DefaultCaptureMode::Center, PendingTool::Center),
            (DefaultCaptureMode::Ruler, PendingTool::Ruler),
        ] {
            assert!(matches!(
                default_capture_action(mode),
                DefaultCaptureAction::Pending(pending_tool) if pending_tool == tool
            ));
        }
    }

    #[test]
    fn pending_default_capture_status_is_explicit() {
        assert_eq!(
            pending_default_capture_status("Capture hotkey", PendingTool::Ocr),
            "Capture hotkey received; default capture mode 'OCR' needs Rust OCR parity."
        );
    }

    #[test]
    fn pending_tool_registry_covers_imported_tool_surface() {
        assert_eq!(
            PendingTool::ALL
                .iter()
                .map(|tool| tool.hotkey_summary_label())
                .collect::<Vec<_>>(),
            vec![
                "OCR",
                "scan",
                "sticker",
                "upscale",
                "center",
                "ruler",
                "scroll-capture",
                "AI redirect"
            ]
        );
        assert_eq!(
            PendingTool::from_default_capture_mode(DefaultCaptureMode::Sticker),
            Some(PendingTool::Sticker)
        );
        assert_eq!(
            PendingTool::from_default_capture_mode(DefaultCaptureMode::Fullscreen),
            None
        );
        assert_eq!(
            pending_tool_hotkey_status(PendingTool::Sticker),
            "Sticker hotkey received; Rust sticker/background removal parity is pending."
        );
    }

    #[test]
    fn hotkey_status_lists_dedicated_capture_hotkeys() {
        assert_eq!(
            hotkey_status_summary(ImportedHotkeyAccelerators {
                capture: "Alt+Shift+S",
                recording: Some("Alt+Shift+R"),
                fullscreen: Some("Alt+Shift+F"),
                active_window: Some("Alt+Shift+A"),
                picker: Some("Alt+Shift+C"),
                ocr: Some("Alt+Shift+O"),
                scan: Some("Alt+Shift+N"),
                sticker: Some("Alt+Shift+T"),
                upscale: Some("Alt+Shift+U"),
                center: Some("Alt+Shift+E"),
                ruler: Some("Alt+Shift+L"),
                scroll_capture: Some("Alt+Shift+P"),
                ai_redirect: Some("Alt+Shift+I"),
            }),
            "Hotkeys: Alt+Shift+S capture, Alt+Shift+R recording, Alt+Shift+F full-screen, Alt+Shift+A active-window, Alt+Shift+C color-picker, Alt+Shift+O OCR, Alt+Shift+N scan, Alt+Shift+T sticker, Alt+Shift+U upscale, Alt+Shift+E center, Alt+Shift+L ruler, Alt+Shift+P scroll-capture, Alt+Shift+I AI redirect."
        );
    }

    #[test]
    fn duplicate_hotkeys_are_rejected_before_registration() {
        let error = validate_unique_hotkey_bindings(ImportedHotkeyAccelerators {
            capture: "Alt+Shift+S",
            recording: Some(" Shift + S + Alt "),
            fullscreen: Some("Alt+Shift+F"),
            active_window: None,
            picker: None,
            ocr: None,
            scan: None,
            sticker: None,
            upscale: None,
            center: None,
            ruler: None,
            scroll_capture: None,
            ai_redirect: None,
        })
        .expect_err("duplicate rejected");

        assert_eq!(
            error,
            "hotkey  Shift + S + Alt  is assigned to both capture and recording"
        );
    }

    #[test]
    fn cross_platform_hotkey_parser_accepts_legacy_accelerator_shape() {
        let hotkey = parse_cross_platform_hotkey("Ctrl+Shift+S").expect("parse legacy accelerator");

        assert!(!hotkey.mods.is_empty());
    }

    #[test]
    fn cross_platform_hotkey_parser_rejects_modifierless_hotkeys() {
        let error = parse_cross_platform_hotkey("S").expect_err("modifierless hotkey rejected");

        assert!(error.contains("must include a modifier"));
    }

    #[test]
    fn cross_platform_hotkey_registrations_include_imported_tool_hotkeys() {
        let registrations = cross_platform_hotkey_registrations(ImportedHotkeyAccelerators {
            capture: "Alt+Shift+S",
            recording: Some("Alt+Shift+R"),
            fullscreen: Some("Alt+Shift+F"),
            active_window: Some("Alt+Shift+A"),
            picker: Some("Alt+Shift+C"),
            ocr: Some("Alt+Shift+O"),
            scan: Some("Alt+Shift+N"),
            sticker: Some("Alt+Shift+T"),
            upscale: Some("Alt+Shift+U"),
            center: Some("Alt+Shift+E"),
            ruler: Some("Alt+Shift+L"),
            scroll_capture: Some("Alt+Shift+P"),
            ai_redirect: Some("Alt+Shift+I"),
        });

        assert_eq!(
            registrations,
            vec![
                ("Alt+Shift+S", CrossPlatformHotkeyEvent::Capture),
                ("Alt+Shift+R", CrossPlatformHotkeyEvent::Recording),
                ("Alt+Shift+F", CrossPlatformHotkeyEvent::FullScreenCapture),
                ("Alt+Shift+A", CrossPlatformHotkeyEvent::ActiveWindowCapture),
                ("Alt+Shift+C", CrossPlatformHotkeyEvent::ColorPicker),
                ("Alt+Shift+O", CrossPlatformHotkeyEvent::Ocr),
                ("Alt+Shift+N", CrossPlatformHotkeyEvent::Scan),
                ("Alt+Shift+T", CrossPlatformHotkeyEvent::Sticker),
                ("Alt+Shift+U", CrossPlatformHotkeyEvent::Upscale),
                ("Alt+Shift+E", CrossPlatformHotkeyEvent::Center),
                ("Alt+Shift+L", CrossPlatformHotkeyEvent::Ruler),
                ("Alt+Shift+P", CrossPlatformHotkeyEvent::ScrollCapture),
                ("Alt+Shift+I", CrossPlatformHotkeyEvent::AiRedirect),
            ]
        );
    }

    #[test]
    fn linux_hotkey_session_gate_requires_x11_display() {
        assert_eq!(
            linux_hotkey_session_error(Some("wayland"), Some(":1"), Some("wayland-0")),
            Some(
                "Linux Wayland global hotkeys need portal or compositor support; OddSnap Rust currently supports global hotkeys only on X11."
            )
        );
        assert_eq!(
            linux_hotkey_session_error(None, None, Some("wayland-0")),
            Some(
                "Linux Wayland global hotkeys need portal or compositor support; OddSnap Rust currently supports global hotkeys only on X11."
            )
        );
        assert_eq!(
            linux_hotkey_session_error(Some("x11"), Some(":0"), None),
            None
        );
        assert_eq!(
            linux_hotkey_session_error(None, None, None),
            Some(
                "Linux X11 global hotkeys need DISPLAY; Wayland portal hotkeys are not implemented yet."
            )
        );
    }

    #[test]
    fn builds_history_reveal_command_for_current_platform() {
        #[cfg(target_os = "windows")]
        let path = PathBuf::from(r"C:\captures\OddSnap.png");
        #[cfg(not(target_os = "windows"))]
        let path = PathBuf::from("/tmp/captures/OddSnap.png");

        let (program, args, action) = reveal_file_command(&path);

        #[cfg(target_os = "windows")]
        {
            assert_eq!(program, "explorer.exe");
            assert_eq!(args, vec![r"/select,C:\captures\OddSnap.png"]);
            assert_eq!(action, RevealAction::RevealFile);
        }

        #[cfg(target_os = "macos")]
        {
            assert_eq!(program, "open");
            assert_eq!(args, vec!["-R", "/tmp/captures/OddSnap.png"]);
            assert_eq!(action, RevealAction::RevealFile);
        }

        #[cfg(all(unix, not(target_os = "macos")))]
        {
            assert_eq!(program, "xdg-open");
            assert_eq!(args, vec!["/tmp/captures"]);
            assert_eq!(action, RevealAction::OpenContainingFolder);
        }
    }

    #[test]
    fn recording_target_labels_are_stable() {
        assert_eq!(RecordingTarget::FullScreen.label(), "desktop");
        assert_eq!(RecordingTarget::ActiveWindow.label(), "active window");
    }

    #[test]
    fn recording_audio_request_matches_host_support() {
        let settings = AppSettings {
            record_microphone: true,
            record_desktop_audio: true,
            ..AppSettings::default()
        };
        let (microphone, desktop_audio, note) = recording_audio_request_for_host(&settings);

        #[cfg(target_os = "linux")]
        {
            assert!(!microphone);
            assert!(!desktop_audio);
            assert_eq!(
                note,
                Some("audio capture pending on Linux; recording video only")
            );
        }

        #[cfg(target_os = "macos")]
        {
            assert!(microphone);
            assert!(!desktop_audio);
            assert_eq!(
                note,
                Some("system audio capture pending on macOS; recording without system audio")
            );
        }

        #[cfg(target_os = "windows")]
        {
            assert!(!microphone);
            assert!(!desktop_audio);
            assert_eq!(
                note,
                Some("audio capture pending on Windows; recording video only")
            );
        }

        #[cfg(all(
            not(target_os = "linux"),
            not(target_os = "macos"),
            not(target_os = "windows")
        ))]
        {
            assert!(!microphone);
            assert!(!desktop_audio);
            assert_eq!(note, None);
        }
    }

    #[test]
    fn history_upload_summary_reports_link_and_failure_states() {
        let mut entry = CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: HistoryKind::Image,
            path: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            upload_url: Some("https://example.test/capture.png".into()),
            upload_provider: Some("Imgur".into()),
            upload_error: None,
        };

        assert_eq!(
            history_upload_summary(&entry),
            "Imgur: https://example.test/capture.png"
        );

        entry.upload_error = Some("rate limited".into());
        assert_eq!(
            history_upload_summary(&entry),
            "Upload failed: rate limited"
        );

        entry.upload_error = Some("pending: Rust upload backend for Catbox is pending.".into());
        assert_eq!(
            history_upload_summary(&entry),
            "Upload pending: Rust upload backend for Catbox is pending."
        );
        assert_eq!(history_kind_label(HistoryKind::Sticker), "Sticker");
    }

    #[test]
    fn upload_settings_summary_reports_configured_and_blocked_states() {
        let blocked = AppSettings {
            image_upload_destination: "Imgur".into(),
            auto_upload_screenshots: true,
            ..AppSettings::default()
        };
        assert_eq!(
            upload_settings_summary(&blocked),
            "upload Imgur blocked (Imgur client ID not configured. Add or update it in Settings -> Uploads.)"
        );

        let configured = AppSettings {
            image_upload_destination: "Catbox".into(),
            auto_upload_screenshots: true,
            ..AppSettings::default()
        };
        assert_eq!(
            upload_settings_summary(&configured),
            "upload Catbox configured; backend pending"
        );
    }

    #[test]
    fn newest_history_image_path_uses_existing_image_entries_only() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-ai-redirect-path-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp test root");
        let image = root.join("capture.png");
        std::fs::write(&image, b"image").expect("write image");
        let missing = root.join("missing.png");

        let history = vec![
            CaptureHistoryEntry {
                mode: CaptureMode::FullScreen,
                kind: HistoryKind::Video,
                path: root.join("capture.mp4").display().to_string(),
                preview_path: None,
                width: 10,
                height: 10,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
            CaptureHistoryEntry {
                mode: CaptureMode::FullScreen,
                kind: HistoryKind::Image,
                path: missing.display().to_string(),
                preview_path: None,
                width: 10,
                height: 10,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
            CaptureHistoryEntry {
                mode: CaptureMode::FullScreen,
                kind: HistoryKind::Sticker,
                path: image.display().to_string(),
                preview_path: None,
                width: 10,
                height: 10,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
        ];

        assert_eq!(newest_history_image_path(&history), Some(image));
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn external_url_command_uses_current_platform_browser_opener() {
        let (program, args) = external_url_command("https://chatgpt.com/");

        #[cfg(target_os = "windows")]
        {
            assert_eq!(program, "rundll32.exe");
            assert_eq!(
                args,
                vec!["url.dll,FileProtocolHandler", "https://chatgpt.com/"]
            );
        }

        #[cfg(target_os = "macos")]
        {
            assert_eq!(program, "open");
            assert_eq!(args, vec!["https://chatgpt.com/"]);
        }

        #[cfg(all(unix, not(target_os = "macos")))]
        {
            assert_eq!(program, "xdg-open");
            assert_eq!(args, vec!["https://chatgpt.com/"]);
        }
    }

    #[test]
    fn thumbnail_output_must_be_decodable_image() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-thumbnail-output-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp test root");

        let invalid = root.join("invalid.jpg");
        std::fs::write(&invalid, b"not a thumbnail").expect("write invalid thumbnail");
        assert!(!valid_thumbnail_output(&invalid));

        let valid = root.join("valid.bmp");
        std::fs::write(
            &valid,
            [
                0x42, 0x4d, 0x3a, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 0x28, 0, 0, 0, 1, 0, 0, 0, 1,
                0, 0, 0, 1, 0, 24, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0x13, 0x0b, 0, 0, 0x13, 0x0b, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 0,
            ],
        )
        .expect("write valid thumbnail");
        assert!(valid_thumbnail_output(&valid));

        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn color_history_limits_recent_entries_for_display() {
        let index = HistoryIndex {
            entries: Vec::new(),
            colors: (0..14)
                .map(|value| ColorHistoryEntry {
                    hex: format!("{value:06X}"),
                    captured_at_unix_ms: value,
                })
                .collect(),
        };

        let colors = history_entries_to_color_history(index);

        assert_eq!(colors.len(), 12);
        assert_eq!(colors[0].hex, "000000");
        assert_eq!(colors[11].hex, "00000B");
    }
}
