#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

use gpui::prelude::FluentBuilder;
use gpui::{
    div, img, px, rgb, size, App, AppContext, Bounds, Context, InteractiveElement, IntoElement,
    KeyDownEvent, ObjectFit, ParentElement, Render, SharedString, StatefulInteractiveElement,
    Styled, StyledImage, TitlebarOptions, Window, WindowBackgroundAppearance, WindowBounds,
    WindowDecorations, WindowOptions,
};
use gpui_platform::application;
use oddsnap_core::{
    apply_image_search_ocr_error, apply_image_search_ocr_success, build_available_capture_path,
    build_video_thumbnail_args, build_video_thumbnail_fallback_args, decode_barcode_image,
    default_history_path, default_image_search_index_path, default_settings_path,
    discover_ffmpeg_tools, format_file_name_template, history_entry_can_be_image_indexed,
    humanize_barcode_format, image_search_record_matches_history_entry,
    image_search_record_needs_ocr, pending_image_search_record_from_history_entry,
    retain_indexed_image_paths, upsert_image_search_record, AiChatProvider, AppSettings,
    CapabilityState, CaptureImageFormat, CodeHistoryEntry, ColorHistoryEntry, DefaultCaptureMode,
    FfmpegThumbnailRequest, HistoryEntry, HistoryIndex, HistoryKind, HistoryStore,
    ImageSearchIndex, ImageSearchIndexRecord, ImageSearchIndexStore, ImageSearchOcrState,
    ImageSearchSources, OcrHistoryEntry, PlatformCapability, RecordingFormat, RecordingQuality,
    ScrollingCaptureMode, SettingsStore, StickerProvider, StickerSettings, TranslationModel,
    UploadDestination, UploadPreflight, UploadSettings, UpscaleProvider, UpscaleSettings,
    SUPPORTED_TRANSLATION_LANGUAGES,
};
use oddsnap_platform::{
    default_capture_directory, image_file_dimensions, persist_capture_to_path_as,
    virtual_screen_region, CaptureRegion, CaptureRequest, CaptureResult,
    CenterSelectionAspectRatio, ClipboardImageService, ClipboardTextService, ColorPickerService,
    OcrTextRequest, OcrTextService, OverlayWindowRequest, PlatformAdapter, RegionSelectionMode,
    RegionSelectionService, ScreenCaptureService, VideoRecordingHandle, VideoRecordingRequest,
    VideoRecordingService, WindowPickerService,
};

mod actions;
mod hotkeys;
mod image_search;
mod media_history;
mod ocr_translation;
mod processed_preview;
mod settings_text;
mod sticker_upscale;
mod ui;

#[cfg(any(test, not(target_os = "windows")))]
use actions::CrossPlatformHotkeyEvent;
use actions::{
    default_capture_action, pending_tool_hotkey_status, pending_tool_trigger_status, CaptureMode,
    DefaultCaptureAction, PendingTool, RecordingTarget, SettingsAction,
};
#[cfg(not(target_os = "windows"))]
use hotkeys::CrossPlatformHotkeyListener;
#[cfg(test)]
use hotkeys::{
    cross_platform_hotkey_registrations, hotkey_status_summary, linux_hotkey_session_error,
    parse_cross_platform_hotkey, validate_unique_hotkey_bindings,
};
use hotkeys::{start_capture_hotkey_listener, ImportedHotkeyAccelerators};
use image_search::{ImageSearchOcrHydrationSummary, ImageSearchReindexQueueState};
use media_history::{filtered_capture_history, HistoryKindFilter, HistoryUploadFilter};
use processed_preview::ProcessedResultPreviewWindow;

#[cfg(any(target_os = "windows", test))]
const WINDOWS_TRAY_FOUNDATION_STATUS: &str =
    "Tray: Windows icon and menu foundation active; OCR wired, scroll capture still pending.";
#[cfg(any(target_os = "macos", test))]
const MACOS_MENU_BAR_FOUNDATION_STATUS: &str =
    "Menu bar: macOS status item foundation active; OCR wired, scroll capture still pending.";
#[cfg(target_os = "linux")]
const LINUX_TRAY_FOUNDATION_STATUS: &str =
    "Tray: Linux appindicator/status icon foundation active; OCR wired, scroll capture still pending.";

fn main() {
    application().run(|cx: &mut App| {
        let bounds = Bounds::centered(
            None,
            size(px(ui::skin::WINDOW_WIDTH), px(ui::skin::WINDOW_HEIGHT)),
            cx,
        );
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
                    window_min_size: Some(size(
                        px(ui::skin::MIN_WINDOW_WIDTH),
                        px(ui::skin::MIN_WINDOW_HEIGHT),
                    )),
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
    image_search_index_store: ImageSearchIndexStore,
    image_search_index_path: String,
    image_search_index_status: String,
    media_status: String,
    hotkey_status: String,
    tray_status: String,
    recording_status: String,
    active_recording: Option<ActiveRecording>,
    settings_text_input: Option<settings_text::SettingsTextInputState>,
    capture_history: Vec<CaptureHistoryEntry>,
    color_history: Vec<ColorHistoryEntry>,
    ocr_history: Vec<OcrHistoryEntry>,
    code_history: Vec<CodeHistoryEntry>,
    latest_ocr_result: Option<String>,
    latest_scan_result: Option<CodeHistoryEntry>,
    image_search: image_search::ImageSearchUiState,
    history_kind_filter: HistoryKindFilter,
    history_upload_filter: HistoryUploadFilter,
    image_search_reindex_queue: ImageSearchReindexQueueState,
    last_auto_image_search_ocr_unix_ms: u64,
    focus_handle: gpui::FocusHandle,
    #[cfg(target_os = "windows")]
    _hotkey_listener: Option<oddsnap_platform_windows::WindowsHotkeyListener>,
    #[cfg(not(target_os = "windows"))]
    _hotkey_listener: Option<CrossPlatformHotkeyListener>,
    #[cfg(target_os = "windows")]
    tray_icon: Option<oddsnap_platform_windows::WindowsTrayIcon>,
    #[cfg(target_os = "macos")]
    tray_icon: Option<oddsnap_platform_macos::MacosTrayIcon>,
    #[cfg(target_os = "linux")]
    tray_icon: Option<oddsnap_platform_linux::LinuxTrayIcon>,
}

#[derive(Clone)]
struct CaptureHistoryEntry {
    mode: CaptureMode,
    kind: HistoryKind,
    path: String,
    file_name: String,
    preview_path: Option<PathBuf>,
    width: u32,
    height: u32,
    captured_at_unix_ms: u64,
    image_search_ocr_text: String,
    image_search_record: Option<ImageSearchIndexRecord>,
    upload_url: Option<String>,
    upload_provider: Option<String>,
    upload_error: Option<String>,
}

impl image_search::ImageSearchItem for CaptureHistoryEntry {
    fn file_path(&self) -> &str {
        &self.path
    }

    fn file_name(&self) -> &str {
        &self.file_name
    }

    fn captured_at_unix_ms(&self) -> u64 {
        self.captured_at_unix_ms
    }

    fn image_search_ocr_text(&self) -> &str {
        self.image_search_record
            .as_ref()
            .map_or(self.image_search_ocr_text.as_str(), |record| {
                record.ocr_text.as_str()
            })
    }

    fn image_search_record(&self) -> Option<&ImageSearchIndexRecord> {
        self.image_search_record.as_ref()
    }
}

struct CaptureRunResult {
    capture: oddsnap_platform::CaptureResult,
    copy_error: Option<String>,
    original_preview_path: Option<PathBuf>,
}

#[derive(Clone, Copy)]
pub(crate) enum ProcessedCaptureTool {
    Sticker,
    Upscale,
}

impl ProcessedCaptureTool {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::Sticker => "Sticker",
            Self::Upscale => "Upscale",
        }
    }

    fn slug(self) -> &'static str {
        match self {
            Self::Sticker => "sticker",
            Self::Upscale => "upscale",
        }
    }

    fn history_kind(self) -> HistoryKind {
        match self {
            Self::Sticker => HistoryKind::Sticker,
            Self::Upscale => HistoryKind::Image,
        }
    }
}

#[derive(Default)]
struct UploadMetadata {
    url: Option<String>,
    provider: Option<String>,
    error: Option<String>,
}

struct OcrResultWindow {
    text: String,
    status: String,
}

impl OcrResultWindow {
    fn new(text: String) -> Self {
        Self {
            text,
            status: "OCR result ready.".into(),
        }
    }
}

impl Render for OcrResultWindow {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .size_full()
            .bg(ui::skin::color(ui::skin::APP_BG))
            .text_color(ui::skin::color(ui::skin::BRIGHT_TEXT))
            .p(px(16.0))
            .flex()
            .flex_col()
            .gap(px(10.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(15.0)).child("OCR result"))
                    .child(
                        ui::action_button_style(
                            div().id("ocr-result-window-copy"),
                            ui::ButtonVariant::History,
                        )
                        .child("Copy")
                        .on_click(cx.listener(
                            |this: &mut Self, _, _, cx| {
                                cx.stop_propagation();
                                this.status = match copy_text_to_host_clipboard(&this.text) {
                                    Ok(()) => "OCR text copied.".into(),
                                    Err(error) => format!("Copy OCR text failed: {error}"),
                                };
                                cx.notify();
                            },
                        )),
                    ),
            )
            .child(
                div()
                    .id("ocr-result-window-text")
                    .flex_1()
                    .min_h(px(0.0))
                    .overflow_y_scroll()
                    .rounded(px(6.0))
                    .border_1()
                    .border_color(rgb(0x2b3039))
                    .bg(rgb(0x151922))
                    .p(px(12.0))
                    .text_size(px(13.0))
                    .text_color(ui::skin::color(ui::skin::BODY_TEXT))
                    .child(SharedString::from(self.text.clone())),
            )
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.status.clone())),
            )
    }
}

struct ActiveRecording {
    handle: Box<dyn VideoRecordingHandle>,
    width: u32,
    height: u32,
    target: RecordingTarget,
}

