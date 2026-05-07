use std::path::Path;
#[cfg(target_os = "macos")]
use std::{
    fs,
    io::{Read, Write},
    path::PathBuf,
    process::{Child, Command, Stdio},
    sync::mpsc::{self, Sender},
    thread::{self, JoinHandle},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

#[cfg(target_os = "macos")]
use oddsnap_core::{build_recording_output_args, FfmpegRecordingRequest};
use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService,
    ColorPickerService, ColorSample, HotkeyService, MonitorInfo, OcrTextRequest, OcrTextResult,
    OcrTextService, OverlayWindowHandle, OverlayWindowRequest, PermissionsService, PlatformAdapter,
    PlatformError, RegionOverlayService, RegionSelectionMode, RegionSelectionService,
    ScreenCaptureService, VideoRecordingRequest, VideoRecordingService, WindowInfo,
    WindowPickerService,
};

#[derive(Debug, Default)]
pub struct MacosPlatform;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MacosTrayEvent {
    Capture,
    Ocr,
    ColorPicker,
    ToggleRecording,
    ScrollCapture,
    Settings,
    History,
    Quit,
}

#[cfg(target_os = "macos")]
pub struct MacosTrayIcon {
    _tray_icon: tray_icon::TrayIcon,
    record_item: tray_icon::menu::MenuItem,
    stop_sender: Sender<()>,
    join_handle: Option<JoinHandle<()>>,
}

#[cfg(target_os = "macos")]
impl MacosTrayIcon {
    pub fn set_recording_state(&self, is_recording: bool) -> Result<(), PlatformError> {
        self.record_item.set_text(if is_recording {
            "Stop recording"
        } else {
            "Record"
        });
        Ok(())
    }
}

#[cfg(target_os = "macos")]
impl Drop for MacosTrayIcon {
    fn drop(&mut self) {
        let _ = self.stop_sender.send(());
        if let Some(join_handle) = self.join_handle.take() {
            let _ = join_handle.join();
        }
    }
}

impl MacosPlatform {
    pub fn capture_interactive_selection(
        &self,
        include_cursor: bool,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_interactive_screencapture(include_cursor)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = include_cursor;
            Err(PlatformError::Unsupported(
                "macOS interactive capture is only available on macOS",
            ))
        }
    }
}

impl PlatformAdapter for MacosPlatform {
    fn name(&self) -> &'static str {
        "macOS"
    }

    fn native_ui_profile(&self) -> NativeUiProfile {
        NativeUiProfile::for_target("macos")
    }

    fn capabilities(&self) -> PlatformCapabilities {
        PlatformCapabilities {
            os: "macos".into(),
            items: vec![
                (
                    PlatformCapability::ScreenCapture,
                    CapabilityState::InProgress,
                ),
                (PlatformCapability::RegionOverlay, CapabilityState::Planned),
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
                (PlatformCapability::Tray, CapabilityState::InProgress),
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

impl ScreenCaptureService for MacosPlatform {
    fn monitors(&self) -> Result<Vec<MonitorInfo>, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_monitor_enumeration()
        }

        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::Unsupported(
                "macOS monitor enumeration is only available on macOS",
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
        #[cfg(target_os = "macos")]
        {
            run_macos_screencapture(Some(&request.region), request.include_cursor)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "macOS region capture is only available on macOS",
            ))
        }
    }

    fn capture_all_screens_with_cursor(
        &self,
        include_cursor: bool,
    ) -> Result<CaptureResult, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_screencapture(None, include_cursor)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = include_cursor;
            Err(PlatformError::Unsupported(
                "macOS full-screen capture is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn run_macos_monitor_enumeration() -> Result<Vec<MonitorInfo>, PlatformError> {
    let output = Command::new("osascript")
        .args(macos_monitor_jxa_args())
        .output()
        .map_err(|source| PlatformError::Failed(format!("failed to start osascript: {source}")))?;

    if !output.status.success() {
        return Err(PlatformError::Failed(format!(
            "osascript monitor enumeration exited with status {}",
            output.status
        )));
    }

    let monitors = parse_macos_monitor_jxa_output(&String::from_utf8_lossy(&output.stdout))?;
    if monitors.is_empty() {
        Err(PlatformError::Failed(
            "macOS monitor enumeration returned no screens".into(),
        ))
    } else {
        Ok(monitors)
    }
}

#[cfg(any(target_os = "macos", test))]
fn macos_monitor_jxa_args() -> [&'static str; 4] {
    [
        "-l",
        "JavaScript",
        "-e",
        "ObjC.import('AppKit'); const screens = $.NSScreen.screens; const lines = []; for (let i = 0; i < screens.count; i++) { const screen = screens.objectAtIndex(i); const frame = screen.frame; const name = screen.localizedName ? ObjC.unwrap(screen.localizedName).replace(/\\t/g, ' ') : `Display ${i + 1}`; lines.push([i + 1, name, Math.round(frame.origin.x), Math.round(frame.origin.y), Math.round(frame.size.width), Math.round(frame.size.height), Math.round(screen.backingScaleFactor * 100)].join('\\t')); } lines.join('\\n');",
    ]
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_monitor_jxa_output(output: &str) -> Result<Vec<MonitorInfo>, PlatformError> {
    let mut monitors = Vec::new();
    for line in output.lines().filter(|line| !line.trim().is_empty()) {
        let parts = line.split('\t').collect::<Vec<_>>();
        if parts.len() != 7 {
            return Err(PlatformError::Failed(
                "osascript monitor output must contain seven tab-separated fields".into(),
            ));
        }
        let width = parse_macos_monitor_u32(parts[4], "width")?;
        let height = parse_macos_monitor_u32(parts[5], "height")?;
        if width == 0 || height == 0 {
            return Err(PlatformError::Failed(
                "osascript monitor output reported an empty display".into(),
            ));
        }

        monitors.push(MonitorInfo {
            id: parts[0].trim().into(),
            name: parts[1].trim().into(),
            x: parse_macos_monitor_i32(parts[2], "x")?,
            y: parse_macos_monitor_i32(parts[3], "y")?,
            width,
            height,
            scale_percent: parse_macos_monitor_u32(parts[6], "scale")?,
        });
    }
    Ok(monitors)
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_monitor_i32(value: &str, label: &str) -> Result<i32, PlatformError> {
    value.trim().parse::<i32>().map_err(|source| {
        PlatformError::Failed(format!(
            "osascript monitor output reported invalid {label}: {source}"
        ))
    })
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_monitor_u32(value: &str, label: &str) -> Result<u32, PlatformError> {
    value.trim().parse::<u32>().map_err(|source| {
        PlatformError::Failed(format!(
            "osascript monitor output reported invalid {label}: {source}"
        ))
    })
}

#[cfg(target_os = "macos")]
fn run_macos_screencapture(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    let output_path = macos_capture_output_path();
    let args = macos_screencapture_args(region, include_cursor, &output_path);
    let status = Command::new("screencapture")
        .args(&args)
        .status()
        .map_err(|source| {
            PlatformError::Failed(format!("failed to start macOS screencapture: {source}"))
        })?;
    if !status.success() {
        return Err(PlatformError::Failed(format!(
            "macOS screencapture exited with status {status}. {}",
            macos_screen_recording_permission_hint()
        )));
    }

    let capture_region = match region {
        Some(region) => region.clone(),
        None => {
            let (width, height) = oddsnap_platform::image_file_dimensions(&output_path)?;
            CaptureRegion {
                x: 0,
                y: 0,
                width,
                height,
            }
        }
    };

    Ok(CaptureResult {
        image_path: output_path,
        region: capture_region,
    })
}

#[cfg(target_os = "macos")]
fn run_macos_interactive_screencapture(
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    let output_path = macos_capture_output_path();
    let args = macos_interactive_screencapture_args(include_cursor, &output_path);
    let status = Command::new("screencapture")
        .args(&args)
        .status()
        .map_err(|source| {
            PlatformError::Failed(format!("failed to start macOS screencapture: {source}"))
        })?;
    if !status.success() {
        return Err(PlatformError::Failed(format!(
            "macOS interactive capture exited with status {status}. {}",
            macos_screen_recording_permission_hint()
        )));
    }
    if !output_path.exists() {
        return Err(PlatformError::Failed(
            "macOS interactive capture canceled".into(),
        ));
    }

    let (width, height) = oddsnap_platform::image_file_dimensions(&output_path)?;
    Ok(CaptureResult {
        image_path: output_path,
        region: CaptureRegion {
            x: 0,
            y: 0,
            width,
            height,
        },
    })
}

#[cfg(any(target_os = "macos", test))]
fn macos_screencapture_args(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
    output_path: &Path,
) -> Vec<String> {
    let mut args = vec!["-x".to_string()];
    if include_cursor {
        args.push("-C".into());
    }
    if let Some(region) = region {
        args.push("-R".into());
        args.push(format!(
            "{},{},{},{}",
            region.x, region.y, region.width, region.height
        ));
    }
    args.push(output_path.display().to_string());
    args
}

#[cfg(any(target_os = "macos", test))]
fn macos_interactive_screencapture_args(include_cursor: bool, output_path: &Path) -> Vec<String> {
    let mut args = vec!["-x".to_string(), "-i".to_string()];
    if include_cursor {
        args.push("-C".into());
    }
    args.push(output_path.display().to_string());
    args
}

#[cfg(target_os = "macos")]
fn macos_capture_output_path() -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-macos-capture-{}-{:09}.png",
        duration.as_secs(),
        duration.subsec_nanos()
    ))
}

fn macos_screen_recording_permission_name() -> &'static str {
    "Screen & System Audio Recording"
}

fn macos_accessibility_permission_name() -> &'static str {
    "Accessibility"
}

