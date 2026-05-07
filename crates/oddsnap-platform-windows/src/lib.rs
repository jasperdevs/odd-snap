use oddsnap_core::{
    build_recording_output_args, discover_ffmpeg_tools, CapabilityState, FfmpegRecordingRequest,
    NativeUiProfile, PlatformCapabilities, PlatformCapability,
};
use oddsnap_platform::{
    CaptureRegion, CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService,
    HotkeyService, MonitorInfo, PlatformAdapter, PlatformError, ScreenCaptureService,
    VideoRecordingHandle, VideoRecordingRequest, VideoRecordingResult, VideoRecordingService,
    WindowInfo, WindowPickerService,
};

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;
#[cfg(target_os = "windows")]
use std::{
    fs,
    io::{Read, Write},
    mem,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::mpsc::{self, Sender},
    thread::{self, JoinHandle},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

#[cfg(target_os = "windows")]
use windows::core::BOOL;
#[cfg(target_os = "windows")]
use windows::Win32::Foundation::{HANDLE, HWND, LPARAM, RECT, WPARAM};
#[cfg(target_os = "windows")]
use windows::Win32::Graphics::Dwm::{DwmGetWindowAttribute, DWMWA_EXTENDED_FRAME_BOUNDS};
#[cfg(target_os = "windows")]
use windows::Win32::Graphics::Gdi::{
    BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, DeleteDC, DeleteObject,
    EnumDisplayMonitors, GetDC, GetDIBits, GetMonitorInfoW, ReleaseDC, SelectObject, BITMAPINFO,
    BITMAPINFOHEADER, BI_RGB, CAPTUREBLT, DIB_RGB_COLORS, HBITMAP, HDC, HMONITOR, MONITORINFO,
    ROP_CODE, SRCCOPY,
};
#[cfg(target_os = "windows")]
use windows::Win32::System::DataExchange::{
    CloseClipboard, EmptyClipboard, OpenClipboard, SetClipboardData,
};
#[cfg(target_os = "windows")]
use windows::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalUnlock, GMEM_MOVEABLE};
#[cfg(target_os = "windows")]
use windows::Win32::System::Ole::{CF_DIB, CF_UNICODETEXT};
#[cfg(target_os = "windows")]
use windows::Win32::System::Threading::GetCurrentThreadId;
#[cfg(target_os = "windows")]
use windows::Win32::UI::HiDpi::{GetDpiForMonitor, GetDpiForSystem, MDT_EFFECTIVE_DPI};
#[cfg(target_os = "windows")]
use windows::Win32::UI::Input::KeyboardAndMouse::UnregisterHotKey;
#[cfg(target_os = "windows")]
use windows::Win32::UI::Input::KeyboardAndMouse::{
    RegisterHotKey, HOT_KEY_MODIFIERS, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT, MOD_SHIFT, MOD_WIN,
};
#[cfg(target_os = "windows")]
use windows::Win32::UI::WindowsAndMessaging::{
    DrawIconEx, GetCursorInfo, GetForegroundWindow, GetIconInfo, GetMessageW, GetSystemMetrics,
    GetWindowRect, GetWindowTextLengthW, GetWindowTextW, PeekMessageW, PostThreadMessageW,
    CURSORINFO, CURSOR_SHOWING, DI_NORMAL, ICONINFO, MSG, PM_NOREMOVE, SM_CXVIRTUALSCREEN,
    SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN, WM_APP, WM_HOTKEY,
};

#[derive(Debug, Default)]
pub struct WindowsPlatform;

#[cfg(target_os = "windows")]
const CAPTURE_HOTKEY_ID: i32 = 0x0dd5;
#[cfg(target_os = "windows")]
const RECORDING_HOTKEY_ID: i32 = 0x0dd7;
#[cfg(target_os = "windows")]
const HOTKEY_STOP_MESSAGE: u32 = WM_APP + 0x0dd5;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WindowsHotkeyEvent {
    Capture,
    Recording,
}

#[cfg(target_os = "windows")]
#[derive(Debug)]
pub struct WindowsHotkeyListener {
    thread_id: u32,
    join_handle: Option<JoinHandle<()>>,
}

#[cfg(target_os = "windows")]
impl WindowsHotkeyListener {
    pub fn thread_id(&self) -> u32 {
        self.thread_id
    }
}

#[cfg(target_os = "windows")]
impl Drop for WindowsHotkeyListener {
    fn drop(&mut self) {
        unsafe {
            let _ = PostThreadMessageW(self.thread_id, HOTKEY_STOP_MESSAGE, WPARAM(0), LPARAM(0));
        }

        if let Some(join_handle) = self.join_handle.take() {
            let _ = join_handle.join();
        }
    }
}

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
                (
                    PlatformCapability::WindowCapture,
                    CapabilityState::InProgress,
                ),
                (
                    PlatformCapability::ScreenshotExclusion,
                    CapabilityState::Planned,
                ),
                (
                    PlatformCapability::GlobalHotkeys,
                    CapabilityState::InProgress,
                ),
                (PlatformCapability::Tray, CapabilityState::Planned),
                (PlatformCapability::Clipboard, CapabilityState::InProgress),
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
            enumerate_windows_monitors()
        }

        #[cfg(not(target_os = "windows"))]
        {
            Err(PlatformError::Unsupported(
                "Windows monitor enumeration is only available on Windows",
            ))
        }
    }

    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "windows")]
        {
            capture_region_to_bmp(region, false)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = region;
            Err(PlatformError::Unsupported(
                "Windows region capture is only available on Windows",
            ))
        }
    }

    fn capture_region_with_options(
        &self,
        request: CaptureRequest,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "windows")]
        {
            capture_region_to_bmp(request.region, request.include_cursor)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Windows region capture is only available on Windows",
            ))
        }
    }
}

impl WindowPickerService for WindowsPlatform {
    fn active_window(&self) -> Result<WindowInfo, PlatformError> {
        #[cfg(target_os = "windows")]
        {
            active_window_info()
        }

        #[cfg(not(target_os = "windows"))]
        {
            Err(PlatformError::Unsupported(
                "Windows active-window detection is only available on Windows",
            ))
        }
    }
}

impl ClipboardImageService for WindowsPlatform {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError> {
        #[cfg(target_os = "windows")]
        {
            copy_bmp_to_clipboard(image_path)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = image_path;
            Err(PlatformError::Unsupported(
                "Windows image clipboard is only available on Windows",
            ))
        }
    }
}