struct RecordingStart {
    handle: Box<dyn VideoRecordingHandle>,
    width: u32,
    height: u32,
    target: RecordingTarget,
    note: Option<&'static str>,
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
        let image_search_index_store =
            ImageSearchIndexStore::new(default_image_search_index_path());
        let image_search_index_path = image_search_index_store.path().display().to_string();
        let (image_search_index, image_search_index_status) =
            sync_image_search_index_records(&image_search_index_store, &history_index, &settings);
        let capture_history =
            history_entries_to_capture_history(history_index.clone(), &image_search_index);
        let color_history = history_entries_to_color_history(history_index.clone());
        let ocr_history = history_entries_to_ocr_history(history_index.clone());
        let code_history = history_entries_to_code_history(history_index);
        let latest_ocr_result = ocr_history.first().map(|entry| entry.text.clone());
        let latest_scan_result = code_history.first().cloned();
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
            image_search_index_store,
            image_search_index_path,
            image_search_index_status,
            media_status,
            hotkey_status,
            tray_status,
            recording_status: "No recording running.".into(),
            active_recording: None,
            settings_text_input: None,
            capture_history,
            color_history,
            ocr_history,
            code_history,
            latest_ocr_result,
            latest_scan_result,
            image_search: image_search::ImageSearchUiState::new(),
            history_kind_filter: HistoryKindFilter::All,
            history_upload_filter: HistoryUploadFilter::All,
            image_search_reindex_queue: ImageSearchReindexQueueState::default(),
            last_auto_image_search_ocr_unix_ms: 0,
            focus_handle: cx.focus_handle(),
            #[cfg(target_os = "windows")]
            _hotkey_listener: hotkey_listener,
            #[cfg(not(target_os = "windows"))]
            _hotkey_listener: hotkey_listener,
            #[cfg(target_os = "windows")]
            tray_icon,
            #[cfg(target_os = "macos")]
            tray_icon,
            #[cfg(target_os = "linux")]
            tray_icon,
        };

        app.start_hotkey_event_pump(hotkey_events, cx);
        app.start_tray_event_pump(tray_events, cx);
        app.start_image_search_ocr_background_pump(cx);
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
            .bg(ui::skin::color(ui::skin::APP_BG))
            .text_color(ui::skin::color(ui::skin::APP_TEXT))
            .font_family("Segoe UI")
            .track_focus(&self.focus_handle)
            .on_key_down(cx.listener(Self::on_key_down))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .h(px(54.0))
                    .px(px(18.0))
                    .border_b_1()
                    .border_color(ui::skin::color(ui::skin::HEADER_BORDER))
                    .child(
                        div()
                            .flex()
                            .flex_col()
                            .child(div().text_size(px(17.0)).child("OddSnap Rust"))
                            .child(
                                div()
                                    .text_size(px(11.0))
                                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                                    .child("GPUI rewrite foundation"),
                            ),
                    )
                    .child(
                        div()
                            .rounded(px(8.0))
                            .border_1()
                            .border_color(ui::skin::color(0x343842))
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
        let mut body = ui::panel_style(div())
            .flex()
            .flex_col()
            .gap(px(9.0))
            .flex_1()
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
                            .child(self.ocr_button(cx))
                            .child(self.scan_button(cx))
                            .child(self.recording_button(cx))
                            .child(self.recording_target_button(cx))
                            .child(self.recording_region_button(cx)),
                    ),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::BODY_TEXT))
                    .child(SharedString::from(self.native_ui_goal.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child("Windows: WinUI 3 aligned; macOS: Liquid Glass aligned; Linux: freedesktop adaptive."),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(format!("Settings: {}", self.settings_path))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(format!("History: {}", self.history_path))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(format!(
                        "Image index: {}",
                        self.image_search_index_path
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.image_search_index_status.clone())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(format!(
                        "Output: {}",
                        self.capture_output_directory().display()
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.capture_preferences_summary())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.advanced_settings_summary())),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(
                        ocr_translation::translation_runtime_status_summary(&self.settings),
                    )),
            )
            .child(
                div()
                    .flex()
                    .flex_wrap()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "translation-model-button",
                        format!(
                            "Translate {}",
                            TranslationModel::from_legacy_value(self.settings.translation_model)
                                .label()
                        ),
                        SettingsAction::TranslationModel,
                    ))
                    .child(self.settings_button(
                        cx,
                        "translation-source-button",
                        format!(
                            "From {}",
                            oddsnap_core::translation_language_name(
                                &oddsnap_core::resolve_translation_source_language(Some(
                                    &self.settings.ocr_default_translate_from
                                ))
                            )
                        ),
                        SettingsAction::TranslationSourceLanguage,
                    ))
                    .child(self.settings_button(
                        cx,
                        "translation-target-button",
                        format!(
                            "To {}",
                            oddsnap_core::translation_language_name(
                                &oddsnap_core::resolve_translation_target_language(
                                    Some(&self.settings.ocr_default_translate_to),
                                    Some(&self.settings.interface_language),
                                    None,
                                )
                            )
                        ),
                        SettingsAction::TranslationTargetLanguage,
                    )),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(sticker_settings_summary_text(
                        &self.settings,
                    ))),
            )
            .child(
                div()
                    .flex()
                    .flex_wrap()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "sticker-provider-button",
                        "Sticker Provider".into(),
                        SettingsAction::StickerProvider,
                    ))
                    .child(self.settings_button(
                        cx,
                        "removebg-key-button",
                        "Remove.bg Key".into(),
                        SettingsAction::EditRemoveBgApiKey,
                    ))
                    .child(self.settings_button(
                        cx,
                        "photoroom-key-button",
                        "Photoroom Key".into(),
                        SettingsAction::EditPhotoroomApiKey,
                    ))
                    .child(self.settings_button(
                        cx,
                        "sticker-engine-button",
                        "Sticker Model".into(),
                        SettingsAction::StickerLocalEngine,
                    ))
                    .child(self.settings_button(
                        cx,
                        "sticker-execution-button",
                        "Sticker CPU/GPU".into(),
                        SettingsAction::StickerExecutionProvider,
                    ))
                    .child(self.settings_button(
                        cx,
                        "sticker-stroke-button",
                        "Stroke".into(),
                        SettingsAction::ToggleStickerStroke,
                    ))
                    .child(self.settings_button(
                        cx,
                        "sticker-shadow-button",
                        "Shadow".into(),
                        SettingsAction::ToggleStickerShadow,
                    )),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(upscale_settings_summary_text(
                        &self.settings,
                    ))),
            )
            .child(
                div()
                    .flex()
                    .flex_wrap()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "upscale-provider-button",
                        "Upscale Provider".into(),
                        SettingsAction::UpscaleProvider,
                    ))
                    .child(self.settings_button(
                        cx,
                        "deepai-key-button",
                        "DeepAI Key".into(),
                        SettingsAction::EditDeepAiApiKey,
                    ))
                    .child(self.settings_button(
                        cx,
                        "upscale-engine-button",
                        "Upscale Model".into(),
                        SettingsAction::UpscaleLocalEngine,
                    ))
                    .child(self.settings_button(
                        cx,
                        "upscale-execution-button",
                        "Upscale CPU/GPU".into(),
                        SettingsAction::UpscaleExecutionProvider,
                    ))
                    .child(self.settings_button(
                        cx,
                        "upscale-scale-button",
                        "Scale".into(),
                        SettingsAction::UpscaleScale,
                    ))
                    .child(self.settings_button(
                        cx,
                        "upscale-preview-button",
                        "Preview".into(),
                        SettingsAction::ToggleUpscalePreview,
                    )),
            )
            .when_some(self.settings_text_input.as_ref(), |body, input| {
                body.child(settings_text_input_panel(input))
            })
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(
                        sticker_upscale::sticker_upscale_runtime_status_summary(&self.settings),
                    )),
            )
            .child(
                div()
                    .flex()
                    .flex_wrap()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "argos-translation-runtime-button",
                        if ocr_translation::argos_runtime_is_installed() {
                            "Remove Argos".into()
                        } else {
                            "Install Argos".into()
                        },
                        if ocr_translation::argos_runtime_is_installed() {
                            SettingsAction::RemoveArgosTranslationRuntime
                        } else {
                            SettingsAction::InstallArgosTranslationRuntime
                        },
                    ))
                    .child(self.settings_button(
                        cx,
                        "local-translation-runtime-button",
                        if ocr_translation::open_source_local_runtime_is_installed() {
                            "Remove Local".into()
                        } else {
                            "Install Local".into()
                        },
                        if ocr_translation::open_source_local_runtime_is_installed() {
                            SettingsAction::RemoveLocalTranslationRuntime
                        } else {
                            SettingsAction::InstallLocalTranslationRuntime
                        },
                    )),
            )
            .child(
                div()
                    .flex()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "sticker-runtime-button",
                        "Install Sticker Runtime".into(),
                        SettingsAction::InstallStickerRuntime,
                    ))
                    .child(self.settings_button(
                        cx,
                        "remove-sticker-runtime-button",
                        "Remove Sticker Runtime".into(),
                        SettingsAction::RemoveStickerRuntime,
                    ))
                    .child(self.settings_button(
                        cx,
                        "upscale-runtime-button",
                        "Install Upscale Runtime".into(),
                        SettingsAction::InstallUpscaleRuntime,
                    ))
                    .child(self.settings_button(
                        cx,
                        "remove-upscale-runtime-button",
                        "Remove Upscale Runtime".into(),
                        SettingsAction::RemoveUpscaleRuntime,
                    )),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(format!(
                        "Image format: {} · JPEG quality {}",
                        self.settings.capture_image_format.label(),
                        self.settings.jpeg_quality
                    ))),
            )
            .child(
                div()
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
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
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
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
                        "capture-sounds-button",
                        format!(
                            "Sounds {}",
                            if self.settings.mute_sounds {
                                "muted"
                            } else {
                                "on"
                            }
                        ),
                        SettingsAction::ToggleMuteSounds,
                    ))
                    .child(self.settings_button(
                        cx,
                        "toast-duration-button",
                        format!("Toast {:.1}s", self.settings.toast_duration_seconds),
                        SettingsAction::ToastDuration,
                    ))
                    .child(self.settings_button(
                        cx,
                        "toast-fade-button",
                        format!("Fade {}", on_off(self.settings.toast_fade_out_enabled)),
                        SettingsAction::ToggleToastFade,
                    ))
                    .child(self.settings_button(
                        cx,
                        "window-detection-button",
                        format!("Detect windows {}", on_off(self.settings.detect_windows)),
                        SettingsAction::ToggleWindowDetection,
                    ))
                    .child(self.settings_button(
                        cx,
                        "scrolling-capture-mode-button",
                        format!(
                            "Scroll {}",
                            ScrollingCaptureMode::from_setting(
                                &self.settings.scrolling_capture_mode
                            )
                            .label()
                        ),
                        SettingsAction::ScrollingCaptureMode,
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
                    .child(SharedString::from(lifecycle_settings_summary(
                        &self.settings,
                    ))),
            )
            .child(
                div()
                    .flex()
                    .gap(px(8.0))
                    .child(self.settings_button(
                        cx,
                        "start-with-system-button",
                        format!("Startup {}", on_off(self.settings.start_with_windows)),
                        SettingsAction::ToggleStartWithSystem,
                    ))
                    .child(self.settings_button(
                        cx,
                        "auto-update-check-button",
                        format!("Updates {}", on_off(self.settings.auto_check_for_updates)),
                        SettingsAction::ToggleAutoCheckForUpdates,
                    )),
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
                ui::surface_style(div())
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::BRIGHT_TEXT))
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
                        .child(
                            div()
                                .flex()
                                .gap(px(6.0))
                                .child(self.copy_color_hex_button(cx, entry.hex.clone()))
                                .child(self.remove_color_history_button(
                                    cx,
                                    entry.hex.clone(),
                                    entry.captured_at_unix_ms,
                                )),
                        ),
                );
            }
        }

        body = body.child(div().text_size(px(13.0)).child("Recent text captures"));
        if self.ocr_history.is_empty() {
            body = body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0x8b93a3))
                    .child("No text captures yet."),
            );
        } else {
            for entry in self.ocr_history.iter().take(3) {
                body = body.child(
                    div()
                        .flex()
                        .flex_col()
                        .gap(px(6.0))
                        .rounded(px(6.0))
                        .bg(rgb(0x1d2027))
                        .px(px(10.0))
                        .py(px(8.0))
                        .child(
                            div()
                                .line_clamp(2)
                                .text_size(px(12.0))
                                .child(SharedString::from(entry.text.clone())),
                        )
                        .child(
                            div()
                                .flex()
                                .gap(px(8.0))
                                .child(self.copy_ocr_text_button(cx, entry.text.clone()))
                                .child(self.open_ocr_result_window_button(cx, entry.text.clone()))
                                .child(self.translate_ocr_text_button(cx, entry.text.clone()))
                                .child(self.remove_ocr_history_button(
                                    cx,
                                    entry.text.clone(),
                                    entry.captured_at_unix_ms,
                                )),
                        ),
                );
            }
        }

        if let Some(text) = self.latest_ocr_result.as_ref() {
            body = body.child(self.ocr_result_panel(cx, text));
        }

        body = body.child(div().text_size(px(13.0)).child("Recent QR/barcode scans"));
        if self.code_history.is_empty() {
            body = body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0x8b93a3))
                    .child("No QR/barcode scans yet."),
            );
        } else {
            for entry in self.code_history.iter().take(3) {
                body = body.child(
                    div()
                        .flex()
                        .flex_col()
                        .gap(px(6.0))
                        .rounded(px(6.0))
                        .bg(rgb(0x1d2027))
                        .px(px(10.0))
                        .py(px(8.0))
                        .child(
                            div()
                                .text_size(px(11.0))
                                .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                                .child(SharedString::from(humanize_barcode_format(&entry.format))),
                        )
                        .child(
                            div()
                                .line_clamp(2)
                                .text_size(px(12.0))
                                .child(SharedString::from(entry.text.clone())),
                        )
                        .child(
                            div()
                                .flex()
                                .gap(px(8.0))
                                .child(self.copy_code_text_button(cx, entry.text.clone()))
                                .child(self.remove_code_history_button(
                                    cx,
                                    entry.text.clone(),
                                    entry.format.clone(),
                                    entry.captured_at_unix_ms,
                                )),
                        ),
                );
            }
        }

        if let Some(entry) = self.latest_scan_result.as_ref() {
            body = body.child(self.scan_result_panel(cx, entry));
        }

        body = body.child(div().text_size(px(13.0)).child("Recent captures"));
        body = body.child(self.history_filter_bar(cx));
        if self.settings.show_image_search_bar {
            body = body.child(self.image_search_bar(cx));
        } else {
            body = body.child(self.settings_button(
                cx,
                "image-search-show-button",
                "Show search".into(),
                SettingsAction::ToggleImageSearchBar,
            ));
        }

        let filtered_history = filtered_capture_history(
            &self.capture_history,
            self.history_kind_filter,
            self.history_upload_filter,
        );

        if self.capture_history.is_empty() {
            return body.child(
                div()
                    .text_size(px(12.0))
                    .text_color(rgb(0x8b93a3))
                    .child("No saved captures yet."),
            );
        }
        if filtered_history.is_empty() {
            return body.child(div().text_size(px(12.0)).text_color(rgb(0x8b93a3)).child(
                SharedString::from(format!(
                    "No captures for {} / {}.",
                    self.history_kind_filter.label(),
                    self.history_upload_filter.label()
                )),
            ));
        }

        let visible_history = image_search::visible_items(
            &self.settings,
            &self.image_search,
            &filtered_history,
            6,
            20,
        );
        let search_active = image_search::is_active(&self.settings, &self.image_search);

        if visible_history.is_empty() && search_active {
            return body.child(div().text_size(px(12.0)).text_color(rgb(0x8b93a3)).child(
                SharedString::from(format!(
                    "No matches for '{}'.",
                    self.image_search.query.trim()
                )),
            ));
        }

        if let Some(preview_path) = visible_history
            .iter()
            .find_map(|entry| entry.preview_path.clone())
        {
            body = body.child(self.capture_preview(preview_path));
        }

        for entry in &visible_history {
            let diagnostics = image_search::diagnostics(&self.settings, &self.image_search, entry);
            let match_summary = image_search::match_summary(&diagnostics);
            let details_text = diagnostics.details_text;
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
                    .when(search_active, |row| {
                        row.child(
                            div()
                                .min_w_0()
                                .w_full()
                                .truncate()
                                .text_size(px(11.0))
                                .text_color(rgb(0x9ba3af))
                                .child(SharedString::from(match_summary.clone())),
                        )
                    })
                    .when(self.settings.show_image_search_diagnostics, |row| {
                        row.child(
                            div()
                                .min_w_0()
                                .w_full()
                                .line_clamp(3)
                                .text_size(px(11.0))
                                .text_color(rgb(0x7f8795))
                                .child(SharedString::from(details_text.clone())),
                        )
                    })
                    .child(
                        div()
                            .flex()
                            .flex_wrap()
                            .gap(px(8.0))
                            .child(self.open_history_file_button(cx, entry.path.clone()))
                            .child(self.open_history_button(cx, entry.path.clone()))
                            .child(self.copy_history_path_button(cx, entry.path.clone()))
                            .when(entry.kind == HistoryKind::Image, |row| {
                                row.child(self.copy_history_image_button(cx, entry.path.clone()))
                            })
                            .when_some(entry.upload_url.clone(), |row, url| {
                                row.child(self.open_upload_url_button(cx, url.clone()))
                                    .child(self.copy_upload_url_button(cx, url))
                            })
                            .child(self.retry_upload_button(cx, entry.path.clone(), entry.kind))
                            .child(self.remove_history_button(cx, entry.path.clone())),
                    ),
            );
        }

        body
    }

    fn history_filter_bar(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let filtered_count = filtered_capture_history(
            &self.capture_history,
            self.history_kind_filter,
            self.history_upload_filter,
        )
        .len();

        div()
            .flex()
            .flex_wrap()
            .gap(px(8.0))
            .child(self.history_kind_filter_button(cx))
            .child(self.history_upload_filter_button(cx))
            .child(self.copy_filtered_upload_links_button(cx))
            .child(self.upload_filtered_history_button(cx))
            .child(self.remove_filtered_history_button(cx))
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(rgb(0x8b93a3))
                    .child(SharedString::from(format!(
                        "{filtered_count} visible before search"
                    ))),
            )
    }

    fn history_kind_filter_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("history-kind-filter-button"),
            ui::ButtonVariant::History,
        )
        .child(SharedString::from(format!(
            "Kind {}",
            self.history_kind_filter.label()
        )))
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.history_kind_filter = this.history_kind_filter.next();
            this.capture_status =
                format!("History kind filter: {}.", this.history_kind_filter.label());
            cx.notify();
        }))
    }

    fn history_upload_filter_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("history-upload-filter-button"),
            ui::ButtonVariant::History,
        )
        .child(SharedString::from(format!(
            "Upload {}",
            self.history_upload_filter.label()
        )))
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.history_upload_filter = this.history_upload_filter.next();
            this.capture_status = format!(
                "History upload filter: {}.",
                this.history_upload_filter.label()
            );
            cx.notify();
        }))
    }

    fn image_search_bar(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let query_label = if self.image_search.query.is_empty() {
            "Search screenshots, OCR text, or filenames...".into()
        } else {
            self.image_search.query.clone()
        };
        let query_color = if self.image_search.query.is_empty() {
            rgb(0x7f8795)
        } else {
            ui::skin::color(ui::skin::BRIGHT_TEXT)
        };
        let sources = image_search::sources(&self.settings);
        let match_count = image_search::visible_items(
            &self.settings,
            &self.image_search,
            &self.capture_history,
            6,
            20,
        )
        .len();

        div()
            .flex()
            .flex_col()
            .gap(px(6.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(8.0))
                    .child(
                        div()
                            .id("image-search-field")
                            .flex_1()
                            .min_w_0()
                            .rounded(px(7.0))
                            .border_1()
                            .border_color(if self.image_search.input_active {
                                rgb(0x5d6f92)
                            } else {
                                rgb(0x303744)
                            })
                            .bg(rgb(0x151922))
                            .px(px(10.0))
                            .py(px(7.0))
                            .truncate()
                            .text_size(px(12.0))
                            .text_color(query_color)
                            .child(SharedString::from(query_label))
                            .on_click(cx.listener(|this: &mut Self, _, window, cx| {
                                cx.stop_propagation();
                                window.focus(&this.focus_handle(cx), cx);
                                this.image_search.activate();
                                cx.notify();
                            })),
                    )
                    .child(self.settings_button(
                        cx,
                        "image-search-file-name-button",
                        format!(
                            "File {}",
                            on_off(sources.contains(ImageSearchSources::FILE_NAME))
                        ),
                        SettingsAction::ToggleImageSearchFileName,
                    ))
                    .child(self.settings_button(
                        cx,
                        "image-search-ocr-button",
                        format!("OCR {}", on_off(sources.contains(ImageSearchSources::OCR))),
                        SettingsAction::ToggleImageSearchOcr,
                    ))
                    .child(self.settings_button(
                        cx,
                        "image-search-exact-button",
                        format!("Exact {}", on_off(self.settings.image_search_exact_match)),
                        SettingsAction::ToggleImageSearchExactMatch,
                    ))
                    .child(self.settings_button(
                        cx,
                        "image-search-diagnostics-button",
                        format!(
                            "Diag {}",
                            on_off(self.settings.show_image_search_diagnostics)
                        ),
                        SettingsAction::ToggleImageSearchDiagnostics,
                    ))
                    .when(!self.image_search_reindex_queue.active, |row| {
                        row.child(self.image_search_index_ocr_button(cx))
                    })
                    .when(self.image_search_reindex_queue.active, |row| {
                        row.child(self.settings_button(
                            cx,
                            "image-search-cancel-reindex-button",
                            "Cancel OCR".into(),
                            SettingsAction::CancelImageSearchReindexQueue,
                        ))
                    })
                    .when(!self.image_search_reindex_queue.active, |row| {
                        row.child(self.settings_button(
                            cx,
                            "image-search-start-reindex-button",
                            "Reindex OCR".into(),
                            SettingsAction::StartImageSearchReindexQueue,
                        ))
                    })
                    .child(self.settings_button(
                        cx,
                        "image-search-auto-index-button",
                        format!("Auto OCR {}", on_off(self.settings.auto_index_images)),
                        SettingsAction::ToggleImageSearchAutoIndex,
                    ))
                    .child(self.settings_button(
                        cx,
                        "image-search-hide-button",
                        "Hide".into(),
                        SettingsAction::ToggleImageSearchBar,
                    )),
            )
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(rgb(0x8b93a3))
                    .child(SharedString::from(
                        self.image_search.status_text(&self.settings, match_count),
                    )),
            )
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(rgb(0x8b93a3))
                    .child(SharedString::from(
                        self.image_search_reindex_queue.detail_text(),
                    )),
            )
    }

    fn ocr_result_panel(&self, cx: &mut Context<Self>, text: &str) -> impl IntoElement {
        ui::surface_style(div())
            .flex()
            .flex_col()
            .gap(px(7.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(13.0)).child("OCR result"))
                    .child(
                        div()
                            .flex()
                            .gap(px(8.0))
                            .child(self.open_ocr_result_window_button(cx, text.to_string()))
                            .child(self.copy_ocr_text_button(cx, text.to_string()))
                            .child(self.translate_ocr_text_button(cx, text.to_string())),
                    ),
            )
            .child(
                div()
                    .line_clamp(8)
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::BODY_TEXT))
                    .child(SharedString::from(text.to_string())),
            )
    }

    fn scan_result_panel(
        &self,
        cx: &mut Context<Self>,
        entry: &CodeHistoryEntry,
    ) -> impl IntoElement {
        ui::surface_style(div())
            .flex()
            .flex_col()
            .gap(px(7.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(13.0)).child(SharedString::from(format!(
                        "{} result",
                        humanize_barcode_format(&entry.format)
                    ))))
                    .child(self.copy_code_text_button(cx, entry.text.clone())),
            )
            .child(
                div()
                    .line_clamp(8)
                    .text_size(px(12.0))
                    .text_color(ui::skin::color(ui::skin::BODY_TEXT))
                    .child(SharedString::from(entry.text.clone())),
            )
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

    fn on_key_down(&mut self, event: &KeyDownEvent, _window: &mut Window, cx: &mut Context<Self>) {
        if let Some(input) = self.settings_text_input.as_mut() {
            match input.handle_key_down(event) {
                settings_text::SettingsTextInputEvent::Editing => {
                    cx.stop_propagation();
                    cx.notify();
                    return;
                }
                settings_text::SettingsTextInputEvent::Commit { target, value } => {
                    self.settings_text_input = None;
                    self.commit_settings_text_input(target, value);
                    cx.stop_propagation();
                    cx.notify();
                    return;
                }
                settings_text::SettingsTextInputEvent::Cancel => {
                    self.settings_text_input = None;
                    self.capture_status = "Settings text edit canceled.".into();
                    cx.stop_propagation();
                    cx.notify();
                    return;
                }
                settings_text::SettingsTextInputEvent::Ignored => {}
            }
        }

        if !self.settings.show_image_search_bar {
            return;
        }
        if !self.image_search.handle_key_down(event) {
            return;
        }
        cx.stop_propagation();
        cx.notify();
    }

    fn capability_panel(&self) -> impl IntoElement {
        let mut body = ui::panel_style(div())
            .flex()
            .flex_col()
            .gap(px(8.0))
            .flex_1()
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
        ui::action_button_style(div().id(element_id), ui::ButtonVariant::Capture)
            .child(mode.label())
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_capture(mode);
                cx.notify();
            }))
    }

    fn color_picker_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(div().id("color-picker-button"), ui::ButtonVariant::Capture)
            .child("Color")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_color_picker();
                cx.notify();
            }))
    }

    fn ocr_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(div().id("ocr-button"), ui::ButtonVariant::Capture)
            .child("OCR")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_ocr_capture("OCR button");
                cx.notify();
            }))
    }

    fn scan_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(div().id("scan-button"), ui::ButtonVariant::Capture)
            .child("Scan")
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.run_scan_capture("Scan button");
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
        ui::action_button_style(div().id(element_id), ui::ButtonVariant::Setting)
            .child(SharedString::from(label))
            .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
                cx.stop_propagation();
                this.apply_settings_action(action);
                cx.notify();
            }))
    }

    fn start_settings_text_input(&mut self, target: settings_text::SettingsTextTarget) {
        let value = match target {
            settings_text::SettingsTextTarget::RemoveBg => {
                StickerSettings::from_json_value(self.settings.sticker_upload_settings.as_ref())
                    .remove_bg_api_key
            }
            settings_text::SettingsTextTarget::Photoroom => {
                StickerSettings::from_json_value(self.settings.sticker_upload_settings.as_ref())
                    .photoroom_api_key
            }
            settings_text::SettingsTextTarget::DeepAi => {
                UpscaleSettings::from_json_value(self.settings.upscale_upload_settings.as_ref())
                    .deep_ai_api_key
            }
        };
        self.settings_text_input = Some(settings_text::SettingsTextInputState::new(target, value));
        self.capture_status = format!("Editing {}.", target.label());
    }

    fn commit_settings_text_input(
        &mut self,
        target: settings_text::SettingsTextTarget,
        value: String,
    ) {
        match target {
            settings_text::SettingsTextTarget::RemoveBg => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.remove_bg_api_key = value;
                self.persist_sticker_settings(settings, "Remove.bg API key updated".into());
            }
            settings_text::SettingsTextTarget::Photoroom => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.photoroom_api_key = value;
                self.persist_sticker_settings(settings, "Photoroom API key updated".into());
            }
            settings_text::SettingsTextTarget::DeepAi => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                settings.deep_ai_api_key = value;
                self.persist_upscale_settings(settings, "DeepAI API key updated".into());
            }
        }
    }

    fn image_search_index_ocr_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("image-search-index-ocr-button"),
            ui::ButtonVariant::History,
        )
        .child("Index OCR")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.hydrate_image_search_ocr();
            cx.notify();
        }))
    }

    fn open_history_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("open-history-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Reveal")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.open_history_path(PathBuf::from(&path));
            cx.notify();
        }))
    }

    fn open_history_file_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("open-history-file-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Open")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.open_history_file(PathBuf::from(&path));
            cx.notify();
        }))
    }

    fn copy_history_path_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("copy-history-path-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Copy path")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_history_path(path.clone());
            cx.notify();
        }))
    }

    fn copy_history_image_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("copy-history-image-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Copy image")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_history_image(PathBuf::from(&path));
            cx.notify();
        }))
    }

    fn copy_upload_url_button(&self, cx: &mut Context<Self>, url: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("copy-upload-{url}"))),
            ui::ButtonVariant::History,
        )
        .child("Copy link")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_history_upload_url(url.clone());
            cx.notify();
        }))
    }

    fn open_upload_url_button(&self, cx: &mut Context<Self>, url: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("open-upload-{url}"))),
            ui::ButtonVariant::History,
        )
        .child("Open link")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.open_history_upload_url(url.clone());
            cx.notify();
        }))
    }

    fn retry_upload_button(
        &self,
        cx: &mut Context<Self>,
        path: String,
        kind: HistoryKind,
    ) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("retry-upload-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Upload")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.retry_history_upload(PathBuf::from(&path), kind);
            cx.notify();
        }))
    }

    fn upload_filtered_history_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("upload-filtered-history-button"),
            ui::ButtonVariant::History,
        )
        .child("Upload filtered")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.retry_filtered_history_uploads();
            cx.notify();
        }))
    }

    fn copy_filtered_upload_links_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("copy-filtered-upload-links-button"),
            ui::ButtonVariant::History,
        )
        .child("Copy links")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_filtered_history_upload_links();
            cx.notify();
        }))
    }

    fn remove_history_button(&self, cx: &mut Context<Self>, path: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("remove-history-{path}"))),
            ui::ButtonVariant::History,
        )
        .child("Remove")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.remove_history_entry(path.clone());
            cx.notify();
        }))
    }

    fn remove_filtered_history_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        ui::action_button_style(
            div().id("remove-filtered-history-button"),
            ui::ButtonVariant::History,
        )
        .child("Remove filtered")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.remove_filtered_history_entries();
            cx.notify();
        }))
    }

    fn copy_color_hex_button(&self, cx: &mut Context<Self>, hex: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!("copy-color-{hex}"))),
            ui::ButtonVariant::History,
        )
        .child("Copy")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_color_history_hex(hex.clone());
            cx.notify();
        }))
    }

    fn remove_color_history_button(
        &self,
        cx: &mut Context<Self>,
        hex: String,
        captured_at_unix_ms: u64,
    ) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "remove-color-{hex}-{captured_at_unix_ms}"
            ))),
            ui::ButtonVariant::History,
        )
        .child("Remove")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.remove_color_history_entry(hex.clone(), captured_at_unix_ms);
            cx.notify();
        }))
    }

    fn copy_ocr_text_button(&self, cx: &mut Context<Self>, text: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "copy-ocr-{}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Copy text")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_ocr_history_text(text.clone());
            cx.notify();
        }))
    }

    fn open_ocr_result_window_button(
        &self,
        cx: &mut Context<Self>,
        text: String,
    ) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "open-ocr-{}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Open")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.open_ocr_result_window(text.clone(), cx);
            cx.notify();
        }))
    }

    fn remove_ocr_history_button(
        &self,
        cx: &mut Context<Self>,
        text: String,
        captured_at_unix_ms: u64,
    ) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "remove-ocr-{}-{captured_at_unix_ms}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Remove")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.remove_ocr_history_entry(text.clone(), captured_at_unix_ms);
            cx.notify();
        }))
    }

    fn translate_ocr_text_button(&self, cx: &mut Context<Self>, text: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "translate-ocr-{}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Translate")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.translate_ocr_history_text(text.clone());
            cx.notify();
        }))
    }

    fn copy_code_text_button(&self, cx: &mut Context<Self>, text: String) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "copy-code-{}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Copy")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_code_history_text(text.clone());
            cx.notify();
        }))
    }

    fn remove_code_history_button(
        &self,
        cx: &mut Context<Self>,
        text: String,
        format: String,
        captured_at_unix_ms: u64,
    ) -> impl IntoElement {
        ui::action_button_style(
            div().id(SharedString::from(format!(
                "remove-code-{}-{captured_at_unix_ms}",
                stable_text_key(&text)
            ))),
            ui::ButtonVariant::History,
        )
        .child("Remove")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.remove_code_history_entry(text.clone(), format.clone(), captured_at_unix_ms);
            cx.notify();
        }))
    }

    fn recording_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let label = if self.active_recording.is_some() {
            "Stop recording"
        } else {
            "Start recording"
        };

        ui::action_button_style(
            div().id("recording-toggle-button"),
            ui::ButtonVariant::Recording,
        )
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

        ui::action_button_style(
            div().id("recording-window-button"),
            ui::ButtonVariant::RecordingSecondary,
        )
        .child(label)
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            if this.active_recording.is_none() {
                this.start_recording(RecordingTarget::ActiveWindow);
            }
            cx.notify();
        }))
    }

    fn recording_region_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let label = if self.active_recording.is_some() {
            "Region busy"
        } else {
            "Record region"
        };

        ui::action_button_style(
            div().id("recording-region-button"),
            ui::ButtonVariant::RecordingSecondary,
        )
        .child(label)
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            if this.active_recording.is_none() {
                this.start_recording(RecordingTarget::Region);
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
        let capture = self.capture_with_adapter(adapter, mode)?;
        self.save_and_copy_capture(adapter, capture)
    }

    #[cfg_attr(target_os = "macos", allow(dead_code))]
    fn capture_with_adapter<T>(
        &self,
        adapter: &T,
        mode: CaptureMode,
    ) -> Result<CaptureResult, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService + WindowPickerService + RegionSelectionService,
    {
        match mode {
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
                    show_magnifier: self.settings.show_capture_magnifier,
                    detect_windows: self.settings.detect_windows,
                    selection_mode: RegionSelectionMode::Rectangle,
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
        }
    }

    #[cfg(target_os = "macos")]
    fn run_capture_with_macos_adapter(
        &self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        mode: CaptureMode,
    ) -> Result<CaptureRunResult, oddsnap_platform::PlatformError> {
        let capture = self.capture_with_macos_adapter(adapter, mode)?;
        self.save_and_copy_capture(adapter, capture)
    }

    #[cfg(target_os = "macos")]
    fn capture_with_macos_adapter(
        &self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        mode: CaptureMode,
    ) -> Result<CaptureResult, oddsnap_platform::PlatformError> {
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
        };

        capture
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
            original_preview_path: None,
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

    fn run_ocr_capture(&mut self, trigger: &'static str) {
        if self.settings.capture_delay_seconds > 0 {
            self.capture_status = format!(
                "Waiting {}s before OCR capture.",
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
            self.run_ocr_capture_with_adapter(&adapter, CaptureMode::Rectangle)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.run_ocr_capture_with_macos_adapter(&adapter, CaptureMode::Rectangle)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_ocr_capture_with_adapter(&adapter, CaptureMode::Rectangle)
        };

        self.capture_status = match result {
            Ok(status) => format!("{trigger} received. {status}"),
            Err(error) => format!("{} OCR failed: {error}", platform.name()),
        };
    }

    #[cfg_attr(target_os = "macos", allow(dead_code))]
    fn run_ocr_capture_with_adapter<T>(
        &mut self,
        adapter: &T,
        mode: CaptureMode,
    ) -> Result<String, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService
            + WindowPickerService
            + RegionSelectionService
            + OcrTextService
            + ClipboardTextService,
    {
        let capture = self.capture_with_adapter(adapter, mode)?;
        self.finish_ocr_capture(adapter, capture)
    }

    #[cfg(target_os = "macos")]
    fn run_ocr_capture_with_macos_adapter(
        &mut self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        mode: CaptureMode,
    ) -> Result<String, oddsnap_platform::PlatformError> {
        let capture = self.capture_with_macos_adapter(adapter, mode)?;
        self.finish_ocr_capture(adapter, capture)
    }

    fn finish_ocr_capture<T>(
        &mut self,
        adapter: &T,
        capture: CaptureResult,
    ) -> Result<String, oddsnap_platform::PlatformError>
    where
        T: OcrTextService + ClipboardTextService,
    {
        let captured_path = capture.image_path.clone();
        let result = adapter.recognize_text(OcrTextRequest {
            image_path: capture.image_path,
            language_tag: self.settings.ocr_language_tag.clone(),
        });
        let _ = fs::remove_file(&captured_path);
        let result = result?;
        let text = result.text.trim().to_string();
        if text.is_empty() {
            return Ok(format!("OCR found no text with {}.", result.engine_id));
        }

        let copy_error = adapter
            .copy_text_to_clipboard(&text)
            .err()
            .map(|error| error.to_string());
        let char_count = text.chars().count();
        self.latest_ocr_result = Some(text.clone());
        let history_status = self.save_ocr_history(text);
        Ok(match copy_error {
            Some(error) => format!(
                "OCR read {char_count} chars with {}; clipboard copy failed ({error}){history_status}.",
                result.engine_id
            ),
            None => format!(
                "OCR read {char_count} chars with {}, copied text{history_status}.",
                result.engine_id
            ),
        })
    }

    fn run_scan_capture(&mut self, trigger: &'static str) {
        if self.settings.capture_delay_seconds > 0 {
            self.capture_status = format!(
                "Waiting {}s before QR/barcode scan.",
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
            self.run_scan_capture_with_adapter(&adapter, CaptureMode::Rectangle)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.run_scan_capture_with_macos_adapter(&adapter, CaptureMode::Rectangle)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_scan_capture_with_adapter(&adapter, CaptureMode::Rectangle)
        };

        self.capture_status = match result {
            Ok(status) => format!("{trigger} received. {status}"),
            Err(error) => format!("{} scan failed: {error}", platform.name()),
        };
    }

    #[cfg_attr(target_os = "macos", allow(dead_code))]
    fn run_scan_capture_with_adapter<T>(
        &mut self,
        adapter: &T,
        mode: CaptureMode,
    ) -> Result<String, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService
            + WindowPickerService
            + RegionSelectionService
            + ClipboardTextService,
    {
        let capture = self.capture_with_adapter(adapter, mode)?;
        self.finish_scan_capture(adapter, capture)
    }

    #[cfg(target_os = "macos")]
    fn run_scan_capture_with_macos_adapter(
        &mut self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        mode: CaptureMode,
    ) -> Result<String, oddsnap_platform::PlatformError> {
        let capture = self.capture_with_macos_adapter(adapter, mode)?;
        self.finish_scan_capture(adapter, capture)
    }

    fn finish_scan_capture<T>(
        &mut self,
        adapter: &T,
        capture: CaptureResult,
    ) -> Result<String, oddsnap_platform::PlatformError>
    where
        T: ClipboardTextService,
    {
        let captured_path = capture.image_path.clone();
        let decoded = decode_barcode_image(&captured_path);
        let _ = fs::remove_file(&captured_path);
        let decoded =
            decoded.map_err(|error| oddsnap_platform::PlatformError::Failed(error.to_string()))?;
        let Some(decoded) = decoded else {
            return Ok("Scan found no QR/barcode.".into());
        };

        let copy_error = adapter
            .copy_text_to_clipboard(&decoded.text)
            .err()
            .map(|error| error.to_string());
        let entry = CodeHistoryEntry::new(decoded.text, decoded.format);
        let format_label = humanize_barcode_format(&entry.format);
        self.latest_scan_result = Some(entry.clone());
        let history_status = self.save_code_history(entry);
        Ok(match copy_error {
            Some(error) => {
                format!("{format_label} found; clipboard copy failed ({error}){history_status}.")
            }
            None => format!("{format_label} found and copied{history_status}."),
        })
    }

    fn run_sticker_capture(&mut self, trigger: &'static str) {
        self.run_processed_capture(trigger, ProcessedCaptureTool::Sticker, None);
    }

    fn run_upscale_capture(&mut self, trigger: &'static str, cx: &mut Context<Self>) {
        self.run_processed_capture(trigger, ProcessedCaptureTool::Upscale, Some(cx));
    }

    fn run_processed_capture(
        &mut self,
        trigger: &'static str,
        tool: ProcessedCaptureTool,
        cx: Option<&mut Context<Self>>,
    ) {
        if self.settings.capture_delay_seconds > 0 {
            self.capture_status = format!(
                "Waiting {}s before {} capture.",
                self.settings.capture_delay_seconds,
                tool.slug()
            );
            thread::sleep(Duration::from_secs(
                self.settings.capture_delay_seconds as u64,
            ));
        }

        let platform = host_platform();
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.run_processed_capture_with_adapter(&adapter, tool)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.run_processed_capture_with_macos_adapter(&adapter, tool)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_processed_capture_with_adapter(&adapter, tool)
        };

        self.capture_status = match result {
            Ok((result, provider_name)) => {
                let path = result.capture.image_path.display().to_string();
                let history_status = self.save_capture_history_with_kind(
                    &result.capture,
                    CaptureMode::Rectangle,
                    tool.history_kind(),
                );
                let preview_status = cx.map_or_else(String::new, |cx| {
                    self.open_processed_result_preview(tool, &provider_name, &result, cx)
                });
                let copy_status = match (
                    self.settings.copy_captures_to_clipboard,
                    result.copy_error.as_deref(),
                ) {
                    (true, None) => "copied and saved".to_string(),
                    (true, Some(error)) => format!("saved; clipboard copy failed ({error})"),
                    (false, _) => "saved".to_string(),
                };
                format!(
                    "{trigger} received. {} {} via {provider_name} {copy_status} {path}{history_status}",
                    platform.name(),
                    tool.label()
                ) + &preview_status
            }
            Err(error) => format!(
                "{} {} capture failed: {error}",
                platform.name(),
                tool.label()
            ),
        };
    }

    fn run_processed_capture_with_adapter<T>(
        &self,
        adapter: &T,
        tool: ProcessedCaptureTool,
    ) -> Result<(CaptureRunResult, String), String>
    where
        T: ScreenCaptureService + ClipboardImageService + RegionSelectionService,
    {
        let bounds = match adapter.monitors() {
            Ok(monitors) => virtual_screen_region(&monitors)
                .ok_or_else(|| "no monitors available for selection".to_string())?,
            #[cfg(target_os = "linux")]
            Err(error) => {
                let _ = error;
                CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 1,
                    height: 1,
                }
            }
            #[cfg(not(target_os = "linux"))]
            Err(error) => return Err(error.to_string()),
        };
        let Some(region) = adapter
            .select_region(OverlayWindowRequest {
                bounds,
                opacity: 24,
                click_through: false,
                show_crosshair_guides: self.settings.show_crosshair_guides,
                show_magnifier: self.settings.show_capture_magnifier,
                detect_windows: self.settings.detect_windows,
                selection_mode: RegionSelectionMode::Rectangle,
            })
            .map_err(|error| error.to_string())?
        else {
            return Err(format!("{} selection canceled", tool.slug()));
        };

        let capture = adapter
            .capture_region_with_options(CaptureRequest {
                region,
                include_cursor: self.settings.show_cursor,
            })
            .map_err(|error| error.to_string())?;
        self.finish_processed_capture(adapter, tool, capture)
    }

    #[cfg(target_os = "macos")]
    fn run_processed_capture_with_macos_adapter(
        &self,
        adapter: &oddsnap_platform_macos::MacosPlatform,
        tool: ProcessedCaptureTool,
    ) -> Result<(CaptureRunResult, String), String> {
        let capture = adapter
            .capture_interactive_selection(self.settings.show_cursor)
            .map_err(|error| error.to_string())?;
        self.finish_processed_capture(adapter, tool, capture)
    }

    fn finish_processed_capture<T>(
        &self,
        adapter: &T,
        tool: ProcessedCaptureTool,
        capture: CaptureResult,
    ) -> Result<(CaptureRunResult, String), String>
    where
        T: ClipboardImageService,
    {
        let input_path = temporary_processed_capture_path(tool, "input.png");
        let output_path = temporary_processed_capture_path(tool, "output.png");
        let source = persist_capture_to_path_as(
            &capture,
            &input_path,
            CaptureImageFormat::Png,
            self.settings.jpeg_quality,
        )
        .map_err(|error| error.to_string())?;
        if capture.image_path != source.image_path {
            let _ = fs::remove_file(&capture.image_path);
        }

        let process_result = match tool {
            ProcessedCaptureTool::Sticker => sticker_upscale::process_sticker_capture(
                &self.settings,
                &source.image_path,
                &output_path,
            ),
            ProcessedCaptureTool::Upscale => sticker_upscale::process_upscale_capture(
                &self.settings,
                &source.image_path,
                &output_path,
            ),
        };
        let provider_name = match process_result {
            Ok(provider_name) => provider_name,
            Err(error) => {
                let _ = fs::remove_file(&input_path);
                let _ = fs::remove_file(&output_path);
                return Err(error);
            }
        };

        let (width, height) = image_file_dimensions(&output_path).map_err(|error| {
            let _ = fs::remove_file(&input_path);
            let _ = fs::remove_file(&output_path);
            error.to_string()
        })?;
        let processed_capture = oddsnap_platform::CaptureResult {
            image_path: output_path.clone(),
            region: CaptureRegion {
                x: source.region.x,
                y: source.region.y,
                width,
                height,
            },
        };
        let destination = self.capture_destination_with_extension(&processed_capture, "png");
        if let Some(parent) = destination.parent() {
            fs::create_dir_all(parent)
                .map_err(|error| format!("failed to create capture directory: {error}"))?;
        }
        fs::copy(&output_path, &destination)
            .map_err(|error| format!("failed to save {} capture: {error}", tool.slug()))?;
        let preview_original_path =
            processed_capture_preview_enabled(tool, &self.settings).then_some(input_path.clone());
        if preview_original_path.is_none() {
            let _ = fs::remove_file(&input_path);
        }
        let _ = fs::remove_file(&output_path);

        let saved = oddsnap_platform::CaptureResult {
            image_path: destination,
            region: processed_capture.region,
        };
        let copy_error = if self.settings.copy_captures_to_clipboard {
            adapter
                .copy_image_to_clipboard(&saved.image_path)
                .err()
                .map(|error| error.to_string())
        } else {
            None
        };
        Ok((
            CaptureRunResult {
                capture: saved,
                copy_error,
                original_preview_path: preview_original_path,
            },
            provider_name,
        ))
    }

    fn run_center_capture(&mut self, trigger: &'static str) {
        if self.settings.capture_delay_seconds > 0 {
            self.capture_status = format!(
                "Waiting {}s before center capture.",
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
            self.run_center_capture_with_adapter(&adapter)
        };

        #[cfg(target_os = "macos")]
        let result = Err(oddsnap_platform::PlatformError::Unsupported(
            "macOS center selection needs the production overlay",
        ));

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_center_capture_with_adapter(&adapter)
        };

        self.capture_status = match result {
            Ok(result) => {
                let path = result.capture.image_path.display().to_string();
                let history_status =
                    self.save_capture_history(&result.capture, CaptureMode::Rectangle);
                let copy_status = match (
                    self.settings.copy_captures_to_clipboard,
                    result.copy_error.as_deref(),
                ) {
                    (true, None) => "copied and saved".to_string(),
                    (true, Some(error)) => format!("saved; clipboard copy failed ({error})"),
                    (false, _) => "saved".to_string(),
                };
                format!(
                    "{trigger} received. {} Center {copy_status} {path}{history_status}",
                    platform.name()
                )
            }
            Err(error) => format!("{} center capture failed: {error}", platform.name()),
        };
    }

    fn run_center_capture_with_adapter<T>(
        &self,
        adapter: &T,
    ) -> Result<CaptureRunResult, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService
            + WindowPickerService
            + ClipboardImageService
            + RegionSelectionService,
    {
        let bounds = match adapter.monitors() {
            Ok(monitors) => virtual_screen_region(&monitors).ok_or_else(|| {
                oddsnap_platform::PlatformError::Failed(
                    "no monitors available for center selection".into(),
                )
            })?,
            #[cfg(target_os = "linux")]
            Err(error) => {
                let _ = error;
                CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 1,
                    height: 1,
                }
            }
            #[cfg(not(target_os = "linux"))]
            Err(error) => return Err(error),
        };
        let Some(region) = adapter.select_region(OverlayWindowRequest {
            bounds,
            opacity: 24,
            click_through: false,
            show_crosshair_guides: self.settings.show_crosshair_guides,
            show_magnifier: self.settings.show_capture_magnifier,
            detect_windows: false,
            selection_mode: RegionSelectionMode::Center {
                aspect_ratio: center_selection_aspect_ratio(
                    &self.settings.center_selection_aspect_ratio,
                ),
            },
        })?
        else {
            return Err(oddsnap_platform::PlatformError::Failed(
                "center selection canceled".into(),
            ));
        };

        let capture = adapter.capture_region_with_options(CaptureRequest {
            region,
            include_cursor: self.settings.show_cursor,
        })?;
        self.save_and_copy_capture(adapter, capture)
    }

    fn run_default_capture_command(&mut self, trigger: &'static str, cx: &mut Context<Self>) {
        match default_capture_action(self.settings.default_capture_mode) {
            DefaultCaptureAction::Capture(mode) => {
                self.capture_status = format!("{trigger} received.");
                self.run_capture(mode);
            }
            DefaultCaptureAction::ColorPicker => {
                self.capture_status = format!("{trigger} received.");
                self.run_color_picker();
            }
            DefaultCaptureAction::Ocr => {
                self.run_ocr_capture(trigger);
            }
            DefaultCaptureAction::Scan => {
                self.run_scan_capture(trigger);
            }
            DefaultCaptureAction::Sticker => {
                self.run_sticker_capture(trigger);
            }
            DefaultCaptureAction::Upscale => {
                self.run_upscale_capture(trigger, cx);
            }
            DefaultCaptureAction::Center => {
                self.run_center_capture(trigger);
            }
            DefaultCaptureAction::Ruler => {
                self.run_ruler_measure(trigger);
            }
        }
    }

    fn run_ruler_measure(&mut self, trigger: &'static str) {
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.run_ruler_measure_with_adapter(&adapter)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.run_ruler_measure_with_adapter(&adapter)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.run_ruler_measure_with_adapter(&adapter)
        };

        self.capture_status = match result {
            Ok(measurement) => format!("{trigger} received. Ruler measured {measurement}."),
            Err(error) => format!("Ruler failed: {error}"),
        };
    }

    fn run_ruler_measure_with_adapter<T>(
        &self,
        adapter: &T,
    ) -> Result<String, oddsnap_platform::PlatformError>
    where
        T: ScreenCaptureService + RegionSelectionService + ClipboardTextService,
    {
        let bounds = ruler_selection_bounds(adapter)?;
        let Some(region) = adapter.select_region(OverlayWindowRequest {
            bounds,
            opacity: 24,
            click_through: false,
            show_crosshair_guides: true,
            show_magnifier: self.settings.show_capture_magnifier,
            detect_windows: self.settings.detect_windows,
            selection_mode: RegionSelectionMode::Rectangle,
        })?
        else {
            return Err(oddsnap_platform::PlatformError::Failed(
                "ruler selection canceled".into(),
            ));
        };
        let measurement = ruler_measurement_text(region);
        adapter.copy_text_to_clipboard(&measurement)?;
        Ok(measurement)
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

    fn save_ocr_history(&mut self, text: String) -> String {
        if !self.settings.save_history {
            self.ocr_history.insert(0, OcrHistoryEntry::new(text));
            self.ocr_history.truncate(12);
            return String::new();
        }

        match self
            .history_store
            .append_ocr_entry(OcrHistoryEntry::new(text))
        {
            Ok(index) => {
                self.ocr_history = history_entries_to_ocr_history(index);
                "; OCR history saved".into()
            }
            Err(error) => format!("; OCR history save failed: {error}"),
        }
    }

    fn save_code_history(&mut self, entry: CodeHistoryEntry) -> String {
        if !self.settings.save_history {
            self.code_history.retain(|existing| {
                !(existing.text == entry.text
                    && existing.format.eq_ignore_ascii_case(&entry.format))
            });
            self.code_history.insert(0, entry);
            self.code_history.truncate(12);
            return String::new();
        }

        match self.history_store.append_code_entry(entry) {
            Ok(index) => {
                self.code_history = history_entries_to_code_history(index);
                "; scan history saved".into()
            }
            Err(error) => format!("; scan history save failed: {error}"),
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
            SettingsAction::ToggleMuteSounds => {
                self.settings.mute_sounds = !self.settings.mute_sounds;
                self.persist_capture_settings(format!(
                    "Capture sounds {}",
                    if self.settings.mute_sounds {
                        "muted"
                    } else {
                        "enabled"
                    }
                ));
            }
            SettingsAction::ToastDuration => {
                self.settings.toast_duration_seconds =
                    next_toast_duration_seconds(self.settings.toast_duration_seconds);
                self.persist_capture_settings(format!(
                    "Toast duration {:.1}s",
                    self.settings.toast_duration_seconds
                ));
            }
            SettingsAction::ToggleToastFade => {
                self.settings.toast_fade_out_enabled = !self.settings.toast_fade_out_enabled;
                self.persist_capture_settings(format!(
                    "Toast fade {}",
                    on_off(self.settings.toast_fade_out_enabled)
                ));
            }
            SettingsAction::ScrollingCaptureMode => {
                self.settings.scrolling_capture_mode =
                    next_scrolling_capture_mode(&self.settings.scrolling_capture_mode);
                self.persist_capture_settings(format!(
                    "Scrolling capture mode set to {}",
                    ScrollingCaptureMode::from_setting(&self.settings.scrolling_capture_mode)
                        .label()
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
            SettingsAction::ToggleStartWithSystem => {
                self.settings.start_with_windows = !self.settings.start_with_windows;
                self.persist_lifecycle_settings(format!(
                    "Start with system {}",
                    on_off(self.settings.start_with_windows)
                ));
            }
            SettingsAction::ToggleAutoCheckForUpdates => {
                self.settings.auto_check_for_updates = !self.settings.auto_check_for_updates;
                self.persist_lifecycle_settings(format!(
                    "Automatic update checks {}",
                    on_off(self.settings.auto_check_for_updates)
                ));
            }
            SettingsAction::ToggleImageSearchBar => {
                self.settings.show_image_search_bar = !self.settings.show_image_search_bar;
                if !self.settings.show_image_search_bar {
                    self.image_search.hide();
                }
                self.persist_image_search_settings(format!(
                    "Image search bar {}",
                    on_off(self.settings.show_image_search_bar)
                ));
            }
            SettingsAction::ToggleImageSearchFileName => {
                image_search::toggle_source(&mut self.settings, ImageSearchSources::FILE_NAME);
                self.persist_image_search_settings(format!(
                    "Image search file-name source {}",
                    on_off(
                        image_search::sources(&self.settings)
                            .contains(ImageSearchSources::FILE_NAME)
                    )
                ));
            }
            SettingsAction::ToggleImageSearchOcr => {
                image_search::toggle_source(&mut self.settings, ImageSearchSources::OCR);
                self.persist_image_search_settings(format!(
                    "Image search OCR source {}",
                    on_off(image_search::sources(&self.settings).contains(ImageSearchSources::OCR))
                ));
            }
            SettingsAction::ToggleImageSearchExactMatch => {
                self.settings.image_search_exact_match = !self.settings.image_search_exact_match;
                self.persist_image_search_settings(format!(
                    "Image search exact match {}",
                    on_off(self.settings.image_search_exact_match)
                ));
            }
            SettingsAction::ToggleImageSearchDiagnostics => {
                self.settings.show_image_search_diagnostics =
                    !self.settings.show_image_search_diagnostics;
                self.persist_image_search_settings(format!(
                    "Image search diagnostics {}",
                    on_off(self.settings.show_image_search_diagnostics)
                ));
            }
            SettingsAction::ToggleImageSearchAutoIndex => {
                self.settings.auto_index_images = !self.settings.auto_index_images;
                if !self.settings.auto_index_images {
                    self.image_search_index_status = "Image index: automatic OCR off.".into();
                } else {
                    self.image_search_index_status =
                        match self.image_search_index_store.load_or_default() {
                            Ok(index) => image_search_index_status(&index),
                            Err(error) => format!("Image index unavailable: {error}"),
                        };
                }
                self.persist_image_search_settings(format!(
                    "Image OCR auto-index {}",
                    on_off(self.settings.auto_index_images)
                ));
            }
            SettingsAction::StartImageSearchReindexQueue => {
                self.start_image_search_reindex_queue();
            }
            SettingsAction::CancelImageSearchReindexQueue => {
                self.cancel_image_search_reindex_queue();
            }
            SettingsAction::TranslationModel => {
                self.settings.translation_model =
                    next_translation_model(self.settings.translation_model) as u32;
                self.persist_translation_settings(format!(
                    "Translation model set to {}",
                    TranslationModel::from_legacy_value(self.settings.translation_model).label()
                ));
            }
            SettingsAction::TranslationSourceLanguage => {
                self.settings.ocr_default_translate_from =
                    next_supported_translation_language(&self.settings.ocr_default_translate_from);
                let source = oddsnap_core::resolve_translation_source_language(Some(
                    &self.settings.ocr_default_translate_from,
                ));
                self.persist_translation_settings(format!(
                    "Translation source set to {}",
                    oddsnap_core::translation_language_name(&source)
                ));
            }
            SettingsAction::TranslationTargetLanguage => {
                self.settings.ocr_default_translate_to =
                    next_supported_translation_language(&self.settings.ocr_default_translate_to);
                let target = oddsnap_core::resolve_translation_target_language(
                    Some(&self.settings.ocr_default_translate_to),
                    Some(&self.settings.interface_language),
                    None,
                );
                self.persist_translation_settings(format!(
                    "Translation target set to {}",
                    oddsnap_core::translation_language_name(&target)
                ));
            }
            SettingsAction::InstallArgosTranslationRuntime => {
                self.capture_status = match ocr_translation::install_argos_runtime() {
                    Ok(()) => "Argos Translate installed.".into(),
                    Err(error) => format!("Argos Translate install failed: {error}"),
                };
            }
            SettingsAction::RemoveArgosTranslationRuntime => {
                self.capture_status = match ocr_translation::remove_argos_runtime() {
                    Ok(()) => "Argos Translate removed.".into(),
                    Err(error) => format!("Argos Translate remove failed: {error}"),
                };
            }
            SettingsAction::InstallLocalTranslationRuntime => {
                self.capture_status = match ocr_translation::install_open_source_local_runtime() {
                    Ok(()) => {
                        self.settings.translation_runtime_installed = true;
                        match self.settings_store.save(&self.settings) {
                            Ok(()) => "Open-source local translation runtime installed.".into(),
                            Err(error) => {
                                format!("Open-source local runtime installed; settings save failed: {error}")
                            }
                        }
                    }
                    Err(error) => format!("Open-source local runtime install failed: {error}"),
                };
            }
            SettingsAction::RemoveLocalTranslationRuntime => {
                self.capture_status = match ocr_translation::remove_open_source_local_runtime() {
                    Ok(()) => {
                        self.settings.translation_runtime_installed = false;
                        match self.settings_store.save(&self.settings) {
                            Ok(()) => "Open-source local translation runtime removed.".into(),
                            Err(error) => {
                                format!("Open-source local runtime removed; settings save failed: {error}")
                            }
                        }
                    }
                    Err(error) => format!("Open-source local runtime remove failed: {error}"),
                };
            }
            SettingsAction::StickerProvider => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.provider = next_sticker_provider(&settings.provider);
                self.persist_sticker_settings(settings, "Sticker provider updated".into());
            }
            SettingsAction::EditRemoveBgApiKey => {
                self.start_settings_text_input(settings_text::SettingsTextTarget::RemoveBg);
            }
            SettingsAction::EditPhotoroomApiKey => {
                self.start_settings_text_input(settings_text::SettingsTextTarget::Photoroom);
            }
            SettingsAction::StickerLocalEngine => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                let next = next_sticker_local_engine(&settings.local_engine);
                set_active_sticker_engine(&mut settings, next);
                self.persist_sticker_settings(settings, "Sticker local model updated".into());
            }
            SettingsAction::StickerExecutionProvider => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.local_execution_provider =
                    next_execution_provider(&settings.local_execution_provider);
                self.persist_sticker_settings(settings, "Sticker local execution updated".into());
            }
            SettingsAction::ToggleStickerShadow => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.add_shadow = !settings.add_shadow;
                self.persist_sticker_settings(settings, "Sticker shadow updated".into());
            }
            SettingsAction::ToggleStickerStroke => {
                let mut settings = StickerSettings::from_json_value(
                    self.settings.sticker_upload_settings.as_ref(),
                );
                settings.add_stroke = !settings.add_stroke;
                self.persist_sticker_settings(settings, "Sticker stroke updated".into());
            }
            SettingsAction::InstallStickerRuntime => {
                self.capture_status =
                    match sticker_upscale::install_active_sticker_runtime(&self.settings) {
                        Ok(status) => status,
                        Err(error) => format!("Sticker local runtime install failed: {error}"),
                    };
            }
            SettingsAction::RemoveStickerRuntime => {
                self.capture_status =
                    match sticker_upscale::remove_active_sticker_runtime(&self.settings) {
                        Ok(status) => status,
                        Err(error) => format!("Sticker local runtime remove failed: {error}"),
                    };
            }
            SettingsAction::UpscaleProvider => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                settings.provider = next_upscale_provider(&settings.provider);
                self.persist_upscale_settings(settings, "Upscale provider updated".into());
            }
            SettingsAction::EditDeepAiApiKey => {
                self.start_settings_text_input(settings_text::SettingsTextTarget::DeepAi);
            }
            SettingsAction::UpscaleLocalEngine => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                let next = next_upscale_local_engine(&settings.local_engine);
                set_active_upscale_engine(&mut settings, next);
                self.persist_upscale_settings(settings, "Upscale local model updated".into());
            }
            SettingsAction::UpscaleExecutionProvider => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                settings.local_execution_provider =
                    next_execution_provider(&settings.local_execution_provider);
                self.persist_upscale_settings(settings, "Upscale local execution updated".into());
            }
            SettingsAction::UpscaleScale => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                settings.scale_factor = next_upscale_scale_factor(settings.scale_factor);
                self.persist_upscale_settings(settings, "Upscale scale updated".into());
            }
            SettingsAction::ToggleUpscalePreview => {
                let mut settings = UpscaleSettings::from_json_value(
                    self.settings.upscale_upload_settings.as_ref(),
                );
                settings.show_preview_window = !settings.show_preview_window;
                self.persist_upscale_settings(settings, "Upscale preview updated".into());
            }
            SettingsAction::InstallUpscaleRuntime => {
                self.capture_status =
                    match sticker_upscale::install_active_upscale_runtime(&self.settings) {
                        Ok(status) => status,
                        Err(error) => format!("Upscale local runtime install failed: {error}"),
                    };
            }
            SettingsAction::RemoveUpscaleRuntime => {
                self.capture_status =
                    match sticker_upscale::remove_active_upscale_runtime(&self.settings) {
                        Ok(status) => status,
                        Err(error) => format!("Upscale local runtime remove failed: {error}"),
                    };
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

    fn persist_lifecycle_settings(&mut self, message: String) {
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}. {}", lifecycle_settings_summary(&self.settings)),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn persist_image_search_settings(&mut self, message: String) {
        self.image_search.refresh_status();
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}."),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn persist_translation_settings(&mut self, message: String) {
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!(
                "{message}. {}",
                ocr_translation::translation_runtime_status_summary(&self.settings)
            ),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn persist_sticker_settings(&mut self, settings: StickerSettings, message: String) {
        self.settings.sticker_upload_settings = Some(settings.to_json_value());
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}: {}.", sticker_settings_summary(&settings)),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn persist_upscale_settings(&mut self, settings: UpscaleSettings, message: String) {
        self.settings.upscale_upload_settings = Some(settings.to_json_value());
        self.capture_status = match self.settings_store.save(&self.settings) {
            Ok(()) => format!("{message}: {}.", upscale_settings_summary(&settings)),
            Err(error) => format!("Settings save failed: {error}"),
        };
    }

    fn hydrate_image_search_ocr(&mut self) {
        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.hydrate_image_search_ocr_with_adapter(&adapter)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.hydrate_image_search_ocr_with_adapter(&adapter)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.hydrate_image_search_ocr_with_adapter(&adapter)
        };

        self.capture_status = match result {
            Ok(summary) => image_search_ocr_hydration_status(&summary),
            Err(error) => format!("Image OCR indexing failed: {error}"),
        };
    }

    fn hydrate_image_search_ocr_with_adapter<T>(
        &mut self,
        adapter: &T,
    ) -> Result<ImageSearchOcrHydrationSummary, String>
    where
        T: OcrTextService,
    {
        self.hydrate_image_search_ocr_batch_with_adapter(adapter, usize::MAX)
    }

    fn start_image_search_reindex_queue(&mut self) {
        if self.image_search_reindex_queue.active {
            self.capture_status = self.image_search_reindex_queue.detail_text();
            return;
        }

        match self.image_search_index_store.load_or_default() {
            Ok(index) => {
                let total = image_search_reindex_pending_count(&index, app_unix_millis_now());
                self.image_search_reindex_queue = ImageSearchReindexQueueState::start(total);
                self.capture_status = if total == 0 {
                    "Image OCR reindex queue already current.".into()
                } else {
                    format!("Image OCR reindex queue started with {total} records.")
                };
                self.image_search_index_status = image_search_index_status(&index);
            }
            Err(error) => {
                self.image_search_reindex_queue = ImageSearchReindexQueueState::default();
                self.capture_status = format!("Image OCR reindex queue failed: {error}");
            }
        }
    }

    fn cancel_image_search_reindex_queue(&mut self) {
        if self.image_search_reindex_queue.active {
            self.image_search_reindex_queue.request_cancel();
            self.capture_status = "Image OCR reindex will cancel before the next record.".into();
        } else {
            self.capture_status = "Image OCR reindex queue is not running.".into();
        }
    }

    fn hydrate_image_search_ocr_batch_with_adapter<T>(
        &mut self,
        adapter: &T,
        max_attempts: usize,
    ) -> Result<ImageSearchOcrHydrationSummary, String>
    where
        T: OcrTextService,
    {
        let mut index = self
            .image_search_index_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        let now = app_unix_millis_now();
        let mut summary = ImageSearchOcrHydrationSummary::default();

        for record in &mut index.records {
            if summary.attempted >= max_attempts {
                break;
            }
            hydrate_image_search_record_ocr(
                record,
                adapter,
                &self.settings.ocr_language_tag,
                now,
                &mut summary,
            );
        }

        self.image_search_index_store
            .save(&index)
            .map_err(|error| error.to_string())?;
        self.image_search_index_status = image_search_index_status(&index);

        let history_index = self
            .history_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        self.refresh_capture_history(history_index);
        Ok(summary)
    }

    fn hydrate_image_search_reindex_queue_tick(&mut self) -> Option<String> {
        if !self.image_search_reindex_queue.active {
            return None;
        }
        if self.image_search_reindex_queue.cancel_requested {
            self.image_search_reindex_queue.finish();
            return Some(format!(
                "Image OCR reindex canceled. {}",
                self.image_search_reindex_queue.detail_text()
            ));
        }

        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.hydrate_image_search_reindex_queue_tick_with_adapter(&adapter)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.hydrate_image_search_reindex_queue_tick_with_adapter(&adapter)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.hydrate_image_search_reindex_queue_tick_with_adapter(&adapter)
        };

        Some(match result {
            Ok(status) => status,
            Err(error) => {
                self.image_search_reindex_queue.finish();
                format!("Image OCR reindex failed: {error}")
            }
        })
    }

    fn hydrate_image_search_reindex_queue_tick_with_adapter<T>(
        &mut self,
        adapter: &T,
    ) -> Result<String, String>
    where
        T: OcrTextService,
    {
        let mut index = self
            .image_search_index_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        let now = app_unix_millis_now();
        let Some(record) = index
            .records
            .iter_mut()
            .find(|record| image_search_record_needs_ocr(record, now))
        else {
            self.image_search_reindex_queue.finish();
            return Ok(format!(
                "Image OCR reindex complete. {}",
                self.image_search_reindex_queue.detail_text()
            ));
        };

        self.image_search_reindex_queue.current_file = Some(file_name_for_path(&record.file_path));
        let mut summary = ImageSearchOcrHydrationSummary::default();
        hydrate_image_search_record_ocr(
            record,
            adapter,
            &self.settings.ocr_language_tag,
            now,
            &mut summary,
        );
        self.image_search_reindex_queue.apply_summary(&summary);

        self.image_search_index_store
            .save(&index)
            .map_err(|error| error.to_string())?;
        self.image_search_index_status = image_search_index_status(&index);

        let history_index = self
            .history_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        self.refresh_capture_history(history_index);

        let remaining = image_search_reindex_pending_count(&index, app_unix_millis_now());
        if remaining == 0
            || self.image_search_reindex_queue.processed >= self.image_search_reindex_queue.total
        {
            self.image_search_reindex_queue.finish();
            Ok(format!(
                "Image OCR reindex complete. {}",
                self.image_search_reindex_queue.detail_text()
            ))
        } else {
            Ok(self.image_search_reindex_queue.detail_text())
        }
    }

    fn hydrate_image_search_ocr_background_tick(&mut self) -> Option<String> {
        if !self.settings.auto_index_images {
            return None;
        }

        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.hydrate_image_search_ocr_batch_with_adapter(&adapter, 1)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.hydrate_image_search_ocr_batch_with_adapter(&adapter, 1)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.hydrate_image_search_ocr_batch_with_adapter(&adapter, 1)
        };

        match result {
            Ok(summary) if summary.attempted > 0 => Some(format!(
                "Image OCR background refresh: {} indexed, {} empty, {} failed.",
                summary.indexed, summary.empty, summary.failed
            )),
            Ok(_) => None,
            Err(error) => Some(format!("Image OCR background refresh failed: {error}")),
        }
    }

    fn image_search_ocr_maintenance_tick(&mut self) -> Option<String> {
        if self.image_search_reindex_queue.active {
            return self.hydrate_image_search_reindex_queue_tick();
        }

        let now = app_unix_millis_now();
        if now.saturating_sub(self.last_auto_image_search_ocr_unix_ms) < 5_000 {
            return None;
        }
        self.last_auto_image_search_ocr_unix_ms = now;
        self.hydrate_image_search_ocr_background_tick()
    }

    fn auto_hydrate_image_search_ocr_for_path(&mut self, path: &Path) -> String {
        if !self.settings.auto_index_images {
            return String::new();
        }

        #[cfg(target_os = "windows")]
        let result = {
            let adapter = oddsnap_platform_windows::WindowsPlatform;
            self.hydrate_image_search_ocr_path_with_adapter(&adapter, path)
        };

        #[cfg(target_os = "macos")]
        let result = {
            let adapter = oddsnap_platform_macos::MacosPlatform;
            self.hydrate_image_search_ocr_path_with_adapter(&adapter, path)
        };

        #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
        let result = {
            let adapter = oddsnap_platform_linux::LinuxPlatform;
            self.hydrate_image_search_ocr_path_with_adapter(&adapter, path)
        };

        match result {
            Ok(summary) => image_search_auto_ocr_status(&summary),
            Err(error) => format!("; image OCR index failed: {error}"),
        }
    }

    fn hydrate_image_search_ocr_path_with_adapter<T>(
        &mut self,
        adapter: &T,
        path: &Path,
    ) -> Result<ImageSearchOcrHydrationSummary, String>
    where
        T: OcrTextService,
    {
        let mut index = self
            .image_search_index_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        let now = app_unix_millis_now();
        let mut summary = ImageSearchOcrHydrationSummary::default();

        if let Some(record) = index
            .records
            .iter_mut()
            .find(|record| record.file_path == path)
        {
            hydrate_image_search_record_ocr(
                record,
                adapter,
                &self.settings.ocr_language_tag,
                now,
                &mut summary,
            );
        }

        if summary.attempted == 0 {
            return Ok(summary);
        }

        self.image_search_index_store
            .save(&index)
            .map_err(|error| error.to_string())?;
        self.image_search_index_status = image_search_index_status(&index);

        let history_index = self
            .history_store
            .load_or_default()
            .map_err(|error| error.to_string())?;
        self.refresh_capture_history(history_index);
        Ok(summary)
    }

    fn open_history_path(&mut self, path: PathBuf) {
        self.capture_status = match reveal_history_path(&path) {
            Ok(action) => format!("{action} {}.", path.display()),
            Err(error) => format!("Open failed: {error}"),
        };
    }

    fn open_history_file(&mut self, path: PathBuf) {
        self.capture_status = match open_file_path(&path) {
            Ok(()) => format!("Opened {}.", path.display()),
            Err(error) => format!("Open file failed: {error}"),
        };
    }

    fn copy_history_upload_url(&mut self, url: String) {
        let result = copy_text_to_host_clipboard(&url);

        self.capture_status = match result {
            Ok(()) => "Upload link copied.".into(),
            Err(error) => format!("Copy upload link failed: {error}"),
        };
    }

    fn open_history_upload_url(&mut self, url: String) {
        self.capture_status = match open_external_url(&url) {
            Ok(()) => "Upload link opened.".into(),
            Err(error) => format!("Open upload link failed: {error}"),
        };
    }

    fn retry_history_upload(&mut self, path: PathBuf, kind: HistoryKind) {
        if !path.exists() {
            self.capture_status =
                format!("Upload retry failed: file not found: {}", path.display());
            return;
        }

        let upload = self.explicit_upload_metadata(kind, &path, false);
        let history_status = self.update_history_upload_metadata(&path, &upload);
        self.capture_status = if let Some(url) = upload.url {
            let provider = upload.provider.as_deref().unwrap_or("Upload");
            format!("Uploaded to {provider}: {url}.{history_status}")
        } else if let Some(error) = upload.error {
            format!("Upload retry failed: {error}.{history_status}")
        } else {
            format!("Upload retry needs a destination in Settings -> Uploads.{history_status}")
        };
    }

    fn retry_filtered_history_uploads(&mut self) {
        let entries = filtered_capture_history(
            &self.capture_history,
            self.history_kind_filter,
            self.history_upload_filter,
        );
        if entries.is_empty() {
            self.capture_status = "No filtered history entries to upload.".into();
            return;
        }

        let total = entries.len();
        let mut uploaded = 0usize;
        let mut failed = 0usize;
        let mut missing = 0usize;
        for entry in entries {
            let path = PathBuf::from(&entry.path);
            if !path.exists() {
                missing += 1;
                continue;
            }

            let upload = self.explicit_upload_metadata(entry.kind, &path, false);
            if upload.url.is_some() {
                uploaded += 1;
            } else {
                failed += 1;
            }
            let _ = self.update_history_upload_metadata(&path, &upload);
        }

        self.capture_status = format!(
            "Filtered upload retry done: {uploaded}/{total} uploaded, {failed} failed, {missing} missing."
        );
    }

    fn copy_filtered_history_upload_links(&mut self) {
        let entries = filtered_capture_history(
            &self.capture_history,
            self.history_kind_filter,
            self.history_upload_filter,
        );
        let Some(text) = filtered_upload_links_text(&entries) else {
            self.capture_status = "No upload links in filtered history.".into();
            return;
        };
        let link_count = text.lines().count();
        self.capture_status = match copy_text_to_host_clipboard(&text) {
            Ok(()) => format!("{link_count} upload links copied."),
            Err(error) => format!("Copy filtered upload links failed: {error}"),
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

        let Some(path) = newest_history_image_path(&self.capture_history) else {
            self.capture_status =
                "AI Redirect needs a saved image capture in Rust history first.".into();
            return;
        };

        if provider == AiChatProvider::GoogleLens {
            let upload = self.upload_metadata(HistoryKind::Image, &path, true);
            let history_status = self.update_history_upload_metadata(&path, &upload);
            if let Some(error) = upload.error {
                self.capture_status =
                    format!("Google Lens upload failed: {error}.{history_status}");
                return;
            }

            let Some(upload_url) = upload.url else {
                self.capture_status = format!(
                    "Google Lens AI Redirect needs an upload destination in Settings -> Uploads.{history_status}"
                );
                return;
            };

            let lens_url = match oddsnap_core::build_google_lens_url(&upload_url) {
                Ok(url) => url,
                Err(error) => {
                    self.capture_status = format!("Google Lens URL build failed: {error}");
                    return;
                }
            };

            self.capture_status = match open_external_url(&lens_url) {
                Ok(()) => {
                    format!("Google Lens opened for uploaded image: {upload_url}.{history_status}")
                }
                Err(error) => format!("Google Lens open failed: {error}.{history_status}"),
            };
            return;
        }

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
                let _ = self
                    .image_search_index_store
                    .remove_record(Path::new(&path));
                self.refresh_capture_history(index);
                format!("Removed {path} from history.")
            }
            Err(error) => format!("Remove from history failed: {error}"),
        };
    }

    fn remove_filtered_history_entries(&mut self) {
        let paths = filtered_capture_history(
            &self.capture_history,
            self.history_kind_filter,
            self.history_upload_filter,
        )
        .into_iter()
        .map(|entry| PathBuf::from(entry.path))
        .collect::<Vec<_>>();

        if paths.is_empty() {
            self.capture_status = "No filtered history entries to remove.".into();
            return;
        }

        self.capture_status = match self.history_store.remove_entries(paths.iter()) {
            Ok(index) => {
                for path in &paths {
                    let _ = self.image_search_index_store.remove_record(path);
                }
                let removed_count = paths.len();
                self.refresh_capture_history(index);
                format!("Removed {removed_count} filtered history entries.")
            }
            Err(error) => format!("Remove filtered history failed: {error}"),
        };
    }

    fn copy_color_history_hex(&mut self, hex: String) {
        let result = copy_text_to_host_clipboard(&hex);

        self.capture_status = match result {
            Ok(()) => format!("Color {hex} copied."),
            Err(error) => format!("Copy color failed: {error}"),
        };
    }

    fn remove_color_history_entry(&mut self, hex: String, captured_at_unix_ms: u64) {
        self.capture_status = match self
            .history_store
            .remove_color_entry(&hex, captured_at_unix_ms)
        {
            Ok(index) => {
                self.color_history = history_entries_to_color_history(index);
                format!("Removed color #{hex} from history.")
            }
            Err(error) => format!("Remove color failed: {error}"),
        };
    }

    fn copy_ocr_history_text(&mut self, text: String) {
        let result = copy_text_to_host_clipboard(&text);

        self.capture_status = match result {
            Ok(()) => "OCR text copied.".into(),
            Err(error) => format!("Copy OCR text failed: {error}"),
        };
    }

    fn remove_ocr_history_entry(&mut self, text: String, captured_at_unix_ms: u64) {
        self.capture_status = match self
            .history_store
            .remove_ocr_entry(&text, captured_at_unix_ms)
        {
            Ok(index) => {
                let entries = history_entries_to_ocr_history(index);
                self.latest_ocr_result = entries.first().map(|entry| entry.text.clone());
                self.ocr_history = entries;
                "Removed OCR text from history.".into()
            }
            Err(error) => format!("Remove OCR text failed: {error}"),
        };
    }

    fn open_ocr_result_window(&mut self, text: String, cx: &mut Context<Self>) {
        let bounds = Bounds::centered(None, size(px(540.0), px(460.0)), cx);
        let result = cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                titlebar: Some(TitlebarOptions {
                    title: Some("OddSnap OCR Result".into()),
                    appears_transparent: true,
                    traffic_light_position: None,
                }),
                window_background: WindowBackgroundAppearance::Transparent,
                window_decorations: Some(WindowDecorations::Client),
                app_id: Some("dev.jasper.oddsnap.rust.ocr-result".into()),
                window_min_size: Some(size(px(360.0), px(260.0))),
                ..Default::default()
            },
            |_, cx| cx.new(|_| OcrResultWindow::new(text)),
        );

        self.capture_status = match result {
            Ok(_) => "OCR result opened.".into(),
            Err(error) => format!("OCR result window failed: {error}"),
        };
    }

    fn open_processed_result_preview(
        &mut self,
        tool: ProcessedCaptureTool,
        provider_name: &str,
        result: &CaptureRunResult,
        cx: &mut Context<Self>,
    ) -> String {
        if !processed_capture_preview_enabled(tool, &self.settings) {
            return String::new();
        }

        let bounds = Bounds::centered(None, size(px(860.0), px(560.0)), cx);
        match cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                titlebar: Some(TitlebarOptions {
                    title: Some(format!("OddSnap {} Preview", tool.label()).into()),
                    appears_transparent: true,
                    traffic_light_position: None,
                }),
                window_background: WindowBackgroundAppearance::Transparent,
                window_decorations: Some(WindowDecorations::Client),
                app_id: Some(format!("dev.jasper.oddsnap.rust.{}-preview", tool.slug())),
                window_min_size: Some(size(px(520.0), px(340.0))),
                ..Default::default()
            },
            |_, cx| {
                cx.new(|_| {
                    ProcessedResultPreviewWindow::new(
                        tool,
                        provider_name.to_string(),
                        result.original_preview_path.clone(),
                        result.capture.image_path.clone(),
                    )
                })
            },
        ) {
            Ok(_) => "; preview opened".into(),
            Err(error) => {
                let _ = result.original_preview_path.as_ref().map(fs::remove_file);
                format!("; preview failed: {error}")
            }
        }
    }

    fn copy_code_history_text(&mut self, text: String) {
        let result = copy_text_to_host_clipboard(&text);

        self.capture_status = match result {
            Ok(()) => "QR/barcode text copied.".into(),
            Err(error) => format!("Copy QR/barcode text failed: {error}"),
        };
    }

    fn remove_code_history_entry(
        &mut self,
        text: String,
        format: String,
        captured_at_unix_ms: u64,
    ) {
        self.capture_status =
            match self
                .history_store
                .remove_code_entry(&text, &format, captured_at_unix_ms)
            {
                Ok(index) => {
                    let entries = history_entries_to_code_history(index);
                    self.latest_scan_result = entries.first().cloned();
                    self.code_history = entries;
                    "Removed QR/barcode scan from history.".into()
                }
                Err(error) => format!("Remove QR/barcode scan failed: {error}"),
            };
    }

    fn translate_ocr_history_text(&mut self, text: String) {
        self.capture_status = match ocr_translation::translate_ocr_text(&text, &self.settings) {
            Ok(translated) => match copy_text_to_host_clipboard(&translated.text) {
                Ok(()) => format!(
                    "Translated OCR text {}->{} with {} and copied it.",
                    translated.source,
                    translated.target,
                    translated.model.label()
                ),
                Err(error) => format!(
                    "Translated OCR text {}->{} with {}; clipboard copy failed: {error}",
                    translated.source,
                    translated.target,
                    translated.model.label()
                ),
            },
            Err(error) => format!("OCR translation failed: {error}"),
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
                    app.handle_hotkey_event(event, cx);
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
                    app.handle_hotkey_event(event, cx);
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

    #[cfg(all(
        not(target_os = "windows"),
        not(target_os = "macos"),
        not(target_os = "linux")
    ))]
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

    #[cfg(target_os = "linux")]
    fn start_tray_event_pump(
        &self,
        receiver: Option<std::sync::mpsc::Receiver<oddsnap_platform_linux::LinuxTrayEvent>>,
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

    fn start_image_search_ocr_background_pump(&self, cx: &mut Context<Self>) {
        cx.spawn(async move |this, cx| loop {
            cx.background_executor()
                .timer(std::time::Duration::from_secs(1))
                .await;

            let _ = this.update(cx, |app, cx| {
                if let Some(status) = app.image_search_ocr_maintenance_tick() {
                    app.image_search_index_status = status;
                    cx.notify();
                }
            });
        })
        .detach();
    }

    #[cfg(target_os = "windows")]
    fn handle_hotkey_event(
        &mut self,
        event: oddsnap_platform_windows::WindowsHotkeyEvent,
        cx: &mut Context<Self>,
    ) {
        match event {
            oddsnap_platform_windows::WindowsHotkeyEvent::Capture => {
                self.run_default_capture_command("Capture hotkey", cx);
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
                self.run_ocr_capture("OCR hotkey");
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Scan => {
                self.run_scan_capture("Scan hotkey");
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Sticker => {
                self.run_sticker_capture("Sticker hotkey");
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Upscale => {
                self.run_upscale_capture("Upscale hotkey", cx);
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Center => {
                self.run_center_capture("Center hotkey");
            }
            oddsnap_platform_windows::WindowsHotkeyEvent::Ruler => {
                self.run_ruler_measure("Ruler hotkey");
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
    fn handle_hotkey_event(&mut self, event: CrossPlatformHotkeyEvent, cx: &mut Context<Self>) {
        match event {
            CrossPlatformHotkeyEvent::Capture => {
                self.run_default_capture_command("Capture hotkey", cx);
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
                self.run_ocr_capture("OCR hotkey");
            }
            CrossPlatformHotkeyEvent::Scan => {
                self.run_scan_capture("Scan hotkey");
            }
            CrossPlatformHotkeyEvent::Sticker => {
                self.run_sticker_capture("Sticker hotkey");
            }
            CrossPlatformHotkeyEvent::Upscale => {
                self.run_upscale_capture("Upscale hotkey", cx);
            }
            CrossPlatformHotkeyEvent::Center => {
                self.run_center_capture("Center hotkey");
            }
            CrossPlatformHotkeyEvent::Ruler => {
                self.run_ruler_measure("Ruler hotkey");
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
                    self.run_default_capture_command("Tray capture", cx);
                }
            }
            oddsnap_platform_windows::WindowsTrayEvent::ToggleRecording => {
                self.recording_status = "Tray recording command received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_windows::WindowsTrayEvent::Ocr => {
                self.run_ocr_capture("Tray text capture");
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
                    self.run_default_capture_command("Menu bar capture", cx);
                }
            }
            oddsnap_platform_macos::MacosTrayEvent::ToggleRecording => {
                self.recording_status = "Menu bar recording command received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_macos::MacosTrayEvent::Ocr => {
                self.run_ocr_capture("Menu bar text capture");
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

    #[cfg(target_os = "linux")]
    fn handle_tray_event(
        &mut self,
        event: oddsnap_platform_linux::LinuxTrayEvent,
        cx: &mut Context<Self>,
    ) {
        match event {
            oddsnap_platform_linux::LinuxTrayEvent::Capture => {
                if self.active_recording.is_some() {
                    self.recording_status = "Tray stop recording received.".into();
                    self.stop_recording();
                } else {
                    self.run_default_capture_command("Tray capture", cx);
                }
            }
            oddsnap_platform_linux::LinuxTrayEvent::ToggleRecording => {
                self.recording_status = "Tray recording command received.".into();
                self.toggle_recording(RecordingTarget::FullScreen);
            }
            oddsnap_platform_linux::LinuxTrayEvent::Ocr => {
                self.run_ocr_capture("Tray text capture");
            }
            oddsnap_platform_linux::LinuxTrayEvent::ColorPicker => {
                self.capture_status = "Tray color picker received.".into();
                self.run_color_picker();
            }
            oddsnap_platform_linux::LinuxTrayEvent::ScrollCapture => {
                self.capture_status =
                    pending_tool_trigger_status("Tray scroll capture", PendingTool::ScrollCapture);
            }
            oddsnap_platform_linux::LinuxTrayEvent::Settings => {
                self.tray_status = "Tray settings command focused the Rust window.".into();
                cx.activate(true);
            }
            oddsnap_platform_linux::LinuxTrayEvent::History => {
                self.tray_status = "Tray history command focused recent captures.".into();
                cx.activate(true);
            }
            oddsnap_platform_linux::LinuxTrayEvent::Quit => {
                self.tray_status = "Tray quit requested.".into();
                cx.quit();
            }
        }
    }

    fn capture_output_directory(&self) -> std::path::PathBuf {
        self.settings
            .capture_output_directory_or(default_capture_directory())
    }

    fn capture_destination(&self, capture: &oddsnap_platform::CaptureResult) -> std::path::PathBuf {
        self.capture_destination_with_extension(
            capture,
            self.settings.capture_image_format.extension(),
        )
    }

    fn capture_destination_with_extension(
        &self,
        capture: &oddsnap_platform::CaptureResult,
        extension: &str,
    ) -> std::path::PathBuf {
        let stem = format_file_name_template(
            &self.settings.file_name_template,
            capture.region.width,
            capture.region.height,
        );
        let file_name = format!("{stem}.{extension}");
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
        #[cfg(target_os = "linux")]
        {
            if let Some(tray_icon) = &self.tray_icon {
                if let Err(error) = tray_icon.set_recording_state(self.active_recording.is_some()) {
                    self.tray_status = format!("Tray recording state update failed: {error}");
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
                    target: start.target,
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
        T: ScreenCaptureService
            + WindowPickerService
            + VideoRecordingService
            + RegionSelectionService,
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
            RecordingTarget::Region => select_recording_region(&adapter, &self.settings)?,
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
                let history_status = self.save_recording_history(
                    result.output_path,
                    active.width,
                    active.height,
                    active.target,
                );
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

        let (microphone, desktop_audio) = recording_audio_status_summary(&self.settings);
        format!("{base} · {microphone} · {desktop_audio}")
    }

    fn capture_preferences_summary(&self) -> String {
        format!(
            "Capture prefs: default {} · delay {}s · cursor {} · crosshair {} · magnifier {} · sounds {} · toasts {} {:.1}s fade {} · UI scale {:.2}x",
            self.settings.default_capture_mode.label(),
            self.settings.capture_delay_seconds,
            on_off(self.settings.show_cursor),
            on_off(self.settings.show_crosshair_guides),
            on_off(self.settings.show_capture_magnifier),
            if self.settings.mute_sounds { "muted" } else { "on" },
            self.settings.toast_position.label(),
            self.settings.toast_duration_seconds,
            on_off(self.settings.toast_fade_out_enabled),
            self.settings.ui_scale
        )
    }

    fn advanced_settings_summary(&self) -> String {
        advanced_settings_summary_text(&self.settings)
    }

    fn upload_metadata(
        &self,
        kind: HistoryKind,
        path: &Path,
        use_ai_redirect: bool,
    ) -> UploadMetadata {
        self.upload_metadata_from_preflight(
            oddsnap_core::upload_preflight_for_media(&self.settings, kind, path, use_ai_redirect),
            path,
        )
    }

    fn explicit_upload_metadata(
        &self,
        kind: HistoryKind,
        path: &Path,
        use_ai_redirect: bool,
    ) -> UploadMetadata {
        self.upload_metadata_from_preflight(
            oddsnap_core::upload_preflight_for_explicit_media(
                &self.settings,
                kind,
                path,
                use_ai_redirect,
            ),
            path,
        )
    }

    fn upload_metadata_from_preflight(
        &self,
        preflight: UploadPreflight,
        path: &Path,
    ) -> UploadMetadata {
        match preflight {
            UploadPreflight::Disabled => UploadMetadata::default(),
            UploadPreflight::Ready {
                destination,
                provider_name,
            } => self.run_upload(destination, provider_name, path),
            UploadPreflight::Blocked {
                provider_name,
                error,
                ..
            } => UploadMetadata {
                provider: Some(provider_name),
                error: Some(error),
                ..UploadMetadata::default()
            },
        }
    }

    fn run_upload(
        &self,
        destination: UploadDestination,
        provider_name: String,
        path: &Path,
    ) -> UploadMetadata {
        if destination == UploadDestination::TempHosts {
            return self.run_temporary_host_upload(path);
        }

        if !destination.curl_upload_supported() {
            return UploadMetadata {
                provider: Some(provider_name.clone()),
                error: Some(format!(
                    "pending: Rust upload backend for {provider_name} is pending; upload was not attempted."
                )),
                ..UploadMetadata::default()
            };
        }

        let upload_settings =
            UploadSettings::from_json_value(self.settings.image_upload_settings.as_ref());
        match run_curl_upload(destination, path, &upload_settings) {
            Ok(success) => UploadMetadata {
                url: Some(success.url),
                provider: Some(success.provider_name),
                error: None,
            },
            Err(error) => UploadMetadata {
                provider: Some(provider_name),
                error: Some(error),
                ..UploadMetadata::default()
            },
        }
    }

    fn run_temporary_host_upload(&self, path: &Path) -> UploadMetadata {
        let mut errors = Vec::new();
        let upload_settings =
            UploadSettings::from_json_value(self.settings.image_upload_settings.as_ref());
        for destination in oddsnap_core::temporary_host_fallbacks() {
            match run_curl_upload(destination.clone(), path, &upload_settings) {
                Ok(success) => {
                    return UploadMetadata {
                        url: Some(success.url),
                        provider: Some(success.provider_name),
                        error: None,
                    };
                }
                Err(error) => {
                    errors.push(format!("{}: {error}", destination.display_name()));
                }
            }
        }

        UploadMetadata {
            provider: Some(UploadDestination::TempHosts.display_name()),
            error: Some(errors.join(" | ")),
            ..UploadMetadata::default()
        }
    }

    fn update_history_upload_metadata(&mut self, path: &Path, upload: &UploadMetadata) -> String {
        let path_text = path.display().to_string();
        if let Some(entry) = self
            .capture_history
            .iter_mut()
            .find(|entry| entry.path == path_text)
        {
            entry.upload_url = upload.url.clone();
            entry.upload_provider = upload.provider.clone();
            entry.upload_error = upload.error.clone();
        }

        if !self.settings.save_history {
            return String::new();
        }

        match self.history_store.update_entry_upload(
            path,
            upload.url.clone(),
            upload.provider.clone(),
            upload.error.clone(),
        ) {
            Ok(index) => {
                self.refresh_capture_history(index);
                String::new()
            }
            Err(error) => format!(" History upload metadata update failed: {error}"),
        }
    }

    fn refresh_capture_history(&mut self, history_index: HistoryIndex) {
        let image_search_index = self
            .image_search_index_store
            .load_or_default()
            .unwrap_or_default();
        self.capture_history =
            history_entries_to_capture_history(history_index, &image_search_index);
    }

    fn sync_pending_image_search_entry(&mut self, entry: &HistoryEntry) -> String {
        if !self.settings.auto_index_images || !history_entry_can_be_image_indexed(entry) {
            return String::new();
        }

        match pending_image_search_record_from_history_entry(
            entry,
            &self.settings.ocr_language_tag,
            "rust-ocr-pending",
        )
        .and_then(|record| self.image_search_index_store.upsert_record(record))
        {
            Ok(index) => {
                self.image_search_index_status = image_search_index_status(&index);
                String::new()
            }
            Err(error) => {
                self.image_search_index_status = format!("Image index update failed: {error}");
                format!("; image index failed: {error}")
            }
        }
    }

    fn save_recording_history(
        &mut self,
        path: PathBuf,
        width: u32,
        height: u32,
        target: RecordingTarget,
    ) -> String {
        if !self.settings.save_history {
            let preview_path = create_video_thumbnail(&self.history_store, &path)
                .or_else(|| preview_path_for_capture(&path));
            let kind = recording_history_kind(self.settings.recording_format);
            let upload = self.upload_metadata(kind, &path, false);
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode: target.capture_mode(),
                    kind,
                    path: path.display().to_string(),
                    file_name: file_name_for_path(&path),
                    preview_path,
                    width,
                    height,
                    captured_at_unix_ms: 0,
                    image_search_ocr_text: String::new(),
                    image_search_record: None,
                    upload_url: upload.url,
                    upload_provider: upload.provider,
                    upload_error: upload.error,
                },
            );
            self.capture_history.truncate(6);
            return String::new();
        }

        let kind = recording_history_kind(self.settings.recording_format);
        let thumbnail_path = create_video_thumbnail(&self.history_store, &path);
        let mut entry = match HistoryEntry::from_capture_file(path, width, height, kind) {
            Ok(entry) => entry,
            Err(error) => return format!("; history failed: {error}"),
        };
        entry.thumbnail_path = thumbnail_path;
        let upload = self.upload_metadata(kind, &entry.file_path, false);
        entry.upload_url = upload.url;
        entry.upload_provider = upload.provider;
        entry.upload_error = upload.error;
        let indexed_entry = entry.clone();

        match self.history_store.append_entry(entry) {
            Ok(index) => {
                let image_search_status = self.sync_pending_image_search_entry(&indexed_entry);
                let image_search_ocr_status =
                    self.auto_hydrate_image_search_ocr_for_path(&indexed_entry.file_path);
                self.refresh_capture_history(index);
                format!(
                    "{}{}{}",
                    upload_history_status(self.capture_history.first()),
                    image_search_status,
                    image_search_ocr_status
                )
            }
            Err(error) => format!("; history failed: {error}"),
        }
    }

    fn save_capture_history(
        &mut self,
        capture: &oddsnap_platform::CaptureResult,
        mode: CaptureMode,
    ) -> String {
        self.save_capture_history_with_kind(capture, mode, HistoryKind::Image)
    }

    fn save_capture_history_with_kind(
        &mut self,
        capture: &oddsnap_platform::CaptureResult,
        mode: CaptureMode,
        kind: HistoryKind,
    ) -> String {
        if !self.settings.save_history {
            let upload = self.upload_metadata(kind, &capture.image_path, false);
            self.capture_history.insert(
                0,
                CaptureHistoryEntry {
                    mode,
                    kind,
                    path: capture.image_path.display().to_string(),
                    file_name: file_name_for_path(&capture.image_path),
                    preview_path: preview_path_for_capture(&capture.image_path),
                    width: capture.region.width,
                    height: capture.region.height,
                    captured_at_unix_ms: 0,
                    image_search_ocr_text: String::new(),
                    image_search_record: None,
                    upload_url: upload.url,
                    upload_provider: upload.provider,
                    upload_error: upload.error,
                },
            );
            self.capture_history.truncate(6);
            return String::new();
        }

        let mut entry = match HistoryEntry::from_capture_file(
            capture.image_path.clone(),
            capture.region.width,
            capture.region.height,
            kind,
        ) {
            Ok(entry) => entry,
            Err(error) => return format!("; history failed: {error}"),
        };
        let upload = self.upload_metadata(kind, &entry.file_path, false);
        entry.upload_url = upload.url;
        entry.upload_provider = upload.provider;
        entry.upload_error = upload.error;
        let indexed_entry = entry.clone();

        match self.history_store.append_entry(entry) {
            Ok(index) => {
                let image_search_status = self.sync_pending_image_search_entry(&indexed_entry);
                self.refresh_capture_history(index);
                format!(
                    "{}{}",
                    upload_history_status(self.capture_history.first()),
                    image_search_status
                )
            }
            Err(error) => format!("; history failed: {error}"),
        }
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

#[cfg(target_os = "linux")]
fn start_tray_icon() -> (
    String,
    Option<oddsnap_platform_linux::LinuxTrayIcon>,
    Option<std::sync::mpsc::Receiver<oddsnap_platform_linux::LinuxTrayEvent>>,
) {
    let (sender, receiver) = std::sync::mpsc::channel();
    match oddsnap_platform_linux::start_oddsnap_tray_icon(sender) {
        Ok(tray_icon) => (
            LINUX_TRAY_FOUNDATION_STATUS.into(),
            Some(tray_icon),
            Some(receiver),
        ),
        Err(error) => (format!("Tray unavailable: {error}"), None, None),
    }
}

#[cfg(all(
    not(target_os = "windows"),
    not(target_os = "macos"),
    not(target_os = "linux")
))]
fn start_tray_icon() -> (String, Option<()>, Option<()>) {
    ("Tray: pending on this platform.".into(), None, None)
}

fn history_entries_to_capture_history(
    index: HistoryIndex,
    image_search_index: &ImageSearchIndex,
) -> Vec<CaptureHistoryEntry> {
    index
        .entries
        .into_iter()
        .map(|entry| CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: entry.kind,
            path: entry.file_path.display().to_string(),
            file_name: entry.file_name,
            preview_path: entry
                .thumbnail_path
                .filter(|path| path.exists())
                .or_else(|| preview_path_for_capture(&entry.file_path)),
            width: entry.width,
            height: entry.height,
            captured_at_unix_ms: entry.captured_at_unix_ms,
            image_search_ocr_text: String::new(),
            image_search_record: image_search_index
                .records
                .iter()
                .find(|record| record.file_path == entry.file_path)
                .cloned(),
            upload_url: entry.upload_url,
            upload_provider: entry.upload_provider,
            upload_error: entry.upload_error,
        })
        .collect()
}

fn sync_image_search_index_records(
    store: &ImageSearchIndexStore,
    history_index: &HistoryIndex,
    settings: &AppSettings,
) -> (ImageSearchIndex, String) {
    let mut index = match store.load_or_default() {
        Ok(index) => index,
        Err(error) => {
            return (
                ImageSearchIndex::default(),
                format!("Image index load failed: {error}"),
            );
        }
    };

    if !settings.auto_index_images {
        return (index, "Image index: auto-index disabled.".into());
    }

    let indexed_paths = history_index
        .entries
        .iter()
        .filter(|entry| history_entry_can_be_image_indexed(entry))
        .map(|entry| entry.file_path.clone())
        .collect::<std::collections::HashSet<_>>();
    retain_indexed_image_paths(&mut index, &indexed_paths);

    let mut updated = 0usize;
    let mut failed = 0usize;
    for entry in history_index
        .entries
        .iter()
        .filter(|entry| history_entry_can_be_image_indexed(entry))
    {
        let current = index
            .records
            .iter()
            .find(|record| record.file_path == entry.file_path);
        if current
            .map(|record| image_search_record_matches_history_entry(record, entry))
            .unwrap_or(false)
        {
            continue;
        }

        match pending_image_search_record_from_history_entry(
            entry,
            &settings.ocr_language_tag,
            "rust-ocr-pending",
        ) {
            Ok(record) => {
                upsert_image_search_record(&mut index, record);
                updated += 1;
            }
            Err(_) => {
                failed += 1;
            }
        }
    }

    match store.save(&index) {
        Ok(()) => (
            index,
            image_search_index_status_with_updates(updated, failed),
        ),
        Err(error) => (
            index,
            format!("Image index save failed after sync: {error}"),
        ),
    }
}

fn image_search_index_status(index: &ImageSearchIndex) -> String {
    let pending = image_search_reindex_pending_count(index, app_unix_millis_now());
    let indexed = index
        .records
        .iter()
        .filter(|record| record.ocr_state == ImageSearchOcrState::Indexed)
        .count();
    let failed = index
        .records
        .iter()
        .filter(|record| record.ocr_state == ImageSearchOcrState::Failed)
        .count();
    image_search_index_status_with_counts(index.records.len(), pending, indexed, None, failed)
}

fn image_search_reindex_pending_count(index: &ImageSearchIndex, now_unix_ms: u64) -> usize {
    index
        .records
        .iter()
        .filter(|record| {
            record.ocr_state != ImageSearchOcrState::Failed
                && image_search_record_needs_ocr(record, now_unix_ms)
        })
        .count()
}

fn image_search_index_status_with_updates(updated: usize, failed: usize) -> String {
    image_search_index_status_with_counts(updated, 0, 0, Some(updated), failed)
}

fn image_search_index_status_with_counts(
    count: usize,
    pending: usize,
    indexed: usize,
    updated: Option<usize>,
    failed: usize,
) -> String {
    match (updated, failed) {
        (Some(updated), 0) if updated > 0 => {
            format!("Image index: {updated} pending records refreshed.")
        }
        (Some(0), 0) => "Image index: ready.".into(),
        (Some(updated), failed) => {
            format!("Image index: {updated} refreshed, {failed} failed.")
        }
        (None, _) => format!(
            "Image index: {count} records, {pending} pending, {indexed} OCR ready, {failed} failed."
        ),
    }
}

fn image_search_ocr_hydration_status(summary: &ImageSearchOcrHydrationSummary) -> String {
    if summary.attempted == 0 {
        return format!(
            "Image OCR index already current; {} records skipped.",
            summary.skipped
        );
    }

    format!(
        "Image OCR index refreshed: {} with text, {} empty, {} failed, {} skipped.",
        summary.indexed, summary.empty, summary.failed, summary.skipped
    )
}

fn image_search_auto_ocr_status(summary: &ImageSearchOcrHydrationSummary) -> String {
    match (
        summary.attempted,
        summary.indexed,
        summary.empty,
        summary.failed,
    ) {
        (0, _, _, _) => String::new(),
        (_, indexed, _, 0) if indexed > 0 => "; image OCR indexed".into(),
        (_, 0, empty, 0) if empty > 0 => "; image OCR found no text".into(),
        (_, _, _, failed) if failed > 0 => "; image OCR index failed".into(),
        _ => String::new(),
    }
}

fn hydrate_image_search_record_ocr<T>(
    record: &mut ImageSearchIndexRecord,
    adapter: &T,
    language_tag: &str,
    now: u64,
    summary: &mut ImageSearchOcrHydrationSummary,
) where
    T: OcrTextService,
{
    if !image_search_record_needs_ocr(record, now) {
        summary.skipped += 1;
        return;
    }

    summary.attempted += 1;
    record.ocr_language_tag = language_tag.to_string();
    if !record.file_path.exists() {
        apply_image_search_ocr_error(record, "indexed image file no longer exists", now);
        summary.failed += 1;
        return;
    }

    match adapter.recognize_text(OcrTextRequest {
        image_path: record.file_path.clone(),
        language_tag: record.ocr_language_tag.clone(),
    }) {
        Ok(result) => {
            let text = result.text.trim().to_string();
            if text.is_empty() {
                summary.empty += 1;
            } else {
                summary.indexed += 1;
            }
            apply_image_search_ocr_success(record, text, result.engine_id, now);
        }
        Err(error) => {
            apply_image_search_ocr_error(record, error.to_string(), now);
            summary.failed += 1;
        }
    }
}

fn file_name_for_path(path: &Path) -> String {
    path.file_name()
        .and_then(|name| name.to_str())
        .unwrap_or_default()
        .to_string()
}

fn history_entries_to_color_history(index: HistoryIndex) -> Vec<ColorHistoryEntry> {
    index.colors.into_iter().take(12).collect()
}

fn history_entries_to_ocr_history(index: HistoryIndex) -> Vec<OcrHistoryEntry> {
    index.ocr_entries.into_iter().take(12).collect()
}

fn history_entries_to_code_history(index: HistoryIndex) -> Vec<CodeHistoryEntry> {
    index.code_entries.into_iter().take(12).collect()
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

fn ruler_selection_bounds<T>(adapter: &T) -> Result<CaptureRegion, oddsnap_platform::PlatformError>
where
    T: ScreenCaptureService,
{
    match adapter.monitors() {
        Ok(monitors) => virtual_screen_region(&monitors).ok_or_else(|| {
            oddsnap_platform::PlatformError::Failed(
                "no monitors available for ruler selection".into(),
            )
        }),
        #[cfg(target_os = "linux")]
        Err(error) => {
            let _ = error;
            Ok(CaptureRegion {
                x: 0,
                y: 0,
                width: 1,
                height: 1,
            })
        }
        #[cfg(not(target_os = "linux"))]
        Err(error) => Err(error),
    }
}

fn ruler_measurement_text(region: CaptureRegion) -> String {
    format!(
        "{}x{} px @ {},{}",
        region.width, region.height, region.x, region.y
    )
}

fn center_selection_aspect_ratio(value: &str) -> CenterSelectionAspectRatio {
    match value.trim().to_ascii_lowercase().as_str() {
        "square" => CenterSelectionAspectRatio::Square,
        "widescreen16x9" | "widescreen_16x9" | "16:9" | "16x9" => {
            CenterSelectionAspectRatio::Widescreen16x9
        }
        "classic4x3" | "classic_4x3" | "4:3" | "4x3" => CenterSelectionAspectRatio::Classic4x3,
        "photo3x2" | "photo_3x2" | "3:2" | "3x2" => CenterSelectionAspectRatio::Photo3x2,
        "portrait9x16" | "portrait_9x16" | "9:16" | "9x16" => {
            CenterSelectionAspectRatio::Portrait9x16
        }
        _ => CenterSelectionAspectRatio::Free,
    }
}

fn temporary_processed_capture_path(tool: ProcessedCaptureTool, suffix: &str) -> PathBuf {
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis())
        .unwrap_or_default();
    std::env::temp_dir().join(format!("oddsnap-{}-{}-{suffix}", tool.slug(), timestamp))
}

fn advanced_settings_summary_text(settings: &AppSettings) -> String {
    let upload = if settings.auto_upload_screenshots
        || settings.auto_upload_gifs
        || settings.auto_upload_videos
    {
        upload_settings_summary(settings)
    } else {
        "upload off".into()
    };
    let enabled_tools = settings
        .enabled_tools
        .as_ref()
        .map_or("all tools".into(), |tools| format!("{} tools", tools.len()));
    let translation_model = TranslationModel::from_legacy_value(settings.translation_model);
    let translation_source = oddsnap_core::resolve_translation_source_language(Some(
        &settings.ocr_default_translate_from,
    ));
    let translation_target = oddsnap_core::resolve_translation_target_language(
        Some(&settings.ocr_default_translate_to),
        Some(&settings.interface_language),
        None,
    );

    let summary = format!(
        "Advanced prefs: OCR {} · translate {}->{} via {} · {} · {} · {} · {} custom hotkeys",
        settings.ocr_language_tag,
        translation_source,
        translation_target,
        translation_model.label(),
        image_search_settings_summary(settings),
        upload,
        enabled_tools,
        settings.tool_hotkeys.len()
    );

    #[cfg(target_os = "macos")]
    let summary = {
        let mut summary = summary;
        summary.push_str(" · ");
        summary.push_str(
            &oddsnap_platform_macos::macos_ocr_language_status(&settings.ocr_language_tag)
                .summary(),
        );
        summary
    };

    summary
}

fn next_translation_model(current: u32) -> TranslationModel {
    match TranslationModel::from_legacy_value(current) {
        TranslationModel::Argos => TranslationModel::Google,
        TranslationModel::Google => TranslationModel::OpenSourceLocal,
        TranslationModel::OpenSourceLocal => TranslationModel::Argos,
    }
}

fn next_supported_translation_language(current: &str) -> String {
    let normalized = oddsnap_core::normalize_supported_translation_language(current)
        .unwrap_or_else(|| "auto".into());
    let current_index = SUPPORTED_TRANSLATION_LANGUAGES
        .iter()
        .position(|(code, _)| code.eq_ignore_ascii_case(&normalized))
        .unwrap_or(0);
    let next_index = (current_index + 1) % SUPPORTED_TRANSLATION_LANGUAGES.len();
    SUPPORTED_TRANSLATION_LANGUAGES[next_index].0.to_string()
}

fn lifecycle_settings_summary(settings: &AppSettings) -> String {
    let startup = if settings.start_with_windows {
        "startup requested"
    } else {
        "startup off"
    };
    let updates = if settings.auto_check_for_updates {
        "update checks requested"
    } else {
        "update checks off"
    };

    format!(
        "Lifecycle prefs: {startup} · {updates} · Rust release/update channel not enabled on this branch"
    )
}

fn sticker_settings_summary_text(settings: &AppSettings) -> String {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    format!(
        "Sticker prefs: {}",
        sticker_settings_summary(&sticker_settings)
    )
}

fn upscale_settings_summary_text(settings: &AppSettings) -> String {
    let upscale_settings =
        UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref());
    format!(
        "Upscale prefs: {}",
        upscale_settings_summary(&upscale_settings)
    )
}

fn sticker_settings_summary(settings: &StickerSettings) -> String {
    format!(
        "{} · remove.bg {} · photoroom {} · model {} · {} · stroke {} · shadow {}",
        settings.provider.label(),
        settings_text::masked_secret_summary(&settings.remove_bg_api_key),
        settings_text::masked_secret_summary(&settings.photoroom_api_key),
        settings.local_engine,
        execution_provider_label(&settings.local_execution_provider),
        on_off(settings.add_stroke),
        on_off(settings.add_shadow)
    )
}

fn upscale_settings_summary(settings: &UpscaleSettings) -> String {
    format!(
        "{} · deepai {} · model {} · {} · {}x · preview {}",
        settings.provider.label(),
        settings_text::masked_secret_summary(&settings.deep_ai_api_key),
        settings.local_engine,
        execution_provider_label(&settings.local_execution_provider),
        settings.scale_factor,
        on_off(settings.show_preview_window)
    )
}

fn next_sticker_provider(provider: &StickerProvider) -> StickerProvider {
    match provider {
        StickerProvider::LocalCpu => StickerProvider::RemoveBg,
        StickerProvider::RemoveBg => StickerProvider::Photoroom,
        StickerProvider::Photoroom => StickerProvider::None,
        StickerProvider::None | StickerProvider::Unknown(_) => StickerProvider::LocalCpu,
    }
}

fn next_upscale_provider(provider: &UpscaleProvider) -> UpscaleProvider {
    match provider {
        UpscaleProvider::Local => UpscaleProvider::DeepAiSuperResolution,
        UpscaleProvider::DeepAiSuperResolution => UpscaleProvider::DeepAiWaifu2x,
        UpscaleProvider::DeepAiWaifu2x => UpscaleProvider::None,
        UpscaleProvider::None | UpscaleProvider::Unknown(_) => UpscaleProvider::Local,
    }
}

fn next_sticker_local_engine(engine: &str) -> &'static str {
    match engine.trim().to_ascii_lowercase().as_str() {
        "u2netp" => "U2Net",
        "u2net" => "BriaRmbg",
        "briarmbg" | "bria-rmbg" | "bria" | "rmbg" => "BiRefNetLite",
        "birefnetlite" | "birefnet-general-lite" => "IsNetGeneralUse",
        _ => "U2Netp",
    }
}

fn next_upscale_local_engine(engine: &str) -> &'static str {
    match engine.trim().to_ascii_lowercase().as_str() {
        "swinirrealworld" | "swinir-realworld" | "swinir" => "RealEsrganX4Plus",
        _ => "SwinIrRealWorld",
    }
}

fn next_execution_provider(provider: &str) -> String {
    if provider.eq_ignore_ascii_case("gpu") {
        "Cpu".into()
    } else {
        "Gpu".into()
    }
}

fn execution_provider_label(provider: &str) -> &'static str {
    if provider.eq_ignore_ascii_case("gpu") {
        "GPU"
    } else {
        "CPU"
    }
}

fn set_active_sticker_engine(settings: &mut StickerSettings, engine: &str) {
    settings.local_engine = engine.into();
    if settings
        .local_execution_provider
        .eq_ignore_ascii_case("gpu")
    {
        settings.local_gpu_engine = engine.into();
    } else {
        settings.local_cpu_engine = engine.into();
    }
}

fn set_active_upscale_engine(settings: &mut UpscaleSettings, engine: &str) {
    settings.local_engine = engine.into();
    if settings
        .local_execution_provider
        .eq_ignore_ascii_case("gpu")
    {
        settings.local_gpu_engine = engine.into();
    } else {
        settings.local_cpu_engine = engine.into();
    }
}

fn next_upscale_scale_factor(scale: u32) -> u32 {
    match scale {
        2 => 3,
        3 => 4,
        _ => 2,
    }
}

fn processed_capture_preview_enabled(tool: ProcessedCaptureTool, settings: &AppSettings) -> bool {
    match tool {
        ProcessedCaptureTool::Sticker => false,
        ProcessedCaptureTool::Upscale => {
            UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref())
                .show_preview_window
        }
    }
}

fn image_search_settings_summary(settings: &AppSettings) -> String {
    image_search::settings_summary(settings)
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
        None if effective_destination == UploadDestination::TempHosts => {
            format!("upload {label} configured")
        }
        None if effective_destination.curl_upload_supported() => format!("upload {label} ready"),
        None => format!("upload {label} configured; backend pending"),
    }
}

fn upload_history_status(entry: Option<&CaptureHistoryEntry>) -> String {
    let Some(entry) = entry else {
        return String::new();
    };

    if let Some(url) = entry.upload_url.as_deref().filter(|url| !url.is_empty()) {
        let provider = entry
            .upload_provider
            .as_deref()
            .filter(|provider| !provider.is_empty())
            .unwrap_or("upload");
        return format!("; uploaded to {provider}: {url}");
    }

    if let Some(error) = entry
        .upload_error
        .as_deref()
        .filter(|error| !error.is_empty())
    {
        if let Some(pending) = error.strip_prefix("pending: ") {
            return format!("; upload pending: {pending}");
        }
        return format!("; upload failed: {error}");
    }

    String::new()
}

fn filtered_upload_links_text(entries: &[CaptureHistoryEntry]) -> Option<String> {
    let links = entries
        .iter()
        .filter_map(|entry| entry.upload_url.as_deref())
        .map(str::trim)
        .filter(|url| !url.is_empty())
        .collect::<Vec<_>>();
    (!links.is_empty()).then(|| links.join("\n"))
}

fn run_curl_upload(
    destination: UploadDestination,
    path: &Path,
    settings: &UploadSettings,
) -> Result<oddsnap_core::UploadSuccess, String> {
    if destination == UploadDestination::Dropbox {
        return run_dropbox_curl_upload(path, settings);
    }
    if destination == UploadDestination::OneDrive {
        return run_onedrive_curl_upload(path, settings);
    }
    if destination == UploadDestination::GoogleDrive {
        return run_google_drive_curl_upload(path, settings);
    }

    let request =
        oddsnap_core::build_curl_upload_request_with_settings(destination, path, settings)?;
    let (stdout, stderr) = run_curl_request(&request)?;

    oddsnap_core::parse_curl_upload_output_with_success_url(
        request.destination,
        &stdout,
        settings,
        request.success_url.as_deref(),
    )
    .map_err(|error| append_curl_stderr(error, &stderr))
}

fn run_dropbox_curl_upload(
    path: &Path,
    settings: &UploadSettings,
) -> Result<oddsnap_core::UploadSuccess, String> {
    let plan = oddsnap_core::build_dropbox_curl_upload_plan(path, settings)?;

    let (upload_stdout, upload_stderr) = run_curl_request(&plan.upload)?;
    oddsnap_core::parse_dropbox_upload_ack(&upload_stdout)
        .map_err(|error| append_curl_stderr(error, &upload_stderr))?;

    let (link_stdout, link_stderr) = run_curl_request(&plan.create_shared_link)?;
    match oddsnap_core::parse_dropbox_shared_link_output(&link_stdout) {
        Ok(success) => Ok(success),
        Err(_) if oddsnap_core::dropbox_shared_link_already_exists(&link_stdout) => {
            let (list_stdout, list_stderr) = run_curl_request(&plan.list_shared_links)?;
            oddsnap_core::parse_dropbox_list_shared_links_output(&list_stdout)
                .map_err(|list_error| append_curl_stderr(list_error, &list_stderr))
        }
        Err(error) => Err(append_curl_stderr(error, &link_stderr)),
    }
}

fn run_onedrive_curl_upload(
    path: &Path,
    settings: &UploadSettings,
) -> Result<oddsnap_core::UploadSuccess, String> {
    let plan = oddsnap_core::build_onedrive_curl_upload_plan(path, settings)?;
    let (upload_stdout, upload_stderr) = run_curl_request(&plan.upload)?;
    let item_id = oddsnap_core::parse_onedrive_upload_item_id(&upload_stdout)
        .map_err(|error| append_curl_stderr(error, &upload_stderr))?;
    let create_link = oddsnap_core::build_onedrive_create_link_request(&item_id, settings)?;
    let (link_stdout, link_stderr) = run_curl_request(&create_link)?;
    oddsnap_core::parse_onedrive_create_link_output(&link_stdout)
        .map_err(|error| append_curl_stderr(error, &link_stderr))
}

fn run_google_drive_curl_upload(
    path: &Path,
    settings: &UploadSettings,
) -> Result<oddsnap_core::UploadSuccess, String> {
    let plan = oddsnap_core::build_google_drive_curl_upload_plan(path, settings)?;
    let (upload_stdout, upload_stderr) = run_curl_request(&plan.upload)?;
    let file_id = match plan.kind {
        oddsnap_core::GoogleDriveUploadPlanKind::Multipart => {
            oddsnap_core::parse_google_drive_upload_file_id(&upload_stdout)
                .map_err(|error| append_curl_stderr(error, &upload_stderr))?
        }
        oddsnap_core::GoogleDriveUploadPlanKind::Resumable => {
            let session_url =
                oddsnap_core::parse_google_drive_resumable_session_output(&upload_stdout)
                    .map_err(|error| append_curl_stderr(error, &upload_stderr))?;
            let resumable =
                oddsnap_core::build_google_drive_resumable_upload_request(&session_url, path)?;
            let (resumable_stdout, resumable_stderr) = run_curl_request(&resumable)?;
            oddsnap_core::parse_google_drive_upload_file_id(&resumable_stdout)
                .map_err(|error| append_curl_stderr(error, &resumable_stderr))?
        }
    };
    let permission = oddsnap_core::build_google_drive_permission_request(&file_id, settings)?;
    let (permission_stdout, permission_stderr) = run_curl_request(&permission)?;
    oddsnap_core::parse_google_drive_permission_output(&permission_stdout, &file_id)
        .map_err(|error| append_curl_stderr(error, &permission_stderr))
}

fn run_curl_request(request: &oddsnap_core::CurlUploadRequest) -> Result<(String, String), String> {
    let mut command = Command::new(&request.program);
    command.args(&request.args);
    if request.stdin_body.is_some() {
        command.stdin(Stdio::piped());
    } else {
        command.stdin(Stdio::null());
    }
    let mut child = command
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| {
            format!(
                "{} upload could not start curl: {error}",
                request.provider_name
            )
        })?;

    if let Some(body) = &request.stdin_body {
        let mut stdin = child.stdin.take().ok_or_else(|| {
            format!(
                "{} upload could not open curl stdin.",
                request.provider_name
            )
        })?;
        stdin.write_all(body).map_err(|error| {
            format!(
                "{} upload could not write curl body: {error}",
                request.provider_name
            )
        })?;
    }

    let output = child.wait_with_output().map_err(|error| {
        format!(
            "{} upload failed while waiting for curl: {error}",
            request.provider_name
        )
    })?;

    let stdout = String::from_utf8_lossy(&output.stdout);
    let stderr = String::from_utf8_lossy(&output.stderr);
    if !output.status.success() && stdout.trim().is_empty() {
        let error = stderr.trim();
        return Err(if error.is_empty() {
            format!(
                "{} upload failed before a response was returned.",
                request.provider_name
            )
        } else {
            error.into()
        });
    }

    Ok((stdout.into(), stderr.into()))
}

