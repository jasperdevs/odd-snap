use std::path::Path;
#[cfg(target_os = "linux")]
use std::{
    fs,
    io::{Read, Write},
    path::PathBuf,
    process::{Child, Command, Stdio},
    thread,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

#[cfg(any(target_os = "linux", test))]
use oddsnap_core::{build_recording_output_args, FfmpegRecordingRequest};
use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService,
    ColorPickerService, ColorSample, HotkeyService, MonitorInfo, OcrTextRequest, OcrTextResult,
    OcrTextService, OverlayWindowHandle, OverlayWindowRequest, PlatformAdapter, PlatformError,
    RegionOverlayService, RegionSelectionService, ScreenCaptureService, VideoRecordingRequest,
    VideoRecordingService, WindowInfo, WindowPickerService,
};

#[derive(Debug, Default)]
pub struct LinuxPlatform;

impl PlatformAdapter for LinuxPlatform {
    fn name(&self) -> &'static str {
        "Linux"
    }

    fn native_ui_profile(&self) -> NativeUiProfile {
        NativeUiProfile::for_target("linux")
    }

    fn capabilities(&self) -> PlatformCapabilities {
        PlatformCapabilities {
            os: "linux".into(),
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

impl ScreenCaptureService for LinuxPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_monitor_enumeration()
        }

        #[cfg(not(target_os = "linux"))]
        {
            Err(PlatformError::Unsupported(
                "Linux monitor enumeration is only available on Linux",
            ))
        }
    }

    fn capture_region(&self, region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
        self.capture_region_with_options(CaptureRequest {
            region,
            include_cursor: false,
        })
    }

    fn capture_region_with_options(
        &self,
        request: CaptureRequest,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_screenshot(Some(&request.region), request.include_cursor)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Linux region capture is only available on Linux",
            ))
        }
    }

    fn capture_all_screens_with_cursor(
        &self,
        include_cursor: bool,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_screenshot(None, include_cursor)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = include_cursor;
            Err(PlatformError::Unsupported(
                "Linux full-screen capture is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn run_linux_monitor_enumeration() -> Result<Vec<MonitorInfo>, PlatformError> {
    let output = run_linux_command_stdout("xrandr", &["--query"])?;
    let monitors = parse_xrandr_query_monitors(&output)?;
    if monitors.is_empty() {
        Err(PlatformError::Failed(
            "xrandr did not report any connected monitors".into(),
        ))
    } else {
        Ok(monitors)
    }
}

#[cfg(any(target_os = "linux", test))]
fn parse_xrandr_query_monitors(output: &str) -> Result<Vec<MonitorInfo>, PlatformError> {
    let mut monitors = Vec::new();
    for line in output.lines() {
        let Some(monitor) = parse_xrandr_connected_monitor(line) else {
            continue;
        };
        monitors.push(monitor);
    }
    Ok(monitors)
}

#[cfg(any(target_os = "linux", test))]
fn parse_xrandr_connected_monitor(line: &str) -> Option<MonitorInfo> {
    let mut parts = line.split_whitespace();
    let name = parts.next()?;
    if parts.next()? != "connected" {
        return None;
    }

    let (width, height, x, y) = parts.find_map(parse_xrandr_geometry)?;

    Some(MonitorInfo {
        id: name.into(),
        name: name.into(),
        x,
        y,
        width,
        height,
        scale_percent: 100,
    })
}

#[cfg(any(target_os = "linux", test))]
fn parse_xrandr_geometry(token: &str) -> Option<(u32, u32, i32, i32)> {
    let x_separator = token.find('x')?;
    let width = token[..x_separator].parse::<u32>().ok()?;
    let rest = &token[x_separator + 1..];
    let first_sign = rest.find(['+', '-'])?;
    let height = rest[..first_sign].parse::<u32>().ok()?;
    let coordinates = &rest[first_sign..];
    let second_sign = coordinates[1..].find(['+', '-']).map(|index| index + 1)?;
    let x = coordinates[..second_sign].parse::<i32>().ok()?;
    let y = coordinates[second_sign..].parse::<i32>().ok()?;

    if width == 0 || height == 0 {
        return None;
    }
    Some((width, height, x, y))
}

#[cfg(target_os = "linux")]
fn run_linux_screenshot(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    let output_path = linux_capture_output_path();
    let mut errors = Vec::new();

    for (program, args) in linux_screenshot_commands(region, include_cursor, &output_path) {
        match Command::new(&program).args(&args).status() {
            Ok(status) if status.success() => {
                match linux_capture_region_from_output(region, &output_path) {
                    Ok(capture_region) => {
                        return Ok(CaptureResult {
                            image_path: output_path,
                            region: capture_region,
                        });
                    }
                    Err(error) => errors.push(format!("{program} wrote invalid output: {error}")),
                }
            }
            Ok(status) => errors.push(format!("{program} exited with status {status}")),
            Err(error) => errors.push(format!("{program}: {error}")),
        }
    }

    Err(PlatformError::Failed(format!(
        "no Linux screenshot command succeeded: {}",
        errors.join("; ")
    )))
}

#[cfg(any(target_os = "linux", test))]
fn linux_capture_region_from_output(
    requested_region: Option<&CaptureRegion>,
    output_path: &Path,
) -> Result<CaptureRegion, PlatformError> {
    let (width, height) = oddsnap_platform::image_file_dimensions(output_path)?;
    match requested_region {
        Some(region) => Ok(region.clone()),
        None => Ok(CaptureRegion {
            x: 0,
            y: 0,
            width,
            height,
        }),
    }
}

#[cfg(any(target_os = "linux", test))]
fn linux_screenshot_commands(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
    output_path: &Path,
) -> Vec<(String, Vec<String>)> {
    let path = output_path.display().to_string();
    match region {
        Some(region) => vec![
            (
                "grim".into(),
                vec![
                    "-g".into(),
                    format!(
                        "{},{} {}x{}",
                        region.x, region.y, region.width, region.height
                    ),
                    path.clone(),
                ],
            ),
            (
                "scrot".into(),
                scrot_args(Some(region), include_cursor, output_path),
            ),
        ],
        None => vec![
            ("grim".into(), vec![path.clone()]),
            (
                "gnome-screenshot".into(),
                gnome_screenshot_args(include_cursor, output_path),
            ),
            (
                "spectacle".into(),
                vec!["-b".into(), "-n".into(), "-o".into(), path.clone()],
            ),
            (
                "scrot".into(),
                scrot_args(None, include_cursor, output_path),
            ),
        ],
    }
}

#[cfg(any(target_os = "linux", test))]
fn gnome_screenshot_args(include_cursor: bool, output_path: &Path) -> Vec<String> {
    let mut args = vec!["-f".into(), output_path.display().to_string()];
    if include_cursor {
        args.insert(0, "-p".into());
    }
    args
}

#[cfg(any(target_os = "linux", test))]
fn scrot_args(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
    output_path: &Path,
) -> Vec<String> {
    let mut args = Vec::new();
    if include_cursor {
        args.push("-p".into());
    }
    if let Some(region) = region {
        args.push("-a".into());
        args.push(format!(
            "{},{},{},{}",
            region.x, region.y, region.width, region.height
        ));
    }
    args.push(output_path.display().to_string());
    args
}

#[cfg(target_os = "linux")]
fn linux_capture_output_path() -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-linux-capture-{}-{:09}.png",
        duration.as_secs(),
        duration.subsec_nanos()
    ))
}

