use oddsnap_core::{CapabilityState, NativeUiProfile, PlatformCapabilities, PlatformCapability};
use oddsnap_platform::{
    CaptureRegion, CaptureResult, MonitorInfo, PlatformAdapter, PlatformError, ScreenCaptureService,
};

#[cfg(target_os = "windows")]
use std::{
    fs, mem,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

#[cfg(target_os = "windows")]
use windows::Win32::Graphics::Gdi::{
    BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, DeleteDC, DeleteObject, GetDC, GetDIBits,
    ReleaseDC, SelectObject, BITMAPINFO, BITMAPINFOHEADER, BI_RGB, CAPTUREBLT, DIB_RGB_COLORS,
    HBITMAP, HDC, ROP_CODE, SRCCOPY,
};
#[cfg(target_os = "windows")]
use windows::Win32::UI::WindowsAndMessaging::{
    GetSystemMetrics, SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN,
};

#[derive(Debug, Default)]
pub struct WindowsPlatform;

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
                (PlatformCapability::WindowCapture, CapabilityState::Planned),
                (
                    PlatformCapability::ScreenshotExclusion,
                    CapabilityState::Planned,
                ),
                (PlatformCapability::GlobalHotkeys, CapabilityState::Planned),
                (PlatformCapability::Tray, CapabilityState::Planned),
                (PlatformCapability::Clipboard, CapabilityState::Planned),
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
                scale_percent: 100,
            }])
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
            capture_region_to_bmp(region)
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = region;
            Err(PlatformError::Unsupported(
                "Windows region capture is only available on Windows",
            ))
        }
    }
}

#[cfg(target_os = "windows")]
fn capture_region_to_bmp(region: CaptureRegion) -> Result<CaptureResult, PlatformError> {
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

    let bgra = unsafe { read_screen_bgra(region.x, region.y, width, height)? };
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
) -> Result<Vec<u8>, PlatformError> {
    let screen_dc = unsafe { GetDC(None) };
    if screen_dc.is_invalid() {
        return Err(PlatformError::Failed("GetDC failed for the desktop".into()));
    }

    let result = unsafe { read_screen_bgra_with_dc(screen_dc, x, y, width, height) };
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

    let result =
        unsafe { read_screen_bgra_with_bitmap(screen_dc, memory_dc, bitmap, x, y, width, height) };

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
    x: i32,
    y: i32,
    width: i32,
    height: i32,
) -> Result<Vec<u8>, PlatformError> {
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
            x,
            y,
            ROP_CODE(SRCCOPY.0 | CAPTUREBLT.0),
        )
    };

    if let Err(error) = blt_result {
        unsafe {
            SelectObject(memory_dc, old_object);
        }
        return Err(PlatformError::Failed(format!("BitBlt failed: {error}")));
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
    use oddsnap_core::{CapabilityState, PlatformCapability};
    use oddsnap_platform::PlatformAdapter;

    use super::WindowsPlatform;

    #[test]
    fn windows_adapter_tracks_early_capture_work() {
        let adapter = WindowsPlatform;
        let capabilities = adapter.capabilities();

        assert_eq!(
            capabilities.state(PlatformCapability::ScreenCapture),
            CapabilityState::InProgress
        );
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn windows_monitor_enumeration_returns_virtual_screen() {
        use oddsnap_platform::ScreenCaptureService;

        let adapter = WindowsPlatform;
        let monitors = adapter.monitors().expect("enumerate monitors");

        assert_eq!(monitors.len(), 1);
        assert!(monitors[0].width > 0);
        assert!(monitors[0].height > 0);
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