#[cfg(any(target_os = "macos", test))]
fn macos_screen_recording_permission_hint() -> &'static str {
    "Enable screen capture access in System Settings > Privacy & Security > Screen & System Audio Recording for OddSnap or the app that launched it."
}

#[cfg(any(target_os = "macos", test))]
fn macos_accessibility_permission_hint() -> &'static str {
    "Enable Accessibility access in System Settings > Privacy & Security > Accessibility for OddSnap or the app that launched it."
}

#[cfg(target_os = "macos")]
fn macos_screen_recording_permission_probe() -> bool {
    let output_path = macos_capture_output_path();
    let region = CaptureRegion {
        x: 0,
        y: 0,
        width: 1,
        height: 1,
    };
    let args = macos_screencapture_args(Some(&region), false, &output_path);
    let status = Command::new("screencapture").args(&args).status();
    let granted = matches!(status, Ok(status) if status.success())
        && output_path.exists()
        && oddsnap_platform::image_file_dimensions(&output_path).is_ok();
    let _ = fs::remove_file(output_path);
    granted
}

#[cfg(target_os = "macos")]
fn macos_accessibility_permission_probe() -> bool {
    Command::new("osascript")
        .args([
            "-e",
            "tell application \"System Events\" to count of application processes",
        ])
        .status()
        .is_ok_and(|status| status.success())
}

impl WindowPickerService for MacosPlatform {
    fn active_window(&self) -> Result<WindowInfo, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_active_window_detection()
        }

        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::Unsupported(
                "macOS active-window detection is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn run_macos_active_window_detection() -> Result<WindowInfo, PlatformError> {
    let output = Command::new("osascript")
        .args(macos_active_window_osascript_args())
        .output()
        .map_err(|source| PlatformError::Failed(format!("failed to start osascript: {source}")))?;

    if !output.status.success() {
        return Err(PlatformError::Failed(format!(
            "osascript exited with status {}. {}",
            output.status,
            macos_accessibility_permission_hint()
        )));
    }

    parse_macos_active_window_output(&String::from_utf8_lossy(&output.stdout))
}