impl ClipboardTextService for WindowsPlatform {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "windows")]
        {
            copy_text_to_clipboard(text)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = text;
            Err(PlatformError::Unsupported(
                "Windows text clipboard is only available on Windows",
            ))
        }
    }
}

impl VideoRecordingService for WindowsPlatform {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn VideoRecordingHandle>, PlatformError> {
        #[cfg(target_os = "windows")]
        {
            start_windows_desktop_recording(request)
                .map(|handle| Box::new(handle) as Box<dyn VideoRecordingHandle>)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Windows desktop recording is only available on Windows",
            ))
        }
    }
}

impl HotkeyService for WindowsPlatform {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "windows")]
        {
            let (modifiers, key) = parse_hotkey_accelerator(accelerator)?;
            register_windows_hotkey(CAPTURE_HOTKEY_ID, modifiers, key)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = accelerator;
            Err(PlatformError::Unsupported(
                "Windows global hotkey registration is only available on Windows",
            ))
        }
    }
}

#[cfg(target_os = "windows")]
#[derive(Debug)]
struct WindowsVideoRecordingHandle {
    child: Option<Child>,
    output_path: PathBuf,
    stderr_thread: Option<JoinHandle<String>>,
}

#[cfg(target_os = "windows")]
impl VideoRecordingHandle for WindowsVideoRecordingHandle {
    fn output_path(&self) -> &Path {
        &self.output_path
    }

    fn stop(&mut self) -> Result<VideoRecordingResult, PlatformError> {
        let Some(mut child) = self.child.take() else {
            return Ok(VideoRecordingResult {
                output_path: self.output_path.clone(),
            });
        };

        if let Some(stdin) = child.stdin.as_mut() {
            let _ = stdin.write_all(b"q\n");
            let _ = stdin.flush();
        }

        let status = wait_for_child_exit(&mut child, Duration::from_secs(30))?;
        let stderr = self
            .stderr_thread
            .take()
            .and_then(|thread| thread.join().ok())
            .unwrap_or_default();

        if !status.success() {
            return Err(PlatformError::Failed(format!(
                "FFmpeg recording failed with exit code {:?}: {}",
                status.code(),
                stderr.trim()
            )));
        }

        let metadata = fs::metadata(&self.output_path).map_err(|source| {
            PlatformError::Failed(format!(
                "recording output was not created at {}: {source}",
                self.output_path.display()
            ))
        })?;
        if metadata.len() == 0 {
            return Err(PlatformError::Failed(format!(
                "recording output is empty: {}",
                self.output_path.display()
            )));
        }

        Ok(VideoRecordingResult {
            output_path: self.output_path.clone(),
        })
    }

    fn cancel(&mut self) {
        if let Some(mut child) = self.child.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
        if let Some(thread) = self.stderr_thread.take() {
            let _ = thread.join();
        }
        let _ = fs::remove_file(&self.output_path);
    }
}

#[cfg(target_os = "windows")]
impl Drop for WindowsVideoRecordingHandle {
    fn drop(&mut self) {
        if self.child.is_some() {
            self.cancel();
        }
    }
}

#[cfg(target_os = "windows")]
fn start_windows_desktop_recording(
    request: VideoRecordingRequest,
) -> Result<WindowsVideoRecordingHandle, PlatformError> {
    if let Some(parent) = request.output_path.parent() {
        fs::create_dir_all(parent).map_err(|source| {
            PlatformError::Failed(format!("failed to create recording directory: {source}"))
        })?;
    }

    let tools = discover_ffmpeg_tools()
        .ok_or_else(|| PlatformError::Failed("FFmpeg not found on PATH".into()))?;
    let args = windows_desktop_recording_args(&request);
    let mut child = Command::new(&tools.ffmpeg)
        .args(args)
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .creation_flags(0x08000000)
        .spawn()
        .map_err(|source| PlatformError::Failed(format!("failed to start FFmpeg: {source}")))?;

    let stderr = child.stderr.take();
    let stderr_thread = stderr.map(|mut stderr| {
        thread::spawn(move || {
            let mut buffer = String::new();
            let _ = stderr.read_to_string(&mut buffer);
            buffer
        })
    });

    Ok(WindowsVideoRecordingHandle {
        child: Some(child),
        output_path: request.output_path,
        stderr_thread,
    })
}

#[cfg(target_os = "windows")]
fn windows_desktop_recording_args(request: &VideoRecordingRequest) -> Vec<String> {
    let fps = request.fps.clamp(1, 240).to_string();
    let mut input_args = vec![
        "-hide_banner".into(),
        "-f".into(),
        "gdigrab".into(),
        "-framerate".into(),
        fps,
    ];
    if let Some(region) = &request.region {
        input_args.extend([
            "-offset_x".into(),
            region.x.to_string(),
            "-offset_y".into(),
            region.y.to_string(),
            "-video_size".into(),
            format!("{}x{}", region.width, region.height),
        ]);
    }
    input_args.extend(["-i".into(), "desktop".into()]);

    build_recording_output_args(&FfmpegRecordingRequest {
        input_args,
        output_path: request.output_path.clone(),
        format: request.format,
        quality: request.quality,
        fps: request.fps,
    })
}

#[cfg(target_os = "windows")]
fn wait_for_child_exit(
    child: &mut Child,
    timeout: Duration,
) -> Result<std::process::ExitStatus, PlatformError> {
    let deadline = Instant::now() + timeout;
    loop {
        if let Some(status) = child
            .try_wait()
            .map_err(|source| PlatformError::Failed(format!("FFmpeg wait failed: {source}")))?
        {
            return Ok(status);
        }

        if Instant::now() >= deadline {
            let _ = child.kill();
            let _ = child.wait();
            return Err(PlatformError::Failed(
                "FFmpeg recording did not stop within 30 seconds".into(),
            ));
        }

        thread::sleep(Duration::from_millis(100));
    }
}

#[cfg(target_os = "windows")]
pub fn start_capture_hotkey_listener(
    accelerator: &str,
    events: Sender<WindowsHotkeyEvent>,
) -> Result<WindowsHotkeyListener, PlatformError> {
    start_capture_and_recording_hotkey_listener(accelerator, None, events)
}

