use std::path::Path;
#[cfg(target_os = "linux")]
use std::{
    io::Write,
    path::PathBuf,
    process::{Command, Stdio},
    time::{SystemTime, UNIX_EPOCH},
};

use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureRequest, CaptureResult, ClipboardImageService, ClipboardTextService,
    ColorPickerService, ColorSample, HotkeyService, MonitorInfo, OverlayWindowHandle,
    OverlayWindowRequest, PlatformAdapter, PlatformError, RegionOverlayService,
    RegionSelectionService, ScreenCaptureService, VideoRecordingRequest, VideoRecordingService,
    WindowInfo, WindowPickerService,
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
        Err(PlatformError::Unsupported(
            "Linux monitor enumeration is not implemented yet",
        ))
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
fn run_linux_screenshot(
    region: Option<&CaptureRegion>,
    include_cursor: bool,
) -> Result<CaptureResult, PlatformError> {
    let output_path = linux_capture_output_path();
    let mut errors = Vec::new();

    for (program, args) in linux_screenshot_commands(region, include_cursor, &output_path) {
        match Command::new(&program).args(&args).status() {
            Ok(status) if status.success() => {
                let capture_region = match region {
                    Some(region) => region.clone(),
                    None => {
                        let (width, height) =
                            oddsnap_platform::image_file_dimensions(&output_path)?;
                        CaptureRegion {
                            x: 0,
                            y: 0,
                            width,
                            height,
                        }
                    }
                };

                return Ok(CaptureResult {
                    image_path: output_path,
                    region: capture_region,
                });
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
        Err(PlatformError::Unsupported(
            "Linux color picker is not implemented yet",
        ))
    }
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
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux desktop recording is not implemented yet",
        ))
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
        let _ = request;
        Err(PlatformError::Unsupported(
            "Linux region selection is not implemented yet",
        ))
    }
}

#[cfg(test)]
mod tests {
    use oddsnap_core::{NativeMaterial, RecordingFormat, RecordingQuality};
    use oddsnap_platform::{
        ClipboardImageService, ClipboardTextService, ColorPickerService, HotkeyService,
        OverlayWindowRequest, PlatformAdapter, RegionOverlayService, RegionSelectionService,
        ScreenCaptureService, VideoRecordingRequest, VideoRecordingService, WindowPickerService,
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
    #[cfg(not(target_os = "linux"))]
    fn linux_capture_services_report_wrong_host() {
        let adapter = LinuxPlatform;

        let error = adapter.monitors().expect_err("Linux capture pending");

        assert!(error.to_string().contains("not implemented yet"));

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
    fn linux_color_picker_service_is_explicitly_unimplemented() {
        let adapter = LinuxPlatform;

        let error = adapter
            .sample_cursor_color()
            .expect_err("Linux color picker pending");

        assert!(error.to_string().contains("not implemented yet"));
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
    fn linux_recording_service_is_explicitly_unimplemented() {
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

        assert!(error.to_string().contains("not implemented yet"));
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
    fn linux_region_selection_service_is_explicitly_unimplemented() {
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

        assert!(error.to_string().contains("not implemented yet"));
    }
}