#[cfg(any(target_os = "macos", test))]
fn macos_active_window_osascript_args() -> [&'static str; 6] {
    [
        "-e",
        "tell application \"System Events\"",
        "-e",
        "set frontApp to first application process whose frontmost is true\nset frontWindow to first window of frontApp\nset windowBounds to bounds of frontWindow\nreturn (name of frontApp) & linefeed & (name of frontWindow) & linefeed & ((item 1 of windowBounds) as text) & \",\" & ((item 2 of windowBounds) as text) & \",\" & ((item 3 of windowBounds) as text) & \",\" & ((item 4 of windowBounds) as text)",
        "-e",
        "end tell",
    ]
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_active_window_output(output: &str) -> Result<WindowInfo, PlatformError> {
    let mut lines = output.lines();
    let app_name = lines
        .next()
        .ok_or_else(|| PlatformError::Failed("osascript output missing app name".into()))?
        .trim();
    let window_title = lines
        .next()
        .ok_or_else(|| PlatformError::Failed("osascript output missing window title".into()))?
        .trim();
    let bounds = lines
        .next()
        .ok_or_else(|| PlatformError::Failed("osascript output missing window bounds".into()))?;
    let [left, top, right, bottom] = parse_macos_window_bounds(bounds)?;
    let width = u32::try_from(right - left)
        .map_err(|_| PlatformError::Failed("osascript reported invalid window width".into()))?;
    let height = u32::try_from(bottom - top)
        .map_err(|_| PlatformError::Failed("osascript reported invalid window height".into()))?;
    if width == 0 || height == 0 {
        return Err(PlatformError::Failed(
            "osascript reported an empty active-window rectangle".into(),
        ));
    }

    Ok(WindowInfo {
        id: app_name.into(),
        title: window_title.into(),
        bounds: CaptureRegion {
            x: left,
            y: top,
            width,
            height,
        },
    })
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_window_bounds(bounds: &str) -> Result<[i32; 4], PlatformError> {
    let values = bounds
        .split(',')
        .map(|value| {
            value.trim().parse::<i32>().map_err(|source| {
                PlatformError::Failed(format!(
                    "osascript reported invalid window bounds: {source}"
                ))
            })
        })
        .collect::<Result<Vec<_>, _>>()?;

    values
        .try_into()
        .map_err(|_| PlatformError::Failed("osascript output must contain four bounds".into()))
}

impl ClipboardImageService for MacosPlatform {
    fn copy_image_to_clipboard(&self, image_path: &Path) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            copy_image_to_macos_clipboard(image_path)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = image_path;
            Err(PlatformError::Unsupported(
                "macOS image clipboard is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn copy_image_to_macos_clipboard(image_path: &Path) -> Result<(), PlatformError> {
    let tiff_path = macos_clipboard_tiff_path();
    let sips_args = macos_sips_tiff_args(image_path, &tiff_path);
    let sips_status = Command::new("sips")
        .args(&sips_args)
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .map_err(|source| PlatformError::Failed(format!("failed to start sips: {source}")))?;
    if !sips_status.success() {
        return Err(PlatformError::Failed(format!(
            "sips image conversion exited with status {sips_status}"
        )));
    }

    let script = macos_set_clipboard_to_tiff_script(&tiff_path);
    let osascript_status = Command::new("osascript")
        .args(["-e", script.as_str()])
        .status()
        .map_err(|source| PlatformError::Failed(format!("failed to start osascript: {source}")))?;
    let _ = fs::remove_file(&tiff_path);

    if osascript_status.success() {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "osascript image clipboard exited with status {osascript_status}"
        )))
    }
}

#[cfg(target_os = "macos")]
fn macos_clipboard_tiff_path() -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-macos-clipboard-{}-{:09}.tiff",
        duration.as_secs(),
        duration.subsec_nanos()
    ))
}

#[cfg(any(target_os = "macos", test))]
fn macos_sips_tiff_args(source: &Path, destination: &Path) -> Vec<String> {
    vec![
        "-s".into(),
        "format".into(),
        "tiff".into(),
        source.display().to_string(),
        "--out".into(),
        destination.display().to_string(),
    ]
}

#[cfg(any(target_os = "macos", test))]
fn macos_set_clipboard_to_tiff_script(path: &Path) -> String {
    format!(
        "set the clipboard to (read (POSIX file {}) as TIFF picture)",
        applescript_string_literal(&path.display().to_string())
    )
}

#[cfg(any(target_os = "macos", test))]
fn applescript_string_literal(value: &str) -> String {
    let escaped = value.replace('\\', "\\\\").replace('"', "\\\"");
    format!("\"{escaped}\"")
}

impl ClipboardTextService for MacosPlatform {
    fn copy_text_to_clipboard(&self, text: &str) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            copy_text_to_macos_clipboard(text)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = text;
            Err(PlatformError::Unsupported(
                "macOS text clipboard is only available on macOS",
            ))
        }
    }
}

impl OcrTextService for MacosPlatform {
    fn recognize_text(&self, request: OcrTextRequest) -> Result<OcrTextResult, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            match recognize_text_with_macos_vision(&request) {
                Ok(result) => Ok(result),
                Err(vision_error) => oddsnap_platform::recognize_text_with_tesseract(&request)
                    .map_err(|tesseract_error| {
                        PlatformError::Failed(format!(
                            "macOS Vision OCR failed: {vision_error}; tesseract fallback failed: {tesseract_error}"
                        ))
                    }),
            }
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "macOS OCR is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn recognize_text_with_macos_vision(
    request: &OcrTextRequest,
) -> Result<OcrTextResult, PlatformError> {
    let args = macos_vision_ocr_command_args(
        &request.image_path.display().to_string(),
        &request.language_tag,
    );
    let output = Command::new("swift")
        .args(args)
        .output()
        .map_err(|source| {
            PlatformError::Failed(format!("failed to start macOS Vision OCR: {source}"))
        })?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let error = stderr.trim();
        return Err(PlatformError::Failed(if error.is_empty() {
            format!("macOS Vision OCR exited with status {}", output.status)
        } else {
            error.into()
        }));
    }

    Ok(OcrTextResult {
        text: String::from_utf8_lossy(&output.stdout)
            .trim_end()
            .to_string(),
        engine_id: "macos-vision-v1".into(),
    })
}

#[cfg(any(target_os = "macos", test))]
fn macos_vision_ocr_command_args(image_path: &str, language_tag: &str) -> Vec<String> {
    vec![
        "-e".into(),
        MACOS_VISION_OCR_SCRIPT.into(),
        "--".into(),
        image_path.into(),
        language_tag.into(),
    ]
}

#[cfg(any(target_os = "macos", test))]
const MACOS_VISION_OCR_SCRIPT: &str = r#"
import AppKit
import Foundation
import Vision

let args = CommandLine.arguments
guard args.count >= 3 else {
    fputs("Vision OCR needs image path and language arguments.\n", stderr)
    exit(2)
}
let imagePath = args[args.count - 2]
let languageTag = args[args.count - 1].trimmingCharacters(in: .whitespacesAndNewlines)
let imageUrl = URL(fileURLWithPath: imagePath)

guard let image = NSImage(contentsOf: imageUrl) else {
    fputs("Could not load image for Vision OCR.\n", stderr)
    exit(2)
}