#[cfg(target_os = "windows")]
pub fn start_capture_and_recording_hotkey_listener(
    capture_accelerator: &str,
    recording_accelerator: Option<&str>,
    events: Sender<WindowsHotkeyEvent>,
) -> Result<WindowsHotkeyListener, PlatformError> {
    let capture = parse_hotkey_accelerator(capture_accelerator)?;
    let recording = recording_accelerator
        .filter(|accelerator| !accelerator.trim().is_empty())
        .map(parse_hotkey_accelerator)
        .transpose()?;
    let (started_sender, started_receiver) = mpsc::sync_channel(1);
    let join_handle = thread::spawn(move || {
        run_hotkey_message_loop(capture, recording, events, started_sender);
    });

    match started_receiver.recv() {
        Ok(Ok(thread_id)) => Ok(WindowsHotkeyListener {
            thread_id,
            join_handle: Some(join_handle),
        }),
        Ok(Err(error)) => {
            let _ = join_handle.join();
            Err(PlatformError::Failed(error))
        }
        Err(error) => {
            let _ = join_handle.join();
            Err(PlatformError::Failed(format!(
                "hotkey listener did not start: {error}"
            )))
        }
    }
}

#[cfg(target_os = "windows")]
fn run_hotkey_message_loop(
    capture: (HOT_KEY_MODIFIERS, u32),
    recording: Option<(HOT_KEY_MODIFIERS, u32)>,
    events: Sender<WindowsHotkeyEvent>,
    started_sender: mpsc::SyncSender<Result<u32, String>>,
) {
    let thread_id = unsafe { GetCurrentThreadId() };
    let mut message = MSG::default();
    unsafe {
        let _ = PeekMessageW(&mut message, None, 0, 0, PM_NOREMOVE);
    }

    if let Err(error) = register_windows_hotkey(CAPTURE_HOTKEY_ID, capture.0, capture.1) {
        let _ = started_sender.send(Err(error.to_string()));
        return;
    }
    if let Some((modifiers, key)) = recording {
        if let Err(error) = register_windows_hotkey(RECORDING_HOTKEY_ID, modifiers, key) {
            let _ = unsafe { UnregisterHotKey(None, CAPTURE_HOTKEY_ID) };
            let _ = started_sender.send(Err(error.to_string()));
            return;
        }
    }

    let _ = started_sender.send(Ok(thread_id));

    loop {
        let status = unsafe { GetMessageW(&mut message, None, 0, 0) };
        if status.0 <= 0 {
            break;
        }

        if message.message == HOTKEY_STOP_MESSAGE {
            break;
        }

        if message.message == WM_HOTKEY {
            match message.wParam {
                WPARAM(value) if value == CAPTURE_HOTKEY_ID as usize => {
                    let _ = events.send(WindowsHotkeyEvent::Capture);
                }
                WPARAM(value) if value == RECORDING_HOTKEY_ID as usize => {
                    let _ = events.send(WindowsHotkeyEvent::Recording);
                }
                _ => {}
            }
        }
    }

    let _ = unsafe { UnregisterHotKey(None, CAPTURE_HOTKEY_ID) };
    let _ = unsafe { UnregisterHotKey(None, RECORDING_HOTKEY_ID) };
}

#[cfg(target_os = "windows")]
fn register_windows_hotkey(
    id: i32,
    modifiers: HOT_KEY_MODIFIERS,
    key: u32,
) -> Result<(), PlatformError> {
    unsafe { RegisterHotKey(None, id, modifiers | MOD_NOREPEAT, key) }
        .map_err(|error| PlatformError::Failed(format!("RegisterHotKey failed: {error}")))
}

#[cfg(all(target_os = "windows", test))]
fn unregister_windows_hotkey(id: i32) -> Result<(), PlatformError> {
    unsafe { UnregisterHotKey(None, id) }
        .map_err(|error| PlatformError::Failed(format!("UnregisterHotKey failed: {error}")))
}

#[cfg(target_os = "windows")]
fn parse_hotkey_accelerator(accelerator: &str) -> Result<(HOT_KEY_MODIFIERS, u32), PlatformError> {
    let mut modifiers = HOT_KEY_MODIFIERS(0);
    let mut key = None;

    for raw_part in accelerator.split('+') {
        let part = raw_part.trim();
        if part.is_empty() {
            continue;
        }

        match part.to_ascii_lowercase().as_str() {
            "alt" => modifiers |= MOD_ALT,
            "ctrl" | "control" => modifiers |= MOD_CONTROL,
            "shift" => modifiers |= MOD_SHIFT,
            "win" | "windows" | "super" => modifiers |= MOD_WIN,
            _ => {
                if key.replace(parse_virtual_key(part)?).is_some() {
                    return Err(PlatformError::Failed(
                        "hotkey accelerator has more than one key".into(),
                    ));
                }
            }
        }
    }

    let key =
        key.ok_or_else(|| PlatformError::Failed("hotkey accelerator is missing a key".into()))?;
    if modifiers.0 == 0 {
        return Err(PlatformError::Failed(
            "hotkey accelerator must include at least one modifier".into(),
        ));
    }

    Ok((modifiers, key))
}

#[cfg(target_os = "windows")]
fn parse_virtual_key(key: &str) -> Result<u32, PlatformError> {
    let upper = key.trim().to_ascii_uppercase();
    if upper == "`" || upper == "BACKTICK" || upper == "OEM_3" {
        return Ok(0xC0);
    }

    if let Some(number) = upper.strip_prefix('F') {
        if let Ok(index) = number.parse::<u32>() {
            if (1..=24).contains(&index) {
                return Ok(0x70 + index - 1);
            }
        }
    }

    let mut chars = upper.chars();
    let Some(character) = chars.next() else {
        return Err(PlatformError::Failed("hotkey key is empty".into()));
    };
    if chars.next().is_none() && character.is_ascii_alphanumeric() {
        return Ok(character as u32);
    }

    Err(PlatformError::Failed(format!(
        "unsupported hotkey key: {key}"
    )))
}

#[cfg(target_os = "windows")]
fn enumerate_windows_monitors() -> Result<Vec<MonitorInfo>, PlatformError> {
    let mut monitors = Vec::<MonitorInfo>::new();
    let data = LPARAM((&mut monitors as *mut Vec<MonitorInfo>) as isize);
    let success = unsafe { EnumDisplayMonitors(None, None, Some(enum_monitor), data) };
    if !success.as_bool() {
        return Err(PlatformError::Failed("EnumDisplayMonitors failed".into()));
    }

    if monitors.is_empty() {
        return fallback_virtual_screen_monitor();
    }

    Ok(monitors)
}