fn append_curl_stderr(error: String, stderr: &str) -> String {
    let stderr = stderr.trim();
    if stderr.is_empty() {
        error
    } else {
        format!("{error}; curl: {stderr}")
    }
}

#[cfg(any(target_os = "windows", target_os = "linux", target_os = "macos"))]
fn select_recording_region<T>(
    adapter: &T,
    settings: &AppSettings,
) -> Result<CaptureRegion, oddsnap_platform::PlatformError>
where
    T: ScreenCaptureService + RegionSelectionService,
{
    let bounds = match adapter.monitors() {
        Ok(monitors) => virtual_screen_region(&monitors).ok_or_else(|| {
            oddsnap_platform::PlatformError::Failed(
                "no monitors available for region recording".into(),
            )
        })?,
        #[cfg(target_os = "linux")]
        Err(error) => {
            let _ = error;
            CaptureRegion {
                x: 0,
                y: 0,
                width: 1,
                height: 1,
            }
        }
        #[cfg(not(target_os = "linux"))]
        Err(error) => return Err(error),
    };

    adapter
        .select_region(OverlayWindowRequest {
            bounds,
            opacity: 24,
            click_through: false,
            show_crosshair_guides: settings.show_crosshair_guides,
            show_magnifier: settings.show_capture_magnifier,
            detect_windows: settings.detect_windows,
            selection_mode: RegionSelectionMode::Rectangle,
        })?
        .ok_or_else(|| oddsnap_platform::PlatformError::Failed("region recording canceled".into()))
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

fn stable_text_key(text: &str) -> String {
    let mut hash = 0xcbf29ce484222325u64;
    for byte in text.bytes() {
        hash ^= byte as u64;
        hash = hash.wrapping_mul(0x100000001b3);
    }
    format!("{hash:016x}")
}

fn app_unix_millis_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64)
        .unwrap_or_default()
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