var proposedRect = CGRect(origin: .zero, size: image.size)
guard let cgImage = image.cgImage(forProposedRect: &proposedRect, context: nil, hints: nil) else {
    fputs("Could not decode image for Vision OCR.\n", stderr)
    exit(2)
}

let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.usesLanguageCorrection = true
if !languageTag.isEmpty && languageTag.lowercased() != "auto" {
    request.recognitionLanguages = [languageTag]
}

let handler = VNImageRequestHandler(cgImage: cgImage, options: [:])
do {
    try handler.perform([request])
} catch {
    fputs("Vision OCR failed: \(error.localizedDescription)\n", stderr)
    exit(1)
}

let text = (request.results ?? [])
    .compactMap { $0.topCandidates(1).first?.string }
    .joined(separator: "\n")
print(text)
"#;

#[cfg(target_os = "macos")]
fn copy_text_to_macos_clipboard(text: &str) -> Result<(), PlatformError> {
    let mut child = Command::new("pbcopy")
        .stdin(Stdio::piped())
        .spawn()
        .map_err(|source| PlatformError::Failed(format!("failed to start pbcopy: {source}")))?;

    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| PlatformError::Failed("failed to open pbcopy stdin".into()))?;
    stdin.write_all(text.as_bytes()).map_err(|source| {
        PlatformError::Failed(format!("failed to write clipboard text: {source}"))
    })?;
    drop(stdin);

    let status = child
        .wait()
        .map_err(|source| PlatformError::Failed(format!("failed to wait for pbcopy: {source}")))?;
    if status.success() {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "pbcopy exited with status {status}"
        )))
    }
}

impl ColorPickerService for MacosPlatform {
    fn sample_cursor_color(&self) -> Result<ColorSample, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            run_macos_color_picker()
        }

        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::Unsupported(
                "macOS color picker is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
fn run_macos_color_picker() -> Result<ColorSample, PlatformError> {
    let cursor = Command::new("osascript")
        .args(macos_cursor_location_jxa_args())
        .output()
        .map_err(|source| PlatformError::Failed(format!("failed to start osascript: {source}")))?;
    if !cursor.status.success() {
        return Err(PlatformError::Failed(format!(
            "osascript cursor location exited with status {}",
            cursor.status
        )));
    }

    let (x, y) = parse_macos_cursor_capture_location(&String::from_utf8_lossy(&cursor.stdout))?;
    let capture = run_macos_screencapture(
        Some(&CaptureRegion {
            x,
            y,
            width: 1,
            height: 1,
        }),
        false,
    )?;
    let sample = oddsnap_platform::image_file_top_left_color_sample(&capture.image_path);
    let _ = fs::remove_file(capture.image_path);
    sample
}

#[cfg(any(target_os = "macos", test))]
fn macos_cursor_location_jxa_args() -> [&'static str; 4] {
    [
        "-l",
        "JavaScript",
        "-e",
        "ObjC.import('AppKit'); const p = $.NSEvent.mouseLocation; const screens = $.NSScreen.screens; let selected = screens.objectAtIndex(0); for (let i = 0; i < screens.count; i++) { const screen = screens.objectAtIndex(i); const f = screen.frame; const minX = f.origin.x; const maxX = f.origin.x + f.size.width; const minY = f.origin.y; const maxY = f.origin.y + f.size.height; if (p.x >= minX && p.x < maxX && p.y >= minY && p.y < maxY) { selected = screen; break; } } const f = selected.frame; const x = Math.round(p.x); const y = Math.round((f.origin.y + f.size.height) - p.y); `${x},${y}`;",
    ]
}

#[cfg(any(target_os = "macos", test))]
fn parse_macos_cursor_capture_location(output: &str) -> Result<(i32, i32), PlatformError> {
    let (x, y) = output
        .trim()
        .split_once(',')
        .ok_or_else(|| PlatformError::Failed("osascript cursor output missing comma".into()))?;

    Ok((
        x.trim().parse::<i32>().map_err(|source| {
            PlatformError::Failed(format!(
                "osascript cursor output reported invalid x: {source}"
            ))
        })?,
        y.trim().parse::<i32>().map_err(|source| {
            PlatformError::Failed(format!(
                "osascript cursor output reported invalid y: {source}"
            ))
        })?,
    ))
}

impl HotkeyService for MacosPlatform {
    fn register_capture_hotkey(&self, accelerator: &str) -> Result<(), PlatformError> {
        let _ = accelerator;
        Err(PlatformError::Unsupported(
            "macOS global hotkey registration is not implemented yet",
        ))
    }
}

#[cfg(target_os = "macos")]
pub fn start_oddsnap_tray_icon(
    events: Sender<MacosTrayEvent>,
) -> Result<MacosTrayIcon, PlatformError> {
    use tray_icon::{
        menu::{Menu, MenuEvent, MenuItem, PredefinedMenuItem},
        TrayIconBuilder,
    };

    let screenshot = MenuItem::with_id("screenshot", "Screenshot", true, None);
    let ocr = MenuItem::with_id("ocr", "Text capture", true, None);
    let color_picker = MenuItem::with_id("color-picker", "Color picker", true, None);
    let record = MenuItem::with_id("record", "Record", true, None);
    let scroll_capture = MenuItem::with_id("scroll-capture", "Scroll capture", true, None);
    let settings = MenuItem::with_id("settings", "Settings", true, None);
    let history = MenuItem::with_id("history", "History", true, None);
    let quit = MenuItem::with_id("quit", "Quit", true, None);
    let separator_one = PredefinedMenuItem::separator();
    let separator_two = PredefinedMenuItem::separator();

    let menu = Menu::new();
    menu.append_items(&[
        &screenshot,
        &ocr,
        &color_picker,
        &record,
        &scroll_capture,
        &separator_one,
        &settings,
        &history,
        &separator_two,
        &quit,
    ])
    .map_err(|error| PlatformError::Failed(format!("failed to build macOS tray menu: {error}")))?;

    let tray_icon = TrayIconBuilder::new()
        .with_title("OddSnap")
        .with_tooltip("OddSnap")
        .with_menu(Box::new(menu))
        .with_menu_on_left_click(true)
        .with_menu_on_right_click(true)
        .build()
        .map_err(|error| {
            PlatformError::Failed(format!("failed to create macOS tray icon: {error}"))
        })?;

    let (stop_sender, stop_receiver) = mpsc::channel();
    let menu_receiver = MenuEvent::receiver().clone();
    let join_handle = thread::spawn(move || loop {
        if stop_receiver.try_recv().is_ok() {
            break;
        }

        if let Ok(event) = menu_receiver.recv_timeout(Duration::from_millis(50)) {
            if let Some(mapped) = macos_tray_event_for_menu_id(event.id().as_ref()) {
                let _ = events.send(mapped);
            }
        }
    });

    Ok(MacosTrayIcon {
        _tray_icon: tray_icon,
        record_item: record,
        stop_sender,
        join_handle: Some(join_handle),
    })
}

#[cfg(any(target_os = "macos", test))]
fn macos_tray_event_for_menu_id(id: &str) -> Option<MacosTrayEvent> {
    match id {
        "screenshot" => Some(MacosTrayEvent::Capture),
        "ocr" => Some(MacosTrayEvent::Ocr),
        "color-picker" => Some(MacosTrayEvent::ColorPicker),
        "record" => Some(MacosTrayEvent::ToggleRecording),
        "scroll-capture" => Some(MacosTrayEvent::ScrollCapture),
        "settings" => Some(MacosTrayEvent::Settings),
        "history" => Some(MacosTrayEvent::History),
        "quit" => Some(MacosTrayEvent::Quit),
        _ => None,
    }
}

impl PermissionsService for MacosPlatform {
    fn missing_permissions(&self) -> Vec<String> {
        #[cfg(target_os = "macos")]
        {
            let mut missing = Vec::new();
            if !macos_screen_recording_permission_probe() {
                missing.push(macos_screen_recording_permission_name().into());
            }
            if !macos_accessibility_permission_probe() {
                missing.push(macos_accessibility_permission_name().into());
            }
            missing
        }

        #[cfg(not(target_os = "macos"))]
        {
            vec![
                macos_screen_recording_permission_name().into(),
                macos_accessibility_permission_name().into(),
            ]
        }
    }
}

impl VideoRecordingService for MacosPlatform {
    fn start_desktop_recording(
        &self,
        request: VideoRecordingRequest,
    ) -> Result<Box<dyn oddsnap_platform::VideoRecordingHandle>, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            start_macos_desktop_recording(request)
                .map(|handle| Box::new(handle) as Box<dyn oddsnap_platform::VideoRecordingHandle>)
        }

        #[cfg(not(target_os = "macos"))]
        {
            let _ = request;
            Err(PlatformError::Unsupported(
                "macOS desktop recording is only available on macOS",
            ))
        }
    }
}