impl WindowPickerService for LinuxPlatform {
    fn active_window(&self) -> Result<WindowInfo, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_active_window_detection()
        }

        #[cfg(not(target_os = "linux"))]
        {
            Err(PlatformError::Unsupported(
                "Linux active-window detection is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn run_linux_active_window_detection() -> Result<WindowInfo, PlatformError> {
    let window_id = run_linux_command_stdout("xdotool", &["getactivewindow"])?;
    let window_id = window_id.trim();
    if window_id.is_empty() {
        return Err(PlatformError::Failed(
            "xdotool did not report an active window id".into(),
        ));
    }

    let geometry =
        run_linux_command_stdout("xdotool", &["getwindowgeometry", "--shell", window_id])?;
    let title = run_linux_command_stdout("xdotool", &["getwindowname", window_id])
        .unwrap_or_default()
        .trim()
        .to_string();

    parse_xdotool_window_geometry(window_id, &title, &geometry)
}

#[cfg(target_os = "linux")]
fn run_linux_command_stdout(program: &str, args: &[&str]) -> Result<String, PlatformError> {
    let output = Command::new(program)
        .args(args)
        .output()
        .map_err(|source| PlatformError::Failed(format!("failed to start {program}: {source}")))?;

    if !output.status.success() {
        return Err(PlatformError::Failed(format!(
            "{program} exited with status {}",
            output.status
        )));
    }

    Ok(String::from_utf8_lossy(&output.stdout).into_owned())
}

#[cfg(any(target_os = "linux", test))]
fn parse_xdotool_window_geometry(
    window_id: &str,
    title: &str,
    geometry: &str,
) -> Result<WindowInfo, PlatformError> {
    let x = parse_xdotool_i32(geometry, "X")?;
    let y = parse_xdotool_i32(geometry, "Y")?;
    let width = parse_xdotool_u32(geometry, "WIDTH")?;
    let height = parse_xdotool_u32(geometry, "HEIGHT")?;
    if width == 0 || height == 0 {
        return Err(PlatformError::Failed(
            "xdotool reported an empty active-window rectangle".into(),
        ));
    }

    Ok(WindowInfo {
        id: window_id.trim().into(),
        title: title.trim().into(),
        bounds: CaptureRegion {
            x,
            y,
            width,
            height,
        },
    })
}

#[cfg(any(target_os = "linux", test))]
fn parse_xdotool_i32(geometry: &str, key: &str) -> Result<i32, PlatformError> {
    parse_xdotool_value(geometry, key)?
        .parse::<i32>()
        .map_err(|source| {
            PlatformError::Failed(format!("xdotool reported invalid {key} value: {source}"))
        })
}

#[cfg(any(target_os = "linux", test))]
fn parse_xdotool_u32(geometry: &str, key: &str) -> Result<u32, PlatformError> {
    parse_xdotool_value(geometry, key)?
        .parse::<u32>()
        .map_err(|source| {
            PlatformError::Failed(format!("xdotool reported invalid {key} value: {source}"))
        })
}

#[cfg(any(target_os = "linux", test))]
fn parse_xdotool_value<'a>(geometry: &'a str, key: &str) -> Result<&'a str, PlatformError> {
    geometry
        .lines()
        .filter_map(|line| line.split_once('='))
        .find_map(|(candidate, value)| (candidate == key).then_some(value.trim()))
        .ok_or_else(|| PlatformError::Failed(format!("xdotool output missing {key}")))
}