#[cfg(target_os = "windows")]
unsafe extern "system" fn enum_monitor(
    monitor: HMONITOR,
    _: HDC,
    rect: *mut RECT,
    data: LPARAM,
) -> BOOL {
    let monitors = unsafe { &mut *(data.0 as *mut Vec<MonitorInfo>) };
    let bounds = if rect.is_null() {
        match monitor_bounds(monitor) {
            Some(bounds) => bounds,
            None => return true.into(),
        }
    } else {
        unsafe { *rect }
    };

    if let Ok(region) = rect_to_region(bounds) {
        monitors.push(MonitorInfo {
            id: format!("windows-monitor-{}", monitors.len() + 1),
            name: format!("Monitor {}", monitors.len() + 1),
            x: region.x,
            y: region.y,
            width: region.width,
            height: region.height,
            scale_percent: monitor_scale_percent(monitor),
        });
    }

    true.into()
}

#[cfg(target_os = "windows")]
fn monitor_bounds(monitor: HMONITOR) -> Option<RECT> {
    let mut info = MONITORINFO {
        cbSize: mem::size_of::<MONITORINFO>() as u32,
        ..Default::default()
    };
    let success = unsafe { GetMonitorInfoW(monitor, &mut info) };
    success.as_bool().then_some(info.rcMonitor)
}

#[cfg(target_os = "windows")]
fn fallback_virtual_screen_monitor() -> Result<Vec<MonitorInfo>, PlatformError> {
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
        scale_percent: system_scale_percent(),
    }])
}

#[cfg(target_os = "windows")]
fn monitor_scale_percent(monitor: HMONITOR) -> u32 {
    let mut dpi_x = 0u32;
    let mut dpi_y = 0u32;
    let result = unsafe { GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &mut dpi_x, &mut dpi_y) };
    if result.is_ok() && dpi_x > 0 {
        return dpi_to_scale_percent(dpi_x);
    }

    system_scale_percent()
}

#[cfg(target_os = "windows")]
fn system_scale_percent() -> u32 {
    let dpi = unsafe { GetDpiForSystem() };
    dpi_to_scale_percent(dpi.max(96))
}

#[cfg(target_os = "windows")]
fn dpi_to_scale_percent(dpi: u32) -> u32 {
    dpi.saturating_mul(100).saturating_add(48) / 96
}

#[cfg(target_os = "windows")]
fn active_window_info() -> Result<WindowInfo, PlatformError> {
    let hwnd = unsafe { GetForegroundWindow() };
    if hwnd.is_invalid() {
        return Err(PlatformError::Failed(
            "GetForegroundWindow returned no window".into(),
        ));
    }

    let rect = window_capture_rect(hwnd)?;
    Ok(WindowInfo {
        id: format!("{:?}", hwnd.0),
        title: window_title(hwnd),
        bounds: rect_to_region(rect)?,
    })
}

#[cfg(target_os = "windows")]
fn window_capture_rect(hwnd: HWND) -> Result<RECT, PlatformError> {
    let mut dwm_rect = RECT::default();
    let dwm_result = unsafe {
        DwmGetWindowAttribute(
            hwnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            (&mut dwm_rect as *mut RECT).cast(),
            mem::size_of::<RECT>() as u32,
        )
    };
    if dwm_result.is_ok() && rect_has_area(dwm_rect) {
        return Ok(dwm_rect);
    }

    let mut rect = RECT::default();
    unsafe { GetWindowRect(hwnd, &mut rect) }
        .map_err(|error| PlatformError::Failed(format!("GetWindowRect failed: {error}")))?;
    Ok(rect)
}

#[cfg(target_os = "windows")]
fn rect_has_area(rect: RECT) -> bool {
    rect.right > rect.left && rect.bottom > rect.top
}

#[cfg(target_os = "windows")]
fn rect_to_region(rect: RECT) -> Result<CaptureRegion, PlatformError> {
    if !rect_has_area(rect) {
        return Err(PlatformError::Failed("window bounds are empty".into()));
    }

    Ok(CaptureRegion {
        x: rect.left,
        y: rect.top,
        width: u32::try_from(rect.right - rect.left)
            .map_err(|_| PlatformError::Failed("window width is invalid".into()))?,
        height: u32::try_from(rect.bottom - rect.top)
            .map_err(|_| PlatformError::Failed("window height is invalid".into()))?,
    })
}

#[cfg(target_os = "windows")]
fn window_title(hwnd: HWND) -> String {
    let length = unsafe { GetWindowTextLengthW(hwnd) };
    if length <= 0 {
        return String::new();
    }

    let mut buffer = vec![0u16; length as usize + 1];
    let copied = unsafe { GetWindowTextW(hwnd, &mut buffer) };
    if copied <= 0 {
        return String::new();
    }

    String::from_utf16_lossy(&buffer[..copied as usize])
}

#[cfg(target_os = "windows")]
fn copy_bmp_to_clipboard(path: &Path) -> Result<(), PlatformError> {
    let dib = bmp_file_to_dib_bytes(path)?;
    unsafe { copy_dib_to_clipboard(&dib) }
}

#[cfg(target_os = "windows")]
fn bmp_file_to_dib_bytes(path: &Path) -> Result<Vec<u8>, PlatformError> {
    let bytes = fs::read(path)
        .map_err(|source| PlatformError::Failed(format!("failed to read BMP: {source}")))?;
    bmp_bytes_to_dib_bytes(&bytes)
}

#[cfg(target_os = "windows")]
fn bmp_bytes_to_dib_bytes(bytes: &[u8]) -> Result<Vec<u8>, PlatformError> {
    const BMP_FILE_HEADER_SIZE: usize = 14;

    if bytes.len() < BMP_FILE_HEADER_SIZE || &bytes[0..2] != b"BM" {
        return Err(PlatformError::Failed(
            "clipboard image must be a BMP file".into(),
        ));
    }

    let pixel_offset = u32::from_le_bytes(
        bytes[10..14]
            .try_into()
            .map_err(|_| PlatformError::Failed("BMP header is truncated".into()))?,
    ) as usize;
    if pixel_offset < BMP_FILE_HEADER_SIZE || pixel_offset > bytes.len() {
        return Err(PlatformError::Failed("BMP pixel offset is invalid".into()));
    }

    Ok(bytes[BMP_FILE_HEADER_SIZE..].to_vec())
}