#[cfg(target_os = "macos")]
#[derive(Debug)]
struct MacosVideoRecordingHandle {
    child: Option<Child>,
    temp_output_path: PathBuf,
    output_path: PathBuf,
    ffmpeg_path: PathBuf,
    format: oddsnap_core::RecordingFormat,
    quality: oddsnap_core::RecordingQuality,
    fps: u32,
    stderr_thread: Option<thread::JoinHandle<String>>,
}

#[cfg(target_os = "macos")]
impl MacosVideoRecordingHandle {
    fn cancel_process_and_outputs(&mut self) {
        if let Some(mut child) = self.child.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
        if let Some(thread) = self.stderr_thread.take() {
            let _ = thread.join();
        }
        let _ = fs::remove_file(&self.temp_output_path);
        let _ = fs::remove_file(&self.output_path);
    }
}

#[cfg(target_os = "macos")]
impl oddsnap_platform::VideoRecordingHandle for MacosVideoRecordingHandle {
    fn output_path(&self) -> &Path {
        &self.output_path
    }

    fn stop(&mut self) -> Result<oddsnap_platform::VideoRecordingResult, PlatformError> {
        let Some(mut child) = self.child.take() else {
            return Err(PlatformError::Failed(
                "recording process is not running".into(),
            ));
        };

        if let Err(error) = request_macos_recording_stop(&mut child) {
            if let Some(thread) = self.stderr_thread.take() {
                let _ = thread.join();
            }
            return Err(error);
        }
        let status = wait_for_macos_child_exit(&mut child, Duration::from_secs(30))?;
        let stderr = self
            .stderr_thread
            .take()
            .and_then(|thread| thread.join().ok())
            .unwrap_or_default();

        if !status.success() {
            return Err(macos_recording_stop_error(status.code(), &stderr));
        }

        if !is_non_empty_file(&self.temp_output_path) {
            return Err(PlatformError::Failed(format!(
                "macOS screen recording output is empty: {}",
                self.temp_output_path.display()
            )));
        }

        transcode_macos_recording(
            &self.ffmpeg_path,
            &self.temp_output_path,
            &self.output_path,
            self.format,
            self.quality,
            self.fps,
        )?;
        let _ = fs::remove_file(&self.temp_output_path);

        Ok(oddsnap_platform::VideoRecordingResult {
            output_path: self.output_path.clone(),
        })
    }

    fn cancel(&mut self) {
        self.cancel_process_and_outputs();
    }
}

#[cfg(target_os = "macos")]
impl Drop for MacosVideoRecordingHandle {
    fn drop(&mut self) {
        if self.child.is_some() {
            self.cancel_process_and_outputs();
        }
    }
}

#[cfg(target_os = "macos")]
fn start_macos_desktop_recording(
    request: VideoRecordingRequest,
) -> Result<MacosVideoRecordingHandle, PlatformError> {
    if request.record_desktop_audio {
        return Err(PlatformError::Unsupported(
            "macOS system-audio recording is not implemented yet",
        ));
    }

    if let Some(parent) = request.output_path.parent() {
        fs::create_dir_all(parent).map_err(|source| {
            PlatformError::Failed(format!("failed to create recording directory: {source}"))
        })?;
    }

    let ffmpeg_path = oddsnap_core::discover_ffmpeg_tools()
        .ok_or_else(|| PlatformError::Failed("FFmpeg not found on PATH".into()))?
        .ffmpeg;
    let temp_output_path = macos_recording_output_path();
    let args = macos_screencapture_recording_args(&request, &temp_output_path);
    let mut child = Command::new("screencapture")
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|source| {
            PlatformError::Failed(format!(
                "failed to start macOS screencapture recording: {source}"
            ))
        })?;

    let stderr = child.stderr.take();
    let stderr_thread = stderr.map(|mut stderr| {
        thread::spawn(move || {
            let mut buffer = String::new();
            let _ = stderr.read_to_string(&mut buffer);
            buffer
        })
    });

    Ok(MacosVideoRecordingHandle {
        child: Some(child),
        temp_output_path,
        output_path: request.output_path,
        ffmpeg_path,
        format: request.format,
        quality: request.quality,
        fps: request.fps,
        stderr_thread,
    })
}