impl ClipboardImageService for LinuxPlatform {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError> {
        #[cfg(target_os = "linux")]
        {
            copy_image_to_linux_clipboard(image_path)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = image_path;
            Err(PlatformError::Unsupported(
                "Linux image clipboard is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn copy_image_to_linux_clipboard(image_path: &Path) -> Result<(), PlatformError> {
    let png = oddsnap_platform::image_file_to_png_bytes(image_path)?;
    let mut errors = Vec::new();

    for (program, args) in linux_image_clipboard_commands() {
        match run_clipboard_bytes_command(program, &args, &png) {
            Ok(()) => return Ok(()),
            Err(error) => errors.push(format!("{program}: {error}")),
        }
    }

    Err(PlatformError::Failed(format!(
        "no Linux image clipboard command succeeded: {}",
        errors.join("; ")
    )))
}

#[cfg(any(target_os = "linux", test))]
fn linux_image_clipboard_commands() -> Vec<(&'static str, Vec<&'static str>)> {
    vec![
        ("wl-copy", vec!["--type", "image/png"]),
        (
            "xclip",
            vec!["-selection", "clipboard", "-t", "image/png", "-i"],
        ),
    ]
}

impl ClipboardTextService for LinuxPlatform {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "linux")]
        {
            copy_text_to_linux_clipboard(text)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = text;
            Err(PlatformError::Unsupported(
                "Linux text clipboard is only available on Linux",
            ))
        }
    }
}

impl OcrTextService for LinuxPlatform {
    fn recognize_text(&self, request: OcrTextRequest) -> Result<OcrTextResult, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            oddsnap_platform::recognize_text_with_tesseract(&request)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Linux OCR is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn copy_text_to_linux_clipboard(text: &str) -> Result<(), PlatformError> {
    let mut errors = Vec::new();

    for (program, args) in [
        ("wl-copy", &[] as &[&str]),
        ("xclip", &["-selection", "clipboard"] as &[&str]),
        ("xsel", &["--clipboard", "--input"] as &[&str]),
    ] {
        match run_clipboard_command(program, args, text) {
            Ok(()) => return Ok(()),
            Err(error) => errors.push(format!("{program}: {error}")),
        }
    }

    Err(PlatformError::Failed(format!(
        "no Linux clipboard command succeeded: {}",
        errors.join("; ")
    )))
}

#[cfg(target_os = "linux")]
fn run_clipboard_command(program: &str, args: &[&str], text: &str) -> Result<(), PlatformError> {
    run_clipboard_bytes_command(program, args, text.as_bytes())
}

#[cfg(target_os = "linux")]
fn run_clipboard_bytes_command(
    program: &str,
    args: &[&str],
    bytes: &[u8],
) -> Result<(), PlatformError> {
    let mut child = Command::new(program)
        .args(args)
        .stdin(Stdio::piped())
        .spawn()
        .map_err(|source| PlatformError::Failed(format!("failed to start command: {source}")))?;

    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| PlatformError::Failed("failed to open clipboard command stdin".into()))?;
    stdin.write_all(bytes).map_err(|source| {
        PlatformError::Failed(format!("failed to write clipboard data: {source}"))
    })?;
    drop(stdin);

    let status = child
        .wait()
        .map_err(|source| PlatformError::Failed(format!("failed to wait for command: {source}")))?;
    if status.success() {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "exited with status {status}"
        )))
    }
}