#[cfg(target_os = "windows")]
unsafe fn copy_dib_to_clipboard(dib: &[u8]) -> Result<(), PlatformError> {
    if dib.is_empty() {
        return Err(PlatformError::Failed("clipboard DIB is empty".into()));
    }

    let memory = unsafe {
        GlobalAlloc(GMEM_MOVEABLE, dib.len())
            .map_err(|error| PlatformError::Failed(format!("GlobalAlloc failed: {error}")))?
    };
    let locked = unsafe { GlobalLock(memory) };
    if locked.is_null() {
        return Err(PlatformError::Failed("GlobalLock failed".into()));
    }

    unsafe {
        std::ptr::copy_nonoverlapping(dib.as_ptr(), locked.cast(), dib.len());
        let _ = GlobalUnlock(memory);
    }

    unsafe {
        OpenClipboard(None)
            .map_err(|error| PlatformError::Failed(format!("OpenClipboard failed: {error}")))?;
        let set_result = EmptyClipboard()
            .map_err(|error| PlatformError::Failed(format!("EmptyClipboard failed: {error}")))
            .and_then(|()| {
                SetClipboardData(CF_DIB.0 as u32, Some(HANDLE(memory.0)))
                    .map(|_| ())
                    .map_err(|error| {
                        PlatformError::Failed(format!("SetClipboardData failed: {error}"))
                    })
            });
        let close_result = CloseClipboard()
            .map_err(|error| PlatformError::Failed(format!("CloseClipboard failed: {error}")));

        set_result?;
        close_result?;
    }

    Ok(())
}

#[cfg(target_os = "windows")]
fn copy_text_to_clipboard(text: &str) -> Result<(), PlatformError> {
    let mut utf16: Vec<u16> = text.encode_utf16().collect();
    utf16.push(0);
    let bytes = unsafe {
        std::slice::from_raw_parts(
            utf16.as_ptr().cast::<u8>(),
            utf16.len() * mem::size_of::<u16>(),
        )
    };
    unsafe { copy_bytes_to_clipboard(CF_UNICODETEXT.0 as u32, bytes) }
}

#[cfg(target_os = "windows")]
unsafe fn copy_bytes_to_clipboard(format: u32, bytes: &[u8]) -> Result<(), PlatformError> {
    if bytes.is_empty() {
        return Err(PlatformError::Failed("clipboard payload is empty".into()));
    }

    let memory = unsafe {
        GlobalAlloc(GMEM_MOVEABLE, bytes.len())
            .map_err(|error| PlatformError::Failed(format!("GlobalAlloc failed: {error}")))?
    };
    let locked = unsafe { GlobalLock(memory) };
    if locked.is_null() {
        return Err(PlatformError::Failed("GlobalLock failed".into()));
    }

    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), locked.cast(), bytes.len());
        let _ = GlobalUnlock(memory);
    }

    unsafe {
        OpenClipboard(None)
            .map_err(|error| PlatformError::Failed(format!("OpenClipboard failed: {error}")))?;
        let set_result = EmptyClipboard()
            .map_err(|error| PlatformError::Failed(format!("EmptyClipboard failed: {error}")))
            .and_then(|()| {
                SetClipboardData(format, Some(HANDLE(memory.0)))
                    .map(|_| ())
                    .map_err(|error| {
                        PlatformError::Failed(format!("SetClipboardData failed: {error}"))
                    })
            });
        let close_result = CloseClipboard()
            .map_err(|error| PlatformError::Failed(format!("CloseClipboard failed: {error}")));

        set_result?;
        close_result?;
    }

    Ok(())
}

#[cfg(target_os = "windows")]
fn capture_region_to_bmp(
    region: CaptureRegion,
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    if region.width == 0 || region.height == 0 {
        return Err(PlatformError::Failed(
            "capture region must have non-zero width and height".into(),
        ));
    }

    let width = i32::try_from(region.width)
        .map_err(|_| PlatformError::Failed("capture region width is too large".into()))?;
    let height = i32::try_from(region.height)
        .map_err(|_| PlatformError::Failed("capture region height is too large".into()))?;
    let output_path = capture_output_path();

    let bgra = unsafe { read_screen_bgra(region.x, region.y, width, height, include_cursor)? };
    write_bmp(&output_path, region.width, region.height, &bgra)?;

    Ok(CaptureResult {
        image_path: output_path,
        region,
    })
}

#[cfg(target_os = "windows")]
unsafe fn read_screen_bgra(
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    include_cursor: bool,
) -> Result<Vec<u8>, PlatformError> {
    let screen_dc = unsafe { GetDC(None) };
    if screen_dc.is_invalid() {
        return Err(PlatformError::Failed("GetDC failed for the desktop".into()));
    }

    let result =
        unsafe { read_screen_bgra_with_dc(screen_dc, x, y, width, height, include_cursor) };
    unsafe {
        ReleaseDC(None, screen_dc);
    }
    result
}

#[cfg(target_os = "windows")]
unsafe fn read_screen_bgra_with_dc(
    screen_dc: HDC,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    include_cursor: bool,
) -> Result<Vec<u8>, PlatformError> {
    let memory_dc = unsafe { CreateCompatibleDC(Some(screen_dc)) };
    if memory_dc.is_invalid() {
        return Err(PlatformError::Failed("CreateCompatibleDC failed".into()));
    }

    let bitmap = unsafe { CreateCompatibleBitmap(screen_dc, width, height) };
    if bitmap.is_invalid() {
        unsafe {
            let _ = DeleteDC(memory_dc);
        }
        return Err(PlatformError::Failed(
            "CreateCompatibleBitmap failed".into(),
        ));
    }

    let result = unsafe {
        read_screen_bgra_with_bitmap(
            screen_dc,
            memory_dc,
            bitmap,
            CaptureRegion {
                x,
                y,
                width: width as u32,
                height: height as u32,
            },
            include_cursor,
        )
    };

    unsafe {
        let _ = DeleteObject(bitmap.into());
        let _ = DeleteDC(memory_dc);
    }

    result
}