#[cfg(target_os = "macos")]
fn macos_recording_output_path() -> PathBuf {
    let duration = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    std::env::temp_dir().join(format!(
        "oddsnap-macos-recording-{}-{:09}.mov",
        duration.as_secs(),
        duration.subsec_nanos()
    ))
}

#[cfg(any(target_os = "macos", test))]
fn macos_screencapture_recording_args(
    request: &VideoRecordingRequest,
    output_path: &Path,
) -> Vec<String> {
    let fps = request.fps.clamp(1, 240).to_string();
    let mut args = vec!["-x".to_string(), "-v".to_string(), "-F".to_string(), fps];
    if let Some(region) = &request.region {
        args.extend([
            "-R".into(),
            format!(
                "{},{},{},{}",
                region.x, region.y, region.width, region.height
            ),
        ]);
    }
    if request.record_microphone {
        args.push("-g".into());
    }
    args.push(output_path.display().to_string());
    args
}

#[cfg(target_os = "macos")]
fn request_macos_recording_stop(child: &mut Child) -> Result<(), PlatformError> {
    let status = Command::new("kill")
        .args(["-INT", &child.id().to_string()])
        .status()
        .map_err(|source| {
            PlatformError::Failed(format!("failed to request macOS recording stop: {source}"))
        })?;
    if status.success() {
        Ok(())
    } else {
        let _ = child.kill();
        Err(PlatformError::Failed(format!(
            "failed to request macOS recording stop: kill exited with status {status}"
        )))
    }
}

#[cfg(target_os = "macos")]
fn wait_for_macos_child_exit(
    child: &mut Child,
    timeout: Duration,
) -> Result<std::process::ExitStatus, PlatformError> {
    let deadline = Instant::now() + timeout;
    loop {
        if let Some(status) = child.try_wait().map_err(|source| {
            PlatformError::Failed(format!("macOS recording wait failed: {source}"))
        })? {
            return Ok(status);
        }

        if Instant::now() >= deadline {
            let _ = child.kill();
            let _ = child.wait();
            return Err(PlatformError::Failed(
                "macOS screen recording did not stop within 30 seconds".into(),
            ));
        }

        thread::sleep(Duration::from_millis(100));
    }
}

#[cfg(any(target_os = "macos", test))]
fn macos_recording_stop_error(code: Option<i32>, stderr: &str) -> PlatformError {
    PlatformError::Failed(format!(
        "macOS screen recording failed with exit code {:?}: {}",
        code,
        stderr.trim()
    ))
}

#[cfg(target_os = "macos")]
fn transcode_macos_recording(
    ffmpeg_path: &Path,
    input_path: &Path,
    output_path: &Path,
    format: oddsnap_core::RecordingFormat,
    quality: oddsnap_core::RecordingQuality,
    fps: u32,
) -> Result<(), PlatformError> {
    let args = build_recording_output_args(&FfmpegRecordingRequest {
        input_args: vec![
            "-hide_banner".into(),
            "-i".into(),
            input_path.display().to_string(),
        ],
        output_path: output_path.to_path_buf(),
        format,
        quality,
        fps,
    });
    let output = Command::new(ffmpeg_path)
        .args(args)
        .output()
        .map_err(|source| PlatformError::Failed(format!("failed to start FFmpeg: {source}")))?;

    if !output.status.success() {
        return Err(PlatformError::Failed(format!(
            "FFmpeg macOS recording conversion failed with exit code {:?}: {}",
            output.status.code(),
            String::from_utf8_lossy(&output.stderr).trim()
        )));
    }

    if is_non_empty_file(output_path) {
        Ok(())
    } else {
        Err(PlatformError::Failed(format!(
            "recording output is empty: {}",
            output_path.display()
        )))
    }
}

#[cfg(target_os = "macos")]
fn is_non_empty_file(path: &Path) -> bool {
    fs::metadata(path)
        .map(|metadata| metadata.len() > 0)
        .unwrap_or(false)
}

impl RegionOverlayService for MacosPlatform {
    fn create_overlay_window(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Box<dyn OverlayWindowHandle>, PlatformError> {
        let _ = request;
        Err(PlatformError::Unsupported(
            "macOS region overlay is not implemented yet",
        ))
    }
}

impl RegionSelectionService for MacosPlatform {
    fn select_region(
        &self,
        request: OverlayWindowRequest,
    ) -> Result<Option<CaptureRegion>, PlatformError> {
        if request.selection_mode != RegionSelectionMode::Rectangle {
            return Err(PlatformError::Unsupported(
                "macOS center selection needs the production overlay",
            ));
        }
        let _ = request;
        Err(PlatformError::Unsupported(
            "macOS region selection is not implemented yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{NativeMaterial, RecordingFormat, RecordingQuality};
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, ColorPickerService, HotkeyService,
        OverlayWindowRequest, PlatformAdapter, RegionOverlayService, RegionSelectionMode,
        RegionSelectionService, ScreenCaptureService, VideoRecordingRequest, VideoRecordingService,
    };
    #[cfg(not(target_os = "macos"))]
    use oddsnap_platform::{PermissionsService, WindowPickerService};

    use super::MacosPlatform;

    #[test]
    fn macos_adapter_uses_liquid_glass_profile() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter.native_ui_profile().material,
            NativeMaterial::LiquidGlass
        );
    }