fn settings_text_input_panel(input: &settings_text::SettingsTextInputState) -> impl IntoElement {
    ui::surface_style(div())
        .flex()
        .flex_col()
        .gap(px(6.0))
        .child(
            div()
                .text_size(px(11.0))
                .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                .child(SharedString::from(format!(
                    "Editing {}",
                    input.target.label()
                ))),
        )
        .child(
            div()
                .rounded(px(6.0))
                .border_1()
                .border_color(rgb(0x5d6f92))
                .bg(rgb(0x151922))
                .px(px(10.0))
                .py(px(7.0))
                .text_size(px(12.0))
                .text_color(ui::skin::color(ui::skin::BRIGHT_TEXT))
                .child(SharedString::from(input.value.clone())),
        )
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

fn next_scrolling_capture_mode(mode: &str) -> String {
    match ScrollingCaptureMode::from_setting(mode) {
        ScrollingCaptureMode::Automatic => "Manual".into(),
        ScrollingCaptureMode::Manual => "Automatic".into(),
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

fn next_toast_duration_seconds(seconds: f64) -> f64 {
    if seconds < 2.0 {
        2.5
    } else if seconds < 4.0 {
        4.5
    } else {
        1.5
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

fn recording_history_kind(format: RecordingFormat) -> HistoryKind {
    match format {
        RecordingFormat::Gif => HistoryKind::Gif,
        RecordingFormat::Mp4 | RecordingFormat::WebM | RecordingFormat::Mkv => HistoryKind::Video,
    }
}

#[cfg(target_os = "macos")]
fn recording_audio_status_summary(settings: &AppSettings) -> (&'static str, &'static str) {
    let microphone = if settings.record_microphone {
        "mic on"
    } else {
        "mic off"
    };
    let desktop_audio = if settings.record_desktop_audio {
        "system audio configured, pending"
    } else {
        "system audio off"
    };
    (microphone, desktop_audio)
}

#[cfg(any(target_os = "linux", target_os = "windows"))]
fn recording_audio_status_summary(settings: &AppSettings) -> (&'static str, &'static str) {
    let microphone = if settings.record_microphone {
        "mic on"
    } else {
        "mic off"
    };
    let desktop_audio = if settings.record_desktop_audio {
        "desktop audio on"
    } else {
        "desktop audio off"
    };
    (microphone, desktop_audio)
}

#[cfg(all(
    not(target_os = "linux"),
    not(target_os = "macos"),
    not(target_os = "windows")
))]
fn recording_audio_status_summary(_: &AppSettings) -> (&'static str, &'static str) {
    ("mic unsupported", "desktop audio unsupported")
}

#[cfg(target_os = "linux")]
fn recording_audio_request_for_host(settings: &AppSettings) -> (bool, bool, Option<&'static str>) {
    (
        settings.record_microphone,
        settings.record_desktop_audio,
        None,
    )
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
    (
        settings.record_microphone,
        settings.record_desktop_audio,
        None,
    )
}

#[cfg(all(
    not(target_os = "linux"),
    not(target_os = "macos"),
    not(target_os = "windows")
))]
fn recording_audio_request_for_host(_: &AppSettings) -> (bool, bool, Option<&'static str>) {
    (false, false, None)
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

fn open_file_command(path: &Path) -> (&'static str, Vec<String>) {
    #[cfg(target_os = "windows")]
    {
        ("explorer.exe", vec![path.display().to_string()])
    }

    #[cfg(target_os = "macos")]
    {
        ("open", vec![path.display().to_string()])
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    {
        ("xdg-open", vec![path.display().to_string()])
    }
}

fn open_file_path(path: &Path) -> Result<(), String> {
    if !path.exists() {
        return Err(format!("{} does not exist", path.display()));
    }

    let (program, args) = open_file_command(path);
    let status = Command::new(program)
        .args(args)
        .status()
        .map_err(|error| format!("failed to run {program}: {error}"))?;
    if status.success() {
        Ok(())
    } else {
        Err(format!("{program} exited with status {status}"))
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
        assert_eq!(next_scrolling_capture_mode("Automatic"), "Manual");
        assert_eq!(next_scrolling_capture_mode("Manual"), "Automatic");
        assert_eq!(next_scrolling_capture_mode("InvalidLegacyValue"), "Manual");
        assert_eq!(next_toast_duration_seconds(1.5), 2.5);
        assert_eq!(next_toast_duration_seconds(2.5), 4.5);
        assert_eq!(next_toast_duration_seconds(4.5), 1.5);
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
    fn cycles_sticker_and_upscale_settings_options() {
        assert_eq!(
            next_sticker_provider(&StickerProvider::LocalCpu),
            StickerProvider::RemoveBg
        );
        assert_eq!(
            next_sticker_provider(&StickerProvider::Photoroom),
            StickerProvider::None
        );
        assert_eq!(next_sticker_local_engine("U2Netp"), "U2Net");
        assert_eq!(next_sticker_local_engine("IsNetGeneralUse"), "U2Netp");

        let mut sticker = StickerSettings::default();
        set_active_sticker_engine(&mut sticker, "U2Net");
        assert_eq!(sticker.local_engine, "U2Net");
        assert_eq!(sticker.local_cpu_engine, "U2Net");
        sticker.remove_bg_api_key = "remove-secret".into();
        assert!(sticker_settings_summary(&sticker).contains("remove.bg set ...cret"));

        assert_eq!(
            next_upscale_provider(&UpscaleProvider::Local),
            UpscaleProvider::DeepAiSuperResolution
        );
        assert_eq!(
            next_upscale_provider(&UpscaleProvider::DeepAiWaifu2x),
            UpscaleProvider::None
        );
        assert_eq!(
            next_upscale_local_engine("SwinIrRealWorld"),
            "RealEsrganX4Plus"
        );
        assert_eq!(
            next_upscale_local_engine("RealEsrganX4Plus"),
            "SwinIrRealWorld"
        );
        assert_eq!(next_execution_provider("Cpu"), "Gpu");
        assert_eq!(next_execution_provider("Gpu"), "Cpu");
        assert_eq!(next_upscale_scale_factor(2), 3);
        assert_eq!(next_upscale_scale_factor(3), 4);
        assert_eq!(next_upscale_scale_factor(4), 2);

        let mut preview_settings = AppSettings {
            upscale_upload_settings: Some(
                UpscaleSettings {
                    show_preview_window: true,
                    ..UpscaleSettings::default()
                }
                .to_json_value(),
            ),
            ..AppSettings::default()
        };
        assert!(processed_capture_preview_enabled(
            ProcessedCaptureTool::Upscale,
            &preview_settings
        ));
        assert!(!processed_capture_preview_enabled(
            ProcessedCaptureTool::Sticker,
            &preview_settings
        ));

        preview_settings.upscale_upload_settings = Some(
            UpscaleSettings {
                show_preview_window: false,
                ..UpscaleSettings::default()
            }
            .to_json_value(),
        );
        assert!(!processed_capture_preview_enabled(
            ProcessedCaptureTool::Upscale,
            &preview_settings
        ));
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
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Ocr),
            DefaultCaptureAction::Ocr
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Scan),
            DefaultCaptureAction::Scan
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Sticker),
            DefaultCaptureAction::Sticker
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Upscale),
            DefaultCaptureAction::Upscale
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Center),
            DefaultCaptureAction::Center
        ));
        assert!(matches!(
            default_capture_action(DefaultCaptureMode::Ruler),
            DefaultCaptureAction::Ruler
        ));
    }

    #[test]
    fn pending_default_capture_status_is_explicit() {
        assert_eq!(
            actions::pending_default_capture_status("Capture hotkey", PendingTool::Sticker),
            "Capture hotkey received; default capture mode 'Sticker' needs Rust sticker/background removal parity."
        );
    }

    #[test]
    fn ruler_measurement_text_includes_size_and_origin() {
        assert_eq!(
            ruler_measurement_text(CaptureRegion {
                x: -10,
                y: 20,
                width: 640,
                height: 480,
            }),
            "640x480 px @ -10,20"
        );
    }

    #[test]
    fn center_selection_aspect_ratio_accepts_legacy_names() {
        assert_eq!(
            center_selection_aspect_ratio("Widescreen16x9"),
            CenterSelectionAspectRatio::Widescreen16x9
        );
        assert_eq!(
            center_selection_aspect_ratio("4:3"),
            CenterSelectionAspectRatio::Classic4x3
        );
        assert_eq!(
            center_selection_aspect_ratio("unknown"),
            CenterSelectionAspectRatio::Free
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
    fn builds_history_open_file_command_for_current_platform() {
        #[cfg(target_os = "windows")]
        let path = PathBuf::from(r"C:\captures\OddSnap.png");
        #[cfg(not(target_os = "windows"))]
        let path = PathBuf::from("/tmp/captures/OddSnap.png");

        let (program, args) = open_file_command(&path);

        #[cfg(target_os = "windows")]
        {
            assert_eq!(program, "explorer.exe");
            assert_eq!(args, vec![r"C:\captures\OddSnap.png"]);
        }

        #[cfg(target_os = "macos")]
        {
            assert_eq!(program, "open");
            assert_eq!(args, vec!["/tmp/captures/OddSnap.png"]);
        }

        #[cfg(all(unix, not(target_os = "macos")))]
        {
            assert_eq!(program, "xdg-open");
            assert_eq!(args, vec!["/tmp/captures/OddSnap.png"]);
        }
    }

    #[test]
    fn recording_target_labels_are_stable() {
        assert_eq!(RecordingTarget::FullScreen.label(), "desktop");
        assert_eq!(RecordingTarget::ActiveWindow.label(), "active window");
        assert_eq!(RecordingTarget::Region.label(), "region");
        assert_eq!(RecordingTarget::Region.capture_mode().label(), "Rectangle");
    }

    #[test]
    fn recording_history_kind_matches_output_format() {
        assert_eq!(
            recording_history_kind(RecordingFormat::Gif),
            HistoryKind::Gif
        );
        assert_eq!(
            recording_history_kind(RecordingFormat::Mp4),
            HistoryKind::Video
        );
        assert_eq!(
            recording_history_kind(RecordingFormat::WebM),
            HistoryKind::Video
        );
        assert_eq!(
            recording_history_kind(RecordingFormat::Mkv),
            HistoryKind::Video
        );
    }

    #[test]
    fn recording_audio_request_matches_host_support() {
        let settings = AppSettings {
            record_microphone: true,
            record_desktop_audio: true,
            ..AppSettings::default()
        };
        let (microphone_status, desktop_audio_status) = recording_audio_status_summary(&settings);
        let (microphone, desktop_audio, note) = recording_audio_request_for_host(&settings);

        #[cfg(target_os = "linux")]
        {
            assert_eq!(microphone_status, "mic on");
            assert_eq!(desktop_audio_status, "desktop audio on");
            assert!(microphone);
            assert!(desktop_audio);
            assert_eq!(note, None);
        }

        #[cfg(target_os = "macos")]
        {
            assert_eq!(microphone_status, "mic on");
            assert_eq!(desktop_audio_status, "system audio configured, pending");
            assert!(microphone);
            assert!(!desktop_audio);
            assert_eq!(
                note,
                Some("system audio capture pending on macOS; recording without system audio")
            );
        }

        #[cfg(target_os = "windows")]
        {
            assert_eq!(microphone_status, "mic on");
            assert_eq!(desktop_audio_status, "desktop audio on");
            assert!(microphone);
            assert!(desktop_audio);
            assert_eq!(note, None);
        }

        #[cfg(all(
            not(target_os = "linux"),
            not(target_os = "macos"),
            not(target_os = "windows")
        ))]
        {
            assert_eq!(microphone_status, "mic unsupported");
            assert_eq!(desktop_audio_status, "desktop audio unsupported");
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
            file_name: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            captured_at_unix_ms: 1,
            image_search_ocr_text: String::new(),
            image_search_record: None,
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
    fn media_history_filters_apply_kind_and_upload_state() {
        let base = CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: HistoryKind::Image,
            path: "capture.png".into(),
            file_name: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            captured_at_unix_ms: 1,
            image_search_ocr_text: String::new(),
            image_search_record: None,
            upload_url: None,
            upload_provider: None,
            upload_error: None,
        };
        let mut uploaded = base.clone();
        uploaded.upload_url = Some("https://example.test/capture.png".into());
        let mut failed = base.clone();
        failed.kind = HistoryKind::Video;
        failed.file_name = "clip.mp4".into();
        failed.upload_error = Some("rate limited".into());
        let mut pending = base.clone();
        pending.kind = HistoryKind::Sticker;
        pending.file_name = "sticker.png".into();
        pending.upload_error = Some("pending: backend pending".into());

        let entries = vec![uploaded, failed, pending];

        assert_eq!(
            filtered_capture_history(
                &entries,
                HistoryKindFilter::Image,
                HistoryUploadFilter::Uploaded
            )
            .len(),
            1
        );
        assert_eq!(
            filtered_capture_history(
                &entries,
                HistoryKindFilter::Video,
                HistoryUploadFilter::Failed
            )[0]
            .file_name,
            "clip.mp4"
        );
        assert_eq!(
            filtered_capture_history(
                &entries,
                HistoryKindFilter::Sticker,
                HistoryUploadFilter::Pending
            )[0]
            .file_name,
            "sticker.png"
        );
        assert_eq!(
            filtered_capture_history(&entries, HistoryKindFilter::Gif, HistoryUploadFilter::All)
                .len(),
            0
        );
    }

    #[test]
    fn filtered_upload_links_text_skips_empty_links() {
        let mut first = CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: HistoryKind::Image,
            path: "capture.png".into(),
            file_name: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            captured_at_unix_ms: 1,
            image_search_ocr_text: String::new(),
            image_search_record: None,
            upload_url: Some(" https://example.test/a.png ".into()),
            upload_provider: Some("Example".into()),
            upload_error: None,
        };
        let mut second = first.clone();
        second.upload_url = None;
        let mut third = first.clone();
        third.upload_url = Some("https://example.test/b.png".into());

        assert_eq!(
            filtered_upload_links_text(&[first.clone(), second, third]).as_deref(),
            Some("https://example.test/a.png\nhttps://example.test/b.png")
        );
        first.upload_url = Some(" ".into());
        assert_eq!(filtered_upload_links_text(&[first]), None);
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
        assert_eq!(upload_settings_summary(&configured), "upload Catbox ready");
    }

    #[test]
    fn lifecycle_summary_reports_imported_startup_and_update_flags() {
        let disabled = AppSettings {
            start_with_windows: false,
            auto_check_for_updates: false,
            ..AppSettings::default()
        };
        assert_eq!(
            lifecycle_settings_summary(&disabled),
            "Lifecycle prefs: startup off · update checks off · Rust release/update channel not enabled on this branch"
        );

        let enabled = AppSettings {
            start_with_windows: true,
            auto_check_for_updates: true,
            ..AppSettings::default()
        };
        assert_eq!(
            lifecycle_settings_summary(&enabled),
            "Lifecycle prefs: startup requested · update checks requested · Rust release/update channel not enabled on this branch"
        );
    }

    #[test]
    fn advanced_settings_summary_uses_core_translation_and_search_labels() {
        let mut tool_hotkeys = std::collections::BTreeMap::new();
        tool_hotkeys.insert("arrow".into(), vec![0, 49]);
        let settings = AppSettings {
            ocr_language_tag: "en-US".into(),
            ocr_default_translate_from: "auto".into(),
            ocr_default_translate_to: "fr-CA".into(),
            translation_model: TranslationModel::Google as u32,
            interface_language: "es-MX".into(),
            image_search_sources: ImageSearchSources::FILE_NAME.bits(),
            image_search_exact_match: true,
            auto_upload_screenshots: true,
            image_upload_destination: "Catbox".into(),
            enabled_tools: Some(vec!["rect".into(), "ocr".into()]),
            tool_hotkeys,
            ..AppSettings::default()
        };

        assert_eq!(
            advanced_settings_summary_text(&settings),
            "Advanced prefs: OCR en-US · translate auto->fr via Google Translate · image search file/exact · upload Catbox ready · 2 tools · 1 custom hotkeys"
        );
    }

    #[test]
    fn translation_setting_cycles_use_supported_core_values() {
        assert_eq!(
            next_translation_model(TranslationModel::Argos as u32),
            TranslationModel::Google
        );
        assert_eq!(
            next_translation_model(TranslationModel::Google as u32),
            TranslationModel::OpenSourceLocal
        );
        assert_eq!(
            next_translation_model(TranslationModel::OpenSourceLocal as u32),
            TranslationModel::Argos
        );

        assert_eq!(next_supported_translation_language("auto"), "ar");
        assert_eq!(next_supported_translation_language("fr-CA"), "ga");
        assert_eq!(next_supported_translation_language("unknown"), "ar");
    }

    #[test]
    fn image_search_summary_reports_hidden_and_source_modes() {
        let hidden = AppSettings {
            show_image_search_bar: false,
            ..AppSettings::default()
        };
        assert_eq!(
            image_search_settings_summary(&hidden),
            "image search hidden"
        );

        let ocr_only = AppSettings {
            image_search_sources: ImageSearchSources::OCR.bits(),
            ..AppSettings::default()
        };
        assert_eq!(
            image_search_settings_summary(&ocr_only),
            "image search OCR/loose"
        );

        let disabled = AppSettings {
            image_search_sources: ImageSearchSources::NONE.bits(),
            ..AppSettings::default()
        };
        assert_eq!(
            image_search_settings_summary(&disabled),
            "image search off/loose"
        );
    }

    #[test]
    fn upload_history_status_reports_success_pending_and_failure() {
        let mut entry = CaptureHistoryEntry {
            mode: CaptureMode::FullScreen,
            kind: HistoryKind::Image,
            path: "capture.png".into(),
            file_name: "capture.png".into(),
            preview_path: None,
            width: 10,
            height: 10,
            captured_at_unix_ms: 1,
            image_search_ocr_text: String::new(),
            image_search_record: None,
            upload_url: Some("https://files.catbox.moe/capture.png".into()),
            upload_provider: Some("Catbox".into()),
            upload_error: None,
        };

        assert_eq!(
            upload_history_status(Some(&entry)),
            "; uploaded to Catbox: https://files.catbox.moe/capture.png"
        );

        entry.upload_url = None;
        entry.upload_error = Some("pending: Rust upload backend for Imgur is pending.".into());
        assert_eq!(
            upload_history_status(Some(&entry)),
            "; upload pending: Rust upload backend for Imgur is pending."
        );

        entry.upload_error = Some("Catbox rate limit reached".into());
        assert_eq!(
            upload_history_status(Some(&entry)),
            "; upload failed: Catbox rate limit reached"
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
                file_name: "capture.mp4".into(),
                preview_path: None,
                width: 10,
                height: 10,
                captured_at_unix_ms: 1,
                image_search_ocr_text: String::new(),
                image_search_record: None,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
            CaptureHistoryEntry {
                mode: CaptureMode::FullScreen,
                kind: HistoryKind::Image,
                path: missing.display().to_string(),
                file_name: "missing.png".into(),
                preview_path: None,
                width: 10,
                height: 10,
                captured_at_unix_ms: 2,
                image_search_ocr_text: String::new(),
                image_search_record: None,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
            CaptureHistoryEntry {
                mode: CaptureMode::FullScreen,
                kind: HistoryKind::Sticker,
                path: image.display().to_string(),
                file_name: "capture.png".into(),
                preview_path: None,
                width: 10,
                height: 10,
                captured_at_unix_ms: 3,
                image_search_ocr_text: String::new(),
                image_search_record: None,
                upload_url: None,
                upload_provider: None,
                upload_error: None,
            },
        ];

        assert_eq!(newest_history_image_path(&history), Some(image));
        let _ = std::fs::remove_dir_all(root);
    }

    #[derive(Debug)]
    struct FakeOcrTextService;

    impl OcrTextService for FakeOcrTextService {
        fn recognize_text(
            &self,
            request: OcrTextRequest,
        ) -> Result<oddsnap_platform::OcrTextResult, oddsnap_platform::PlatformError> {
            assert_eq!(request.language_tag, "en-US");
            Ok(oddsnap_platform::OcrTextResult {
                text: " Settings window ".into(),
                engine_id: "fake-ocr".into(),
            })
        }
    }

    #[test]
    fn hydrate_image_search_record_ocr_indexes_trimmed_text() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-image-search-ocr-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp test root");
        let image = root.join("settings.png");
        std::fs::write(&image, b"fake image").expect("write image");
        let history_entry =
            HistoryEntry::from_capture_file(image.clone(), 10, 10, HistoryKind::Image)
                .expect("history entry");
        let mut record =
            pending_image_search_record_from_history_entry(&history_entry, "auto", "pending")
                .expect("pending record");
        let mut summary = ImageSearchOcrHydrationSummary::default();

        hydrate_image_search_record_ocr(
            &mut record,
            &FakeOcrTextService,
            "en-US",
            123,
            &mut summary,
        );

        assert_eq!(summary.attempted, 1);
        assert_eq!(summary.indexed, 1);
        assert_eq!(record.ocr_text, "Settings window");
        assert_eq!(record.ocr_engine_id, "fake-ocr");
        assert_eq!(record.ocr_language_tag, "en-US");

        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn image_search_index_status_reports_pending_ready_and_failed_counts() {
        let pending = ImageSearchIndexRecord {
            file_path: PathBuf::from("pending.png"),
            file_length_bytes: 10,
            last_write_time_unix_ms: 1,
            ocr_language_tag: "auto".into(),
            ocr_engine_id: "pending".into(),
            ocr_completed: false,
            ocr_state: ImageSearchOcrState::Pending,
            ocr_retry_count: 0,
            next_ocr_retry_unix_ms: 0,
            ocr_text: String::new(),
            indexed_at_unix_ms: 1,
            last_error: String::new(),
        };
        let mut indexed = pending.clone();
        indexed.file_path = PathBuf::from("indexed.png");
        indexed.ocr_completed = true;
        indexed.ocr_state = ImageSearchOcrState::Indexed;
        indexed.ocr_text = "Settings".into();
        let mut failed = pending.clone();
        failed.file_path = PathBuf::from("failed.png");
        failed.ocr_state = ImageSearchOcrState::Failed;
        failed.ocr_retry_count = 3;

        let status = image_search_index_status(&ImageSearchIndex {
            records: vec![pending, indexed, failed],
        });

        assert_eq!(
            status,
            "Image index: 3 records, 1 pending, 1 OCR ready, 1 failed."
        );
    }

    #[test]
    fn image_search_reindex_queue_tracks_progress_and_cancel_state() {
        let mut queue = ImageSearchReindexQueueState::start(3);
        assert!(queue.active);
        assert_eq!(queue.total, 3);

        queue.current_file = Some("first.png".into());
        queue.apply_summary(&ImageSearchOcrHydrationSummary {
            attempted: 1,
            indexed: 1,
            ..ImageSearchOcrHydrationSummary::default()
        });
        assert_eq!(
            queue.detail_text(),
            "Reindex running: 1/3 processed, 1 text, 0 empty, 0 failed, 0 skipped; current first.png."
        );

        queue.request_cancel();
        assert!(queue.cancel_requested);
        assert!(queue.detail_text().starts_with("Reindex canceling:"));
        queue.finish();
        assert!(!queue.active);
        assert_eq!(queue.current_file, None);
    }

    #[test]
    fn image_search_reindex_pending_count_excludes_indexed_and_failed_records() {
        let pending = ImageSearchIndexRecord {
            file_path: PathBuf::from("pending.png"),
            file_length_bytes: 10,
            last_write_time_unix_ms: 1,
            ocr_language_tag: "auto".into(),
            ocr_engine_id: "pending".into(),
            ocr_completed: false,
            ocr_state: ImageSearchOcrState::Pending,
            ocr_retry_count: 0,
            next_ocr_retry_unix_ms: 0,
            ocr_text: String::new(),
            indexed_at_unix_ms: 1,
            last_error: String::new(),
        };
        let mut indexed = pending.clone();
        indexed.file_path = PathBuf::from("indexed.png");
        indexed.ocr_completed = true;
        indexed.ocr_state = ImageSearchOcrState::Indexed;
        let mut failed = pending.clone();
        failed.file_path = PathBuf::from("failed.png");
        failed.ocr_state = ImageSearchOcrState::Failed;

        assert_eq!(
            image_search_reindex_pending_count(
                &ImageSearchIndex {
                    records: vec![pending, indexed, failed],
                },
                10
            ),
            1
        );
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
    fn open_external_url_rejects_non_http_urls_before_launch() {
        assert_eq!(
            open_external_url("file:///tmp/capture.png").expect_err("reject local file URL"),
            "external URL must be absolute HTTP(S)"
        );
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
            ocr_entries: Vec::new(),
            code_entries: Vec::new(),
        };

        let colors = history_entries_to_color_history(index);

        assert_eq!(colors.len(), 12);
        assert_eq!(colors[0].hex, "000000");
        assert_eq!(colors[11].hex, "00000B");
    }
}