impl ColorPickerService for LinuxPlatform {
    fn sample_cursor_color(&self) -> Result<ColorSample, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_color_picker()
        }

        #[cfg(not(target_os = "linux"))]
        {
            Err(PlatformError::Unsupported(
                "Linux color picker is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn run_linux_color_picker() -> Result<ColorSample, PlatformError> {
    let cursor = run_linux_command_stdout("xdotool", &["getmouselocation", "--shell"])?;
    let (x, y) = parse_xdotool_mouse_location(&cursor)?;
    let capture = run_linux_screenshot(
        Some(&CaptureRegion {
            x,
            y,
            width: 1,
            height: 1,
        }),
        false,
    )?;
    let sample = oddsnap_platform::image_file_top_left_color_sample(&capture.image_path);
    let _ = std::fs::remove_file(capture.image_path);
    sample
}

#[cfg(any(target_os = "linux", test))]
fn parse_xdotool_mouse_location(output: &str) -> Result<(i32, i32), PlatformError> {
    Ok((
        parse_xdotool_i32(output, "X")?,
        parse_xdotool_i32(output, "Y")?,
    ))
}

impl HotkeyService for LinuxPlatform {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError> {
        let _ = accelerator;
        Err(PlatformError::Unsupported(
            "Linux global hotkey registration is not implemented yet",
        ))
    }
}

impl VideoRecordingService for LinuxPlatform {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn oddsnap_platform::VideoRecordingHandle>, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            start_linux_desktop_recording(request)
                .map(|handle| Box::new(handle) as Box<dyn oddsnap_platform::VideoRecordingHandle>)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Linux desktop recording is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
#[derive(Debug)]
struct LinuxVideoRecordingHandle {
    child: Option<Child>,
    output_path: PathBuf,
    stderr_thread: Option<thread::JoinHandle<String>>,
}

#[cfg(target_os = "linux")]
impl oddsnap_platform::VideoRecordingHandle for LinuxVideoRecordingHandle {
    fn output_path(&self) -> &Path {
        &self.output_path
    }

    fn stop(&mut self) -> Result<oddsnap_platform::VideoRecordingResult, PlatformError> {
        let Some(mut child) = self.child.take() else {
            return Err(PlatformError::Failed(
                "recording process is not running".into(),
            ));
        };

        if let Some(stdin) = child.stdin.as_mut() {
            let _ = stdin.write_all(b"q\n");
            let _ = stdin.flush();
        }

        let status = wait_for_linux_child_exit(&mut child, Duration::from_secs(30))?;
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

        Ok(oddsnap_platform::VideoRecordingResult {
            output_path: self.output_path.clone(),
        })
    }

    fn cancel(&mut self) {
        self.cancel_process_and_outputs();
    }
}

#[cfg(target_os = "linux")]
impl LinuxVideoRecordingHandle {
    fn cancel_process_and_outputs(&mut self) {
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

#[cfg(target_os = "linux")]
impl Drop for LinuxVideoRecordingHandle {
    fn drop(&mut self) {
        if self.child.is_some() {
            self.cancel_process_and_outputs();
        }
    }
}

#[cfg(target_os = "linux")]
fn start_linux_desktop_recording(
    request: VideoRecordingRequest,
) -> Result<LinuxVideoRecordingHandle, PlatformError> {
    if let Some(parent) = request.output_path.parent() {
        fs::create_dir_all(parent).map_err(|source| {
            PlatformError::Failed(format!("failed to create recording directory: {source}"))
        })?;
    }

    let tools = oddsnap_core::discover_ffmpeg_tools()
        .ok_or_else(|| PlatformError::Failed("FFmpeg not found on PATH".into()))?;
    let args = linux_desktop_recording_args(&request);
    let mut child = Command::new(&tools.ffmpeg)
        .args(args)
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
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

    Ok(LinuxVideoRecordingHandle {
        child: Some(child),
        output_path: request.output_path,
        stderr_thread,
    })
}

#[cfg(target_os = "linux")]
fn linux_desktop_recording_args(request: &VideoRecordingRequest) -> Vec<String> {
    linux_desktop_recording_args_for_display(request, &linux_x11_display_name())
}

#[cfg(any(target_os = "linux", test))]
fn linux_desktop_recording_args_for_display(
    request: &VideoRecordingRequest,
    display: &str,
) -> Vec<String> {
    let fps = request.fps.clamp(1, 240).to_string();
    let mut input_args = vec![
        "-hide_banner".into(),
        "-f".into(),
        "x11grab".into(),
        "-draw_mouse".into(),
        "1".into(),
        "-framerate".into(),
        fps,
    ];
    if let Some(region) = &request.region {
        input_args.extend([
            "-video_size".into(),
            format!("{}x{}", region.width, region.height),
        ]);
    }
    input_args.extend([
        "-i".into(),
        linux_x11grab_input(display, request.region.as_ref()),
    ]);

    build_recording_output_args(&FfmpegRecordingRequest {
        input_args,
        output_path: request.output_path.clone(),
        format: request.format,
        quality: request.quality,
        fps: request.fps,
    })
}

#[cfg(target_os = "linux")]
fn linux_x11_display_name() -> String {
    std::env::var("DISPLAY")
        .ok()
        .filter(|display| !display.trim().is_empty())
        .unwrap_or_else(|| ":0.0".into())
}

#[cfg(any(target_os = "linux", test))]
fn linux_x11grab_input(display: &str, region: Option<&CaptureRegion>) -> String {
    match region {
        Some(region) => format!("{display}+{},{}", region.x, region.y),
        None => display.into(),
    }
}

#[cfg(target_os = "linux")]
fn wait_for_linux_child_exit(
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

impl RegionOverlayService for LinuxPlatform {
    fn create_overlay_window(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Box<dyn OverlayWindowHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux region overlay is not implemented yet",
        ))
    }
}

impl RegionSelectionService for LinuxPlatform {
    fn select_region(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Option<CaptureRegion>, PlatformError> {
        #[cfg(target_os = "linux")]
        {
            run_linux_region_selection(&request)
        }

        #[cfg(not(target_os = "linux"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "Linux region selection is only available on Linux",
            ))
        }
    }
}

#[cfg(target_os = "linux")]
fn run_linux_region_selection(
    request: &OverlayWindowRequest,
) -> Result<Option<CaptureRegion>, PlatformError> {
    let mut errors = Vec::new();

    for (program, args) in linux_region_selection_commands(request) {
        match Command::new(program).args(&args).output() {
            Ok(output) if output.status.success() => {
                let stdout = String::from_utf8_lossy(&output.stdout);
                if stdout.trim().is_empty() {
                    return Ok(None);
                }
                return parse_linux_region_selection(stdout.trim()).map(Some);
            }
            Ok(output) => errors.push(format!("{program} exited with status {}", output.status)),
            Err(error) => errors.push(format!("{program}: {error}")),
        }
    }

    Err(PlatformError::Failed(format!(
        "no Linux region selector command succeeded: {}",
        errors.join("; ")
    )))
}

#[cfg(any(target_os = "linux", test))]
fn linux_region_selection_commands(
    request: &OverlayWindowRequest,
) -> Vec<(&'static str, Vec<String>)> {
    let prompt = if request.detect_windows {
        "Select window or region"
    } else {
        "Select region"
    };
    vec![
        ("slurp", vec!["-d".into(), "-p".into(), prompt.into()]),
        ("slop", vec!["-f".into(), "%x,%y %wx%h".into()]),
    ]
}

#[cfg(any(target_os = "linux", test))]
fn parse_linux_region_selection(selection: &str) -> Result<CaptureRegion, PlatformError> {
    let (origin, size) = selection
        .trim()
        .split_once(' ')
        .ok_or_else(|| PlatformError::Failed("region selector output missing size".into()))?;
    let (x, y) = origin
        .split_once(',')
        .ok_or_else(|| PlatformError::Failed("region selector output missing origin".into()))?;
    let (width, height) = size
        .split_once('x')
        .ok_or_else(|| PlatformError::Failed("region selector output missing dimensions".into()))?;

    let region = CaptureRegion {
        x: x.trim().parse::<i32>().map_err(|source| {
            PlatformError::Failed(format!("region selector reported invalid x: {source}"))
        })?,
        y: y.trim().parse::<i32>().map_err(|source| {
            PlatformError::Failed(format!("region selector reported invalid y: {source}"))
        })?,
        width: width.trim().parse::<u32>().map_err(|source| {
            PlatformError::Failed(format!("region selector reported invalid width: {source}"))
        })?,
        height: height.trim().parse::<u32>().map_err(|source| {
            PlatformError::Failed(format!("region selector reported invalid height: {source}"))
        })?,
    };
    if region.width == 0 || region.height == 0 {
        return Err(PlatformError::Failed(
            "region selector reported an empty region".into(),
        ));
    }
    Ok(region)
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{NativeMaterial, RecordingFormat, RecordingQuality};
    #[cfg(not(target_os = "linux"))]
    use oddsnap_platform::WindowPickerService;
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, ColorPickerService, HotkeyService,
        OverlayWindowRequest, PlatformAdapter, RegionOverlayService, RegionSelectionService,
        ScreenCaptureService, VideoRecordingRequest, VideoRecordingService,
    };

    use super::LinuxPlatform;

    #[test]
    fn linux_adapter_uses_freedesktop_profile() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter.native_ui_profile().material,
            NativeMaterial::FreedesktopAdaptive
        );
    }

    #[test]
    fn linux_screen_capture_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::ScreenCapture),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    fn linux_global_hotkey_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::GlobalHotkeys),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    fn linux_window_capture_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::WindowCapture),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    fn linux_region_overlay_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::RegionOverlay),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_capture_services_report_wrong_host() {
        let adapter = LinuxPlatform;

        let error = adapter.monitors().expect_err("Linux capture wrong host");

        assert!(error.to_string().contains("only available on Linux"));

        let error = adapter
            .capture_region(oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            })
            .expect_err("Linux capture wrong host");
        assert!(error.to_string().contains("only available on Linux"));

        let error = adapter
            .active_window()
            .expect_err("Linux active window wrong host");
        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    fn linux_xrandr_parser_reads_connected_monitors() {
        let monitors = super::parse_xrandr_query_monitors(
            "Screen 0: minimum 8 x 8, current 4480 x 1440, maximum 32767 x 32767\n\
             DP-1 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 597mm x 336mm\n\
             HDMI-1 connected 1920x1080+2560+120 (normal left inverted right x axis y axis) 598mm x 336mm\n\
             DP-2 disconnected (normal left inverted right x axis y axis)\n",
        )
        .expect("parse xrandr output");

        assert_eq!(
            monitors,
            vec![
                oddsnap_platform::MonitorInfo {
                    id: "DP-1".into(),
                    name: "DP-1".into(),
                    x: 0,
                    y: 0,
                    width: 2560,
                    height: 1440,
                    scale_percent: 100,
                },
                oddsnap_platform::MonitorInfo {
                    id: "HDMI-1".into(),
                    name: "HDMI-1".into(),
                    x: 2560,
                    y: 120,
                    width: 1920,
                    height: 1080,
                    scale_percent: 100,
                },
            ]
        );
    }

    #[test]
    fn linux_xrandr_parser_accepts_negative_offsets() {
        assert_eq!(
            super::parse_xrandr_geometry("1920x1080-1920+0"),
            Some((1920, 1080, -1920, 0))
        );
        assert_eq!(
            super::parse_xrandr_geometry("3840x2160+0-2160"),
            Some((3840, 2160, 0, -2160))
        );
    }

    #[test]
    fn linux_active_window_parser_reads_xdotool_shell_geometry() {
        let window = super::parse_xdotool_window_geometry(
            "10485763",
            "OddSnap",
            "WINDOW=10485763\nX=-20\nY=40\nWIDTH=1280\nHEIGHT=720\nSCREEN=0\n",
        )
        .expect("parse xdotool geometry");

        assert_eq!(window.id, "10485763");
        assert_eq!(window.title, "OddSnap");
        assert_eq!(
            window.bounds,
            oddsnap_platform::CaptureRegion {
                x: -20,
                y: 40,
                width: 1280,
                height: 720,
            }
        );
    }

    #[test]
    fn linux_active_window_parser_rejects_empty_geometry() {
        let error = super::parse_xdotool_window_geometry(
            "10485763",
            "OddSnap",
            "WINDOW=10485763\nX=0\nY=0\nWIDTH=0\nHEIGHT=720\n",
        )
        .expect_err("empty xdotool geometry rejected");

        assert!(error.to_string().contains("empty active-window"));
    }

    #[test]
    fn linux_mouse_location_parser_reads_xdotool_shell_output() {
        assert_eq!(
            super::parse_xdotool_mouse_location("X=-12\nY=34\nSCREEN=0\nWINDOW=10485763\n")
                .expect("parse mouse location"),
            (-12, 34)
        );
    }

    #[test]
    fn linux_screenshot_commands_cover_fullscreen_and_region_backends() {
        let path = std::path::Path::new("/tmp/oddsnap-test.png");
        let full = super::linux_screenshot_commands(None, true, path);

        assert!(full.iter().any(|(program, args)| {
            program == "gnome-screenshot" && args == &vec!["-p", "-f", "/tmp/oddsnap-test.png"]
        }));
        assert!(full.iter().any(|(program, args)| {
            program == "spectacle" && args == &vec!["-b", "-n", "-o", "/tmp/oddsnap-test.png"]
        }));

        let region = oddsnap_platform::CaptureRegion {
            x: -10,
            y: 20,
            width: 30,
            height: 40,
        };
        let region_commands = super::linux_screenshot_commands(Some(&region), false, path);

        assert_eq!(
            region_commands[0],
            (
                "grim".into(),
                vec![
                    "-g".into(),
                    "-10,20 30x40".into(),
                    "/tmp/oddsnap-test.png".into()
                ]
            )
        );
        assert_eq!(
            region_commands[1],
            (
                "scrot".into(),
                vec![
                    "-a".into(),
                    "-10,20,30,40".into(),
                    "/tmp/oddsnap-test.png".into()
                ]
            )
        );
    }

    #[test]
    fn linux_capture_region_from_output_requires_decodable_image() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-linux-capture-output-test-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp test root");

        let missing = root.join("missing.png");
        let error = super::linux_capture_region_from_output(None, &missing)
            .expect_err("missing screenshot output rejected");
        assert!(error.to_string().contains("image"));

        let valid = root.join("valid.png");
        std::fs::write(
            &valid,
            [
                137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0,
                1, 8, 6, 0, 0, 0, 31, 21, 196, 137, 0, 0, 0, 13, 73, 68, 65, 84, 120, 156, 99, 224,
                18, 145, 251, 15, 0, 3, 74, 1, 66, 143, 246, 24, 176, 0, 0, 0, 0, 73, 69, 78, 68,
                174, 66, 96, 130,
            ],
        )
        .expect("write valid png");

        assert_eq!(
            super::linux_capture_region_from_output(None, &valid).expect("read image dimensions"),
            oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 1,
                height: 1
            }
        );

        let requested = oddsnap_platform::CaptureRegion {
            x: -10,
            y: 20,
            width: 30,
            height: 40,
        };
        assert_eq!(
            super::linux_capture_region_from_output(Some(&requested), &valid)
                .expect("validate region output"),
            requested
        );

        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    #[ignore = "writes a screenshot file through the local Linux screenshot command if one is installed"]
    #[cfg(target_os = "linux")]
    fn linux_full_screen_capture_writes_image_file() {
        let adapter = LinuxPlatform;
        let capture = adapter
            .capture_all_screens_with_cursor(false)
            .expect("capture screen");

        assert!(capture.image_path.exists());
        assert!(capture.region.width > 0);
        assert!(capture.region.height > 0);
        std::fs::remove_file(capture.image_path).expect("remove captured image");
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_image_clipboard_reports_wrong_host() {
        let adapter = LinuxPlatform;

        let error = adapter
            .copy_image_to_clipboard(std::path::Path::new("capture.bmp"))
            .expect_err("Linux clipboard pending");

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    fn linux_image_clipboard_commands_use_png_mime_type() {
        assert_eq!(
            super::linux_image_clipboard_commands(),
            vec![
                ("wl-copy", vec!["--type", "image/png"]),
                (
                    "xclip",
                    vec!["-selection", "clipboard", "-t", "image/png", "-i"]
                ),
            ]
        );
    }

    #[test]
    #[ignore = "writes a tiny image to the local Linux clipboard through wl-copy or xclip"]
    #[cfg(target_os = "linux")]
    fn linux_image_clipboard_can_copy_png_data() {
        let adapter = LinuxPlatform;
        let root = std::env::temp_dir().join(format!(
            "oddsnap-linux-image-clipboard-test-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp test root");
        let source = root.join("source.png");
        std::fs::write(
            &source,
            [
                137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0,
                1, 8, 6, 0, 0, 0, 31, 21, 196, 137, 0, 0, 0, 13, 73, 68, 65, 84, 120, 156, 99, 224,
                18, 145, 251, 15, 0, 3, 74, 1, 66, 143, 246, 24, 176, 0, 0, 0, 0, 73, 69, 78, 68,
                174, 66, 96, 130,
            ],
        )
        .expect("write source png");

        adapter
            .copy_image_to_clipboard(&source)
            .expect("copy image");

        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn linux_clipboard_capability_is_in_progress() {
        let adapter = LinuxPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::Clipboard),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_text_clipboard_reports_wrong_host() {
        let adapter = LinuxPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("Linux text clipboard pending");

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    #[ignore = "writes text to the local Linux clipboard through wl-copy, xclip, or xsel"]
    #[cfg(target_os = "linux")]
    fn linux_text_clipboard_can_copy_text() {
        let adapter = LinuxPlatform;

        adapter
            .copy_text_to_clipboard("OddSnap Linux clipboard smoke")
            .expect("copy text");
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_color_picker_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .sample_cursor_color()
            .expect_err("Linux color picker pending");

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    #[ignore = "samples one pixel at the local Linux cursor using xdotool and a screenshot backend"]
    #[cfg(target_os = "linux")]
    fn linux_color_picker_can_sample_cursor_color() {
        let adapter = LinuxPlatform;

        adapter
            .sample_cursor_color()
            .expect("sample cursor color through Linux backend");
    }

    #[test]
    fn linux_hotkey_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .register_capture_hotkey("Alt+`")
            .expect_err("Linux hotkey pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_recording_service_reports_wrong_host() {
        let adapter = LinuxPlatform;

        let result = adapter.start_desktop_recording(VideoRecordingRequest {
            output_path: std::path::PathBuf::from("capture.mp4"),
            region: None,
            format: RecordingFormat::Mp4,
            quality: RecordingQuality::Original,
            fps: 30,
            record_microphone: false,
            record_desktop_audio: false,
            microphone_device_id: None,
            desktop_audio_device_id: None,
        });
        let error = match result {
            Ok(_) => panic!("Linux recording should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    fn linux_recording_args_use_x11grab_region_input() {
        let args = super::linux_desktop_recording_args_for_display(
            &VideoRecordingRequest {
                output_path: std::path::PathBuf::from("/tmp/oddsnap-recording.webm"),
                region: Some(oddsnap_platform::CaptureRegion {
                    x: -12,
                    y: 34,
                    width: 800,
                    height: 600,
                }),
                format: RecordingFormat::WebM,
                quality: RecordingQuality::P720,
                fps: 144,
                record_microphone: false,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            },
            ":1",
        );

        assert!(args
            .windows(2)
            .any(|pair| pair == ["-f".to_string(), "x11grab".to_string()]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-video_size".to_string(), "800x600".to_string()]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-i".to_string(), ":1+-12,34".to_string()]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-vf".to_string(), "scale=-2:720".to_string()]));
        assert_eq!(
            args.last().map(String::as_str),
            Some("/tmp/oddsnap-recording.webm")
        );
    }

    #[test]
    fn linux_recording_args_allow_full_display_input() {
        let args = super::linux_desktop_recording_args_for_display(
            &VideoRecordingRequest {
                output_path: std::path::PathBuf::from("/tmp/oddsnap-recording.mp4"),
                region: None,
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::Original,
                fps: 30,
                record_microphone: false,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            },
            ":0",
        );

        assert!(!args.iter().any(|arg| arg == "-video_size"));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-i".to_string(), ":0".to_string()]));
    }

    #[test]
    fn linux_recording_args_ignore_audio_flags_until_audio_capture_lands() {
        let args = super::linux_desktop_recording_args_for_display(
            &VideoRecordingRequest {
                output_path: std::path::PathBuf::from("/tmp/oddsnap-recording.mp4"),
                region: None,
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::Original,
                fps: 30,
                record_microphone: true,
                record_desktop_audio: true,
                microphone_device_id: Some("mic".into()),
                desktop_audio_device_id: Some("desktop".into()),
            },
            ":0",
        );

        assert!(args.windows(2).any(|pair| pair == ["-f", "x11grab"]));
        assert!(!args.iter().any(|arg| arg.contains("pulse")));
        assert!(!args.iter().any(|arg| arg.contains("alsa")));
    }

    #[test]
    #[ignore = "starts a short local X11 FFmpeg recording if DISPLAY and ffmpeg are available"]
    #[cfg(target_os = "linux")]
    fn linux_recording_can_start_and_cancel() {
        if oddsnap_core::discover_ffmpeg_tools().is_none() {
            return;
        }
        if std::env::var("DISPLAY")
            .unwrap_or_default()
            .trim()
            .is_empty()
        {
            return;
        }

        let output_path = std::env::temp_dir().join(format!(
            "oddsnap-linux-recording-test-{}.mp4",
            std::process::id()
        ));
        let adapter = LinuxPlatform;
        let mut handle = adapter
            .start_desktop_recording(VideoRecordingRequest {
                output_path: output_path.clone(),
                region: Some(oddsnap_platform::CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 64,
                    height: 64,
                }),
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::Original,
                fps: 5,
                record_microphone: false,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            })
            .expect("start Linux recording");

        std::thread::sleep(std::time::Duration::from_millis(250));
        handle.cancel();
        let _ = std::fs::remove_file(output_path);
    }

    #[test]
    fn linux_region_overlay_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;
        let error = match adapter.create_overlay_window(OverlayWindowRequest {
            bounds: oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            },
            opacity: 1,
            click_through: true,
            show_crosshair_guides: false,
            detect_windows: false,
        }) {
            Ok(_) => panic!("Linux overlay should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    #[cfg(not(target_os = "linux"))]
    fn linux_region_selection_service_reports_wrong_host() {
        let adapter = LinuxPlatform;
        let error = match adapter.select_region(OverlayWindowRequest {
            bounds: oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            },
            opacity: 1,
            click_through: false,
            show_crosshair_guides: false,
            detect_windows: false,
        }) {
            Ok(_) => panic!("Linux region selection should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("only available on Linux"));
    }

    #[test]
    fn linux_region_selection_parser_reads_slurp_geometry() {
        assert_eq!(
            super::parse_linux_region_selection("-10,20 300x400").expect("parse selector geometry"),
            oddsnap_platform::CaptureRegion {
                x: -10,
                y: 20,
                width: 300,
                height: 400,
            }
        );
    }

    #[test]
    fn linux_region_selection_parser_rejects_empty_geometry() {
        let error = super::parse_linux_region_selection("10,20 0x400")
            .expect_err("empty selector geometry rejected");

        assert!(error.to_string().contains("empty region"));
    }

    #[test]
    fn linux_region_selection_commands_include_wayland_and_x11_tools() {
        let commands = super::linux_region_selection_commands(&OverlayWindowRequest {
            bounds: oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            },
            opacity: 1,
            click_through: false,
            show_crosshair_guides: false,
            detect_windows: true,
        });

        assert_eq!(commands[0].0, "slurp");
        assert_eq!(commands[1].0, "slop");
    }

    #[test]
    #[ignore = "opens an interactive local Linux region selector through slurp or slop"]
    #[cfg(target_os = "linux")]
    fn linux_region_selection_can_select_region() {
        let adapter = LinuxPlatform;

        let _ = adapter
            .select_region(OverlayWindowRequest {
                bounds: oddsnap_platform::CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 1,
                    height: 1,
                },
                opacity: 1,
                click_through: false,
                show_crosshair_guides: false,
                detect_windows: false,
            })
            .expect("select region");
    }
}