    #[test]
    fn macos_screen_capture_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::ScreenCapture),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    fn macos_global_hotkey_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::GlobalHotkeys),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    fn macos_window_capture_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::WindowCapture),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_capture_services_report_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter.monitors().expect_err("macOS capture pending");

        assert!(error.to_string().contains("only available on macOS"));

        let error = adapter
            .capture_region(oddsnap_platform::CaptureRegion {
                x: 0,
                y: 0,
                width: 10,
                height: 10,
            })
            .expect_err("macOS capture wrong host");
        assert!(error.to_string().contains("only available on macOS"));

        let error = adapter
            .active_window()
            .expect_err("macOS active window wrong host");
        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_monitor_jxa_uses_appkit_screens() {
        let args = super::macos_monitor_jxa_args();

        assert_eq!(args[0], "-l");
        assert_eq!(args[1], "JavaScript");
        assert!(args[3].contains("NSScreen.screens"));
        assert!(args[3].contains("backingScaleFactor"));
    }

    #[test]
    fn macos_monitor_parser_reads_multiple_screens() {
        let monitors = super::parse_macos_monitor_jxa_output(
            "1\tBuilt-in Display\t0\t0\t1512\t982\t200\n\
             2\tStudio Display\t-1920\t-120\t1920\t1080\t100\n",
        )
        .expect("parse monitor output");

        assert_eq!(
            monitors,
            vec![
                oddsnap_platform::MonitorInfo {
                    id: "1".into(),
                    name: "Built-in Display".into(),
                    x: 0,
                    y: 0,
                    width: 1512,
                    height: 982,
                    scale_percent: 200,
                },
                oddsnap_platform::MonitorInfo {
                    id: "2".into(),
                    name: "Studio Display".into(),
                    x: -1920,
                    y: -120,
                    width: 1920,
                    height: 1080,
                    scale_percent: 100,
                },
            ]
        );
    }

    #[test]
    fn macos_monitor_parser_rejects_empty_display() {
        let error = super::parse_macos_monitor_jxa_output("1\tDisplay\t0\t0\t0\t982\t200\n")
            .expect_err("empty monitor rejected");

        assert!(error.to_string().contains("empty display"));
    }

    #[test]
    fn macos_active_window_parser_reads_osascript_output() {
        let window =
            super::parse_macos_active_window_output("OddSnap\nSettings\n100,200,900,700\n")
                .expect("parse osascript output");

        assert_eq!(window.id, "OddSnap");
        assert_eq!(window.title, "Settings");
        assert_eq!(
            window.bounds,
            oddsnap_platform::CaptureRegion {
                x: 100,
                y: 200,
                width: 800,
                height: 500,
            }
        );
    }

    #[test]
    fn macos_active_window_parser_rejects_empty_bounds() {
        let error = super::parse_macos_active_window_output("OddSnap\nSettings\n100,200,100,700\n")
            .expect_err("empty window bounds rejected");

        assert!(error.to_string().contains("empty active-window"));
    }

    #[test]
    fn macos_active_window_osascript_uses_system_events() {
        let args = super::macos_active_window_osascript_args();

        assert!(args.iter().any(|arg| arg.contains("System Events")));
        assert!(args.iter().any(|arg| arg.contains("bounds of frontWindow")));
    }

    #[test]
    fn macos_screencapture_args_include_region_and_cursor_options() {
        let path = std::path::Path::new("/tmp/oddsnap-test.png");

        let args = super::macos_screencapture_args(
            Some(&oddsnap_platform::CaptureRegion {
                x: -10,
                y: 20,
                width: 30,
                height: 40,
            }),
            true,
            path,
        );

        assert_eq!(
            args,
            vec!["-x", "-C", "-R", "-10,20,30,40", "/tmp/oddsnap-test.png"]
        );
    }

    #[test]
    fn macos_interactive_screencapture_args_use_native_selection_mode() {
        let path = std::path::Path::new("/tmp/oddsnap-selection.png");

        let args = super::macos_interactive_screencapture_args(true, path);

        assert_eq!(args, vec!["-x", "-i", "-C", "/tmp/oddsnap-selection.png"]);
        assert!(!args.iter().any(|arg| arg == "-R"));
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_interactive_capture_reports_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter
            .capture_interactive_selection(false)
            .expect_err("macOS interactive capture wrong host");

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_permission_guidance_names_current_system_setting() {
        assert_eq!(
            super::macos_screen_recording_permission_name(),
            "Screen & System Audio Recording"
        );
        assert_eq!(
            super::macos_accessibility_permission_name(),
            "Accessibility"
        );
        assert!(super::macos_screen_recording_permission_hint()
            .contains("System Settings > Privacy & Security"));
        assert!(super::macos_accessibility_permission_hint()
            .contains("System Settings > Privacy & Security > Accessibility"));
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_permission_service_reports_required_permissions_on_wrong_host() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter.missing_permissions(),
            vec![
                "Screen & System Audio Recording".to_string(),
                "Accessibility".to_string()
            ]
        );
    }

    #[test]
    #[ignore = "writes a screenshot file through the local macOS screencapture command"]
    #[cfg(target_os = "macos")]
    fn macos_full_screen_capture_writes_png_file() {
        let adapter = MacosPlatform;
        let capture = adapter
            .capture_all_screens_with_cursor(false)
            .expect("capture screen");

        assert!(capture.image_path.exists());
        assert!(capture.region.width > 0);
        assert!(capture.region.height > 0);
        std::fs::remove_file(capture.image_path).expect("remove captured png");
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_image_clipboard_reports_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter
            .copy_image_to_clipboard(std::path::Path::new("capture.bmp"))
            .expect_err("macOS clipboard wrong host");

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_image_clipboard_uses_sips_tiff_conversion() {
        let args = super::macos_sips_tiff_args(
            std::path::Path::new("/tmp/source.png"),
            std::path::Path::new("/tmp/out.tiff"),
        );

        assert_eq!(
            args,
            vec![
                "-s",
                "format",
                "tiff",
                "/tmp/source.png",
                "--out",
                "/tmp/out.tiff"
            ]
        );
    }

    #[test]
    fn macos_image_clipboard_script_reads_tiff_picture() {
        let script =
            super::macos_set_clipboard_to_tiff_script(std::path::Path::new("/tmp/odd\"snap.tiff"));

        assert_eq!(
            script,
            "set the clipboard to (read (POSIX file \"/tmp/odd\\\"snap.tiff\") as TIFF picture)"
        );
    }

    #[test]
    fn macos_vision_ocr_command_uses_swift_script_and_arguments() {
        let args = super::macos_vision_ocr_command_args("/tmp/capture.png", "en-US");

        assert_eq!(args[0], "-e");
        assert!(args[1].contains("VNRecognizeTextRequest"));
        assert_eq!(args[2], "--");
        assert_eq!(args[3], "/tmp/capture.png");
        assert_eq!(args[4], "en-US");
    }

    #[test]
    #[ignore = "writes a tiny image to the local macOS clipboard through sips and osascript"]
    #[cfg(target_os = "macos")]
    fn macos_image_clipboard_can_copy_png_data() {
        let adapter = MacosPlatform;
        let root = std::env::temp_dir().join(format!(
            "oddsnap-macos-image-clipboard-test-{}",
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
    fn macos_clipboard_capability_is_in_progress() {
        let adapter = MacosPlatform;

        assert_eq!(
            adapter
                .capabilities()
                .state(oddsnap_core::PlatformCapability::Clipboard),
            oddsnap_core::CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_text_clipboard_reports_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter
            .copy_text_to_clipboard("capture text")
            .expect_err("macOS text clipboard pending");

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    #[ignore = "writes text to the local macOS clipboard"]
    #[cfg(target_os = "macos")]
    fn macos_text_clipboard_can_copy_text() {
        let adapter = MacosPlatform;

        adapter
            .copy_text_to_clipboard("OddSnap macOS clipboard smoke")
            .expect("copy text");
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_color_picker_reports_wrong_host() {
        let adapter = MacosPlatform;

        let error = adapter
            .sample_cursor_color()
            .expect_err("macOS color picker wrong host");

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_cursor_location_jxa_uses_appkit_mouse_location() {
        let args = super::macos_cursor_location_jxa_args();

        assert_eq!(args[0], "-l");
        assert_eq!(args[1], "JavaScript");
        assert!(args[3].contains("NSEvent.mouseLocation"));
        assert!(args[3].contains("NSScreen.screens"));
    }

    #[test]
    fn macos_cursor_location_parser_reads_capture_coordinates() {
        assert_eq!(
            super::parse_macos_cursor_capture_location("-10,42\n").expect("parse cursor location"),
            (-10, 42)
        );
    }

    #[test]
    fn macos_cursor_location_parser_rejects_invalid_output() {
        let error = super::parse_macos_cursor_capture_location("not-a-point")
            .expect_err("invalid cursor location rejected");

        assert!(error.to_string().contains("missing comma"));
    }

    #[test]
    #[ignore = "samples one pixel at the local macOS cursor using AppKit and screencapture"]
    #[cfg(target_os = "macos")]
    fn macos_color_picker_can_sample_cursor_color() {
        let adapter = MacosPlatform;

        adapter
            .sample_cursor_color()
            .expect("sample cursor color through macOS backend");
    }

    #[test]
    fn macos_hotkey_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;

        let error = adapter
            .register_capture_hotkey("Alt+`")
            .expect_err("macOS hotkey pending");

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_tray_menu_ids_map_to_expected_events() {
        assert_eq!(
            super::macos_tray_event_for_menu_id("screenshot"),
            Some(super::MacosTrayEvent::Capture)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("ocr"),
            Some(super::MacosTrayEvent::Ocr)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("color-picker"),
            Some(super::MacosTrayEvent::ColorPicker)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("record"),
            Some(super::MacosTrayEvent::ToggleRecording)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("scroll-capture"),
            Some(super::MacosTrayEvent::ScrollCapture)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("settings"),
            Some(super::MacosTrayEvent::Settings)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("history"),
            Some(super::MacosTrayEvent::History)
        );
        assert_eq!(
            super::macos_tray_event_for_menu_id("quit"),
            Some(super::MacosTrayEvent::Quit)
        );
        assert_eq!(super::macos_tray_event_for_menu_id("missing"), None);
    }

    #[test]
    #[cfg(not(target_os = "macos"))]
    fn macos_recording_service_reports_wrong_host() {
        let adapter = MacosPlatform;

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
            Ok(_) => panic!("macOS recording should be unavailable off macOS"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("only available on macOS"));
    }

    #[test]
    fn macos_recording_args_use_native_video_capture() {
        let args = super::macos_screencapture_recording_args(
            &VideoRecordingRequest {
                output_path: std::path::PathBuf::from("capture.mp4"),
                region: Some(oddsnap_platform::CaptureRegion {
                    x: -20,
                    y: 30,
                    width: 640,
                    height: 480,
                }),
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::P720,
                fps: 144,
                record_microphone: true,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            },
            std::path::Path::new("/tmp/oddsnap-recording.mov"),
        );

        assert_eq!(
            args,
            vec![
                "-x",
                "-v",
                "-F",
                "144",
                "-R",
                "-20,30,640,480",
                "-g",
                "/tmp/oddsnap-recording.mov"
            ]
        );
    }

    #[test]
    fn macos_recording_stop_error_reports_failed_exit() {
        let error = super::macos_recording_stop_error(Some(1), " permission denied \n");

        assert!(error
            .to_string()
            .contains("macOS screen recording failed with exit code Some(1)"));
        assert!(error.to_string().contains("permission denied"));
    }

    #[test]
    #[ignore = "records the local macOS desktop through screencapture and FFmpeg"]
    #[cfg(target_os = "macos")]
    fn macos_desktop_recording_can_start_and_stop_if_ffmpeg_exists() {
        if oddsnap_core::discover_ffmpeg_tools().is_none() {
            return;
        }

        let adapter = MacosPlatform;
        let root = std::env::temp_dir().join(format!(
            "oddsnap-macos-recording-test-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("create temp recording root");
        let output_path = root.join("capture.mp4");
        let mut handle = adapter
            .start_desktop_recording(VideoRecordingRequest {
                output_path: output_path.clone(),
                region: Some(oddsnap_platform::CaptureRegion {
                    x: 0,
                    y: 0,
                    width: 320,
                    height: 240,
                }),
                format: RecordingFormat::Mp4,
                quality: RecordingQuality::Original,
                fps: 30,
                record_microphone: false,
                record_desktop_audio: false,
                microphone_device_id: None,
                desktop_audio_device_id: None,
            })
            .expect("start macOS recording");
        std::thread::sleep(std::time::Duration::from_secs(2));
        let result = handle.stop().expect("stop macOS recording");

        assert_eq!(result.output_path, output_path);
        assert!(
            std::fs::metadata(&result.output_path)
                .expect("recording metadata")
                .len()
                > 0
        );
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn macos_region_overlay_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;
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
            selection_mode: RegionSelectionMode::Rectangle,
        }) {
            Ok(_) => panic!("macOS overlay should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }

    #[test]
    fn macos_region_selection_service_is_explicitly_unimplemented() {
        let adapter = MacosPlatform;
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
            selection_mode: RegionSelectionMode::Rectangle,
        }) {
            Ok(_) => panic!("macOS region selection should be pending"),
            Err(error) => error,
        };

        assert!(error.to_string().contains("not implemented yet"));
    }
}