#[cfg(target_os = "windows")]
unsafe fn read_screen_bgra_with_bitmap(
    screen_dc: HDC,
    memory_dc: HDC,
    bitmap: HBITMAP,
    region: CaptureRegion,
    include_cursor: bool,
) -> Result<Vec<u8>, PlatformError> {
    let width = i32::try_from(region.width)
        .map_err(|_| PlatformError::Failed("capture region width is too large".into()))?;
    let height = i32::try_from(region.height)
        .map_err(|_| PlatformError::Failed("capture region height is too large".into()))?;
    let old_object = unsafe { SelectObject(memory_dc, bitmap.into()) };
    if old_object.is_invalid() {
        return Err(PlatformError::Failed(
            "SelectObject failed for capture bitmap".into(),
        ));
    }

    let blt_result = unsafe {
        BitBlt(
            memory_dc,
            0,
            0,
            width,
            height,
            Some(screen_dc),
            region.x,
            region.y,
            ROP_CODE(SRCCOPY.0 | CAPTUREBLT.0),
        )
    };

    if let Err(error) = blt_result {
        unsafe {
            SelectObject(memory_dc, old_object);
        }
        return Err(PlatformError::Failed(format!("BitBlt failed: {error}")));
    }

    if include_cursor {
        unsafe {
            draw_cursor_if_inside(memory_dc, &region);
        }
    }

    let mut info = BITMAPINFO {
        bmiHeader: BITMAPINFOHEADER {
            biSize: mem::size_of::<BITMAPINFOHEADER>() as u32,
            biWidth: width,
            biHeight: -height,
            biPlanes: 1,
            biBitCount: 32,
            biCompression: BI_RGB.0,
            ..Default::default()
        },
        ..Default::default()
    };
    let byte_count = (width as usize)
        .checked_mul(height as usize)
        .and_then(|pixels| pixels.checked_mul(4))
        .ok_or_else(|| PlatformError::Failed("capture region is too large".into()))?;
    let mut pixels = vec![0u8; byte_count];

    let rows = unsafe {
        GetDIBits(
            memory_dc,
            bitmap,
            0,
            height as u32,
            Some(pixels.as_mut_ptr().cast()),
            &mut info,
            DIB_RGB_COLORS,
        )
    };

    unsafe {
        SelectObject(memory_dc, old_object);
    }

    if rows == 0 {
        return Err(PlatformError::Failed("GetDIBits returned no rows".into()));
    }

    Ok(pixels)
}

#[cfg(target_os = "windows")]
unsafe fn draw_cursor_if_inside(target_dc: HDC, capture_region: &CaptureRegion) {
    let mut cursor_info = CURSORINFO {
        cbSize: mem::size_of::<CURSORINFO>() as u32,
        ..Default::default()
    };
    if unsafe { GetCursorInfo(&mut cursor_info) }.is_err() {
        return;
    }
    if cursor_info.flags.0 & CURSOR_SHOWING.0 == 0 || cursor_info.hCursor.is_invalid() {
        return;
    }

    let cursor_x = cursor_info.ptScreenPos.x;
    let cursor_y = cursor_info.ptScreenPos.y;
    let left = capture_region.x;
    let top = capture_region.y;
    let right = left.saturating_add(capture_region.width as i32);
    let bottom = top.saturating_add(capture_region.height as i32);
    if cursor_x < left || cursor_x >= right || cursor_y < top || cursor_y >= bottom {
        return;
    }

    let mut icon_info = ICONINFO::default();
    let cursor_icon = cursor_info.hCursor.into();
    if unsafe { GetIconInfo(cursor_icon, &mut icon_info) }.is_err() {
        return;
    }

    let draw_x = cursor_x - left - icon_info.xHotspot as i32;
    let draw_y = cursor_y - top - icon_info.yHotspot as i32;
    let _ = unsafe {
        DrawIconEx(
            target_dc,
            draw_x,
            draw_y,
            cursor_icon,
            0,
            0,
            0,
            None,
            DI_NORMAL,
        )
    };
    if !icon_info.hbmMask.is_invalid() {
        let _ = unsafe { DeleteObject(icon_info.hbmMask.into()) };
    }
    if !icon_info.hbmColor.is_invalid() {
        let _ = unsafe { DeleteObject(icon_info.hbmColor.into()) };
    }
}

#[cfg(target_os = "windows")]
fn capture_output_path() -> PathBuf {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_nanos())
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-rust-capture-{}-{nanos}.bmp",
        std::process::id()
    ))
}

