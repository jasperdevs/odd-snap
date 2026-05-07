#[cfg(not(target_os = "windows"))]
use std::time::Duration;

#[cfg(any(test, not(target_os = "windows")))]
use crate::actions::CrossPlatformHotkeyEvent;
use crate::actions::PendingTool;

#[derive(Clone, Copy)]
pub(crate) struct ImportedHotkeyAccelerators<'a> {
    pub(crate) capture: &'a str,
    pub(crate) recording: Option<&'a str>,
    pub(crate) fullscreen: Option<&'a str>,
    pub(crate) active_window: Option<&'a str>,
    pub(crate) picker: Option<&'a str>,
    pub(crate) ocr: Option<&'a str>,
    pub(crate) scan: Option<&'a str>,
    pub(crate) sticker: Option<&'a str>,
    pub(crate) upscale: Option<&'a str>,
    pub(crate) center: Option<&'a str>,
    pub(crate) ruler: Option<&'a str>,
    pub(crate) scroll_capture: Option<&'a str>,
    pub(crate) ai_redirect: Option<&'a str>,
}

impl<'a> ImportedHotkeyAccelerators<'a> {
    pub(crate) fn pending_tool_hotkey(self, tool: PendingTool) -> Option<&'a str> {
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

#[cfg(not(target_os = "windows"))]
pub(crate) struct CrossPlatformHotkeyListener {
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

#[cfg(target_os = "windows")]
pub(crate) fn start_capture_hotkey_listener(
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
pub(crate) fn start_capture_hotkey_listener(
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
pub(crate) fn cross_platform_hotkey_registrations(
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
pub(crate) fn parse_cross_platform_hotkey(
    accelerator: &str,
) -> Result<global_hotkey::hotkey::HotKey, String> {
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

pub(crate) fn validate_unique_hotkey_bindings(
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
pub(crate) fn linux_hotkey_session_error(
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

pub(crate) fn hotkey_status_summary(accelerators: ImportedHotkeyAccelerators<'_>) -> String {
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