#[cfg(target_os = "windows")]
fn write_bmp(path: &Path, width: u32, height: u32, bgra: &[u8]) -> Result<(), PlatformError> {
    let pixel_size = width
        .checked_mul(height)
        .and_then(|pixels| pixels.checked_mul(4))
        .ok_or_else(|| PlatformError::Failed("BMP output is too large".into()))?;
    if bgra.len() != pixel_size as usize {
        return Err(PlatformError::Failed(
            "pixel buffer size does not match BMP dimensions".into(),
        ));
    }

    let file_header_size = 14u32;
    let dib_header_size = 40u32;
    let pixel_offset = file_header_size + dib_header_size;
    let file_size = pixel_offset
        .checked_add(pixel_size)
        .ok_or_else(|| PlatformError::Failed("BMP file is too large".into()))?;

    let mut bytes = Vec::with_capacity(file_size as usize);
    bytes.extend_from_slice(b"BM");
    bytes.extend_from_slice(&file_size.to_le_bytes());
    bytes.extend_from_slice(&[0, 0, 0, 0]);
    bytes.extend_from_slice(&pixel_offset.to_le_bytes());
    bytes.extend_from_slice(&dib_header_size.to_le_bytes());
    bytes.extend_from_slice(&(width as i32).to_le_bytes());
    bytes.extend_from_slice(&(-(height as i32)).to_le_bytes());
    bytes.extend_from_slice(&1u16.to_le_bytes());
    bytes.extend_from_slice(&32u16.to_le_bytes());
    bytes.extend_from_slice(&0u32.to_le_bytes());
    bytes.extend_from_slice(&pixel_size.to_le_bytes());
    bytes.extend_from_slice(&0i32.to_le_bytes());
    bytes.extend_from_slice(&0i32.to_le_bytes());
    bytes.extend_from_slice(&0u32.to_le_bytes());
    bytes.extend_from_slice(&0u32.to_le_bytes());
    bytes.extend_from_slice(bgra);

    fs::write(path, bytes)
        .map_err(|source| PlatformError::Failed(format!("failed to write BMP: {source}")))
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{CapabilityState, PlatformCapability, RecordingFormat, RecordingQuality};
    use oddsnap_platform::{PlatformAdapter, VideoRecordingRequest, VideoRecordingService};

    use super::WindowsPlatform;

    #[test]
    fn windows_adapter_tracks_early_capture_work() {
        let adapter = WindowsPlatform;
        let capabilities = adapter.capabilities();

        assert_eq!(
            capabilities.state(PlatformCapability::ScreenCapture),
            CapabilityState::InProgress
        );
        assert_eq!(
            capabilities.state(PlatformCapability::Clipboard),
            CapabilityState::InProgress
        );
        assert_eq!(
            capabilities.state(PlatformCapability::WindowCapture),
            CapabilityState::InProgress
        );
        assert_eq!(
            capabilities.state(PlatformCapability::GlobalHotkeys),
            CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn windows_monitor_enumeration_returns_virtual_screen() {
        use oddsnap_platform::ScreenCaptureService;

        let adapter = WindowsPlatform;
        let monitors = adapter.monitors().expect("enumerate monitors");

        assert!(!monitors.is_empty());
        assert!(monitors.iter().all(|monitor| monitor.width > 0));
        assert!(monitors.iter().all(|monitor| monitor.height > 0));
        assert!(monitors.iter().all(|monitor| monitor.scale_percent >= 100));
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn dpi_to_scale_percent_rounds_common_windows_scales() {
        assert_eq!(super::dpi_to_scale_percent(96), 100);
        assert_eq!(super::dpi_to_scale_percent(120), 125);
        assert_eq!(super::dpi_to_scale_percent(144), 150);
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn parse_hotkey_accelerator_supports_legacy_default() {
        let (modifiers, key) = super::parse_hotkey_accelerator("Alt+`").expect("parse hotkey");

        assert!(modifiers.contains(windows::Win32::UI::Input::KeyboardAndMouse::MOD_ALT));
        assert_eq!(key, 0xC0);
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn parse_hotkey_accelerator_rejects_modifierless_keys() {
        let error = super::parse_hotkey_accelerator("F24").expect_err("missing modifier");

        assert!(error.to_string().contains("modifier"));
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn windows_desktop_recording_args_use_gdigrab_and_configured_format() {
        let args = super::windows_desktop_recording_args(&VideoRecordingRequest {
            output_path: std::path::PathBuf::from("capture.webm"),
            region: Some(oddsnap_platform::CaptureRegion {
                x: -10,
                y: 20,
                width: 640,
                height: 480,
            }),
            format: RecordingFormat::WebM,
            quality: RecordingQuality::P1080,
            fps: 60,
            record_microphone: false,
            record_desktop_audio: false,
            microphone_device_id: None,
            desktop_audio_device_id: None,
        });

        assert!(args.windows(2).any(|pair| pair == ["-f", "gdigrab"]));
        assert!(args.windows(2).any(|pair| pair == ["-framerate", "60"]));
        assert!(args.windows(2).any(|pair| pair == ["-offset_x", "-10"]));
        assert!(args.windows(2).any(|pair| pair == ["-offset_y", "20"]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-video_size", "640x480"]));
        assert!(args.windows(2).any(|pair| pair == ["-vf", "scale=-2:1080"]));
        assert!(args.windows(2).any(|pair| pair == ["-c:v", "libvpx-vp9"]));
        assert_eq!(args.last().map(String::as_str), Some("capture.webm"));
    }

    #[test]
    #[ignore = "starts FFmpeg and records the local Windows desktop for about one second"]
    #[cfg(target_os = "windows")]
    fn windows_desktop_recording_can_start_and_stop_if_ffmpeg_exists() {
        if oddsnap_core::discover_ffmpeg_tools().is_none() {
            return;
        }

        let output = std::env::temp_dir().join(format!(
            "oddsnap-rust-recording-smoke-{}.mp4",
            std::process::id()
        ));
        let _ = std::fs::remove_file(&output);
        let adapter = WindowsPlatform;
        let mut handle = adapter
            .start_desktop_recording(VideoRecordingRequest {
                output_path: output.clone(),
                region: Some(oddsnap_platform::CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 320,
                    height: 240,
                }),
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::P480,
                fps: 10,
                record_microphone: false,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            })
            .expect("start desktop recording");

        std::thread::sleep(std::time::Duration::from_secs(1));
        let result = handle.stop().expect("stop desktop recording");

        assert_eq!(result.output_path, output);
        assert!(
            std::fs::metadata(&result.output_path)
                .expect("recording metadata")
                .len()
                > 0
        );
        let _ = std::fs::remove_file(result.output_path);
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn windows_region_capture_writes_bmp_file() {
        use std::fs;

        use oddsnap_platform::{CaptureRegion, ScreenCaptureService};

        let adapter = WindowsPlatform;
        let result = adapter
            .capture_region(CaptureRegion {
                x: 0,
                y: 0,
                width: 2,
                height: 2,
            })
            .expect("capture tiny region");
        let bytes = fs::read(&result.image_path).expect("read captured bmp");
        fs::remove_file(&result.image_path).expect("remove captured bmp");

        assert_eq!(&bytes[0..2], b"BM");
        assert_eq!(result.region.width, 2);
        assert_eq!(result.region.height, 2);
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn rect_to_region_rejects_empty_bounds() {
        let error = super::rect_to_region(windows::Win32::Foundation::RECT {
            left: 10,
            top: 10,
            right: 10,
            bottom: 20,
        })
        .expect_err("empty rect should fail");

        assert!(error.to_string().contains("empty"));
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn bmp_bytes_to_dib_bytes_removes_file_header() {
        let mut bytes = Vec::new();
        bytes.extend_from_slice(b"BM");
        bytes.extend_from_slice(&58u32.to_le_bytes());
        bytes.extend_from_slice(&[0, 0, 0, 0]);
        bytes.extend_from_slice(&54u32.to_le_bytes());
        bytes.extend_from_slice(&40u32.to_le_bytes());
        bytes.extend_from_slice(&1i32.to_le_bytes());
        bytes.extend_from_slice(&(-1i32).to_le_bytes());
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&32u16.to_le_bytes());
        bytes.extend_from_slice(&0u32.to_le_bytes());
        bytes.extend_from_slice(&4u32.to_le_bytes());
        bytes.extend_from_slice(&[0; 16]);
        bytes.extend_from_slice(&[1, 2, 3, 4]);

        let dib = super::bmp_bytes_to_dib_bytes(&bytes).expect("DIB bytes");

        assert_eq!(dib.len(), bytes.len() - 14);
        assert_eq!(&dib[0..4], &40u32.to_le_bytes());
        assert_eq!(&dib[dib.len() - 4..], &[1, 2, 3, 4]);
    }

    #[test]
    #[ignore = "depends on the currently focused local desktop window"]
    #[cfg(target_os = "windows")]
    fn windows_active_window_capture_writes_bmp_file() {
        use std::fs;

        use oddsnap_platform::WindowCaptureService;

        let adapter = WindowsPlatform;
        let result = adapter
            .capture_active_window()
            .expect("capture active window");
        let bytes = fs::read(&result.image_path).expect("read active-window bmp");
        fs::remove_file(&result.image_path).expect("remove active-window bmp");

        assert_eq!(&bytes[0..2], b"BM");
        assert!(result.region.width > 0);
        assert!(result.region.height > 0);
    }

    #[test]
    #[ignore = "writes a tiny image to the local Windows clipboard"]
    #[cfg(target_os = "windows")]
    fn windows_region_capture_can_copy_to_clipboard() {
        use std::fs;

        use oddsnap_platform::{CaptureRegion, ClipboardImageService, ScreenCaptureService};

        let adapter = WindowsPlatform;
        let result = adapter
            .capture_region(CaptureRegion {
                x: 0,
                y: 0,
                width: 2,
                height: 2,
            })
            .expect("capture tiny region");

        adapter
            .copy_image_to_clipboard(&result.image_path)
            .expect("copy capture");
        fs::remove_file(&result.image_path).expect("remove captured bmp");
    }

    #[test]
    #[ignore = "captures a tiny local desktop region with cursor drawing enabled"]
    #[cfg(target_os = "windows")]
    fn windows_region_capture_accepts_cursor_option() {
        use std::fs;

        use oddsnap_platform::{CaptureRegion, CaptureRequest, ScreenCaptureService};

        let adapter = WindowsPlatform;
        let result = adapter
            .capture_region_with_options(CaptureRequest {
                region: CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 2,
                    height: 2,
                },
                include_cursor: true,
            })
            .expect("capture tiny region with cursor option");
        let bytes = fs::read(&result.image_path).expect("read cursor-option bmp");
        fs::remove_file(&result.image_path).expect("remove captured bmp");

        assert_eq!(&bytes[0..2], b"BM");
    }

    #[test]
    #[ignore = "writes text to the local Windows clipboard"]
    #[cfg(target_os = "windows")]
    fn windows_text_clipboard_can_copy_text() {
        use oddsnap_platform::ClipboardTextService;

        let adapter = WindowsPlatform;

        adapter
            .copy_text_to_clipboard("OddSnap Rust clipboard smoke")
            .expect("copy text");
    }

    #[test]
    #[ignore = "registers and unregisters a process-local Windows global hotkey"]
    #[cfg(target_os = "windows")]
    fn windows_hotkey_can_register_and_unregister() {
        let (modifiers, key) =
            super::parse_hotkey_accelerator("Alt+Shift+F24").expect("parse hotkey");

        super::register_windows_hotkey(0x0dd6, modifiers, key).expect("register hotkey");
        super::unregister_windows_hotkey(0x0dd6).expect("unregister hotkey");
    }

    #[test]
    #[ignore = "registers a process-local Windows global hotkey and posts a synthetic WM_HOTKEY"]
    #[cfg(target_os = "windows")]
    fn windows_hotkey_listener_dispatches_capture_event() {
        use std::sync::mpsc;
        use std::time::Duration;

        use windows::Win32::Foundation::{LPARAM, WPARAM};
        use windows::Win32::UI::WindowsAndMessaging::{PostThreadMessageW, WM_HOTKEY};

        let (sender, receiver) = mpsc::channel();
        let listener =
            super::start_capture_hotkey_listener("Alt+Shift+F24", sender).expect("listener");
        unsafe {
            PostThreadMessageW(
                listener.thread_id(),
                WM_HOTKEY,
                WPARAM(super::CAPTURE_HOTKEY_ID as usize),
                LPARAM(0),
            )
            .expect("post hotkey message");
        }

        let event = receiver
            .recv_timeout(Duration::from_secs(2))
            .expect("receive hotkey event");

        assert_eq!(event, super::WindowsHotkeyEvent::Capture);
    }

    #[test]
    #[ignore = "registers process-local capture and recording hotkeys and posts synthetic WM_HOTKEY messages"]
    #[cfg(target_os = "windows")]
    fn windows_hotkey_listener_dispatches_recording_event() {
        use std::sync::mpsc;
        use std::time::Duration;

        use windows::Win32::Foundation::{LPARAM, WPARAM};
        use windows::Win32::UI::WindowsAndMessaging::{PostThreadMessageW, WM_HOTKEY};

        let (sender, receiver) = mpsc::channel();
        let listener = super::start_capture_and_recording_hotkey_listener(
            "Alt+Shift+F23",
            Some("Alt+Shift+F24"),
            sender,
        )
        .expect("listener");
        unsafe {
            PostThreadMessageW(
                listener.thread_id(),
                WM_HOTKEY,
                WPARAM(super::RECORDING_HOTKEY_ID as usize),
                LPARAM(0),
            )
            .expect("post recording hotkey message");
        }

        let event = receiver
            .recv_timeout(Duration::from_secs(2))
            .expect("receive recording hotkey event");

        assert_eq!(event, super::WindowsHotkeyEvent::Recording);
    }

    #[test]
    #[ignore = "captures the full local desktop and writes a large temporary BMP"]
    #[cfg(target_os = "windows")]
    fn windows_full_screen_capture_writes_bmp_file() {
        use std::fs;

        use oddsnap_platform::ScreenCaptureService;

        let adapter = WindowsPlatform;
        let result = adapter
            .capture_all_screens()
            .expect("capture full virtual screen");
        let bytes = fs::read(&result.image_path).expect("read full-screen bmp");
        fs::remove_file(&result.image_path).expect("remove full-screen bmp");

        assert_eq!(&bytes[0..2], b"BM");
        assert!(result.region.width > 0);
        assert!(result.region.height > 0);
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
