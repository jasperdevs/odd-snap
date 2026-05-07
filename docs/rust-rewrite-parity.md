# Rust Rewrite Parity Notes

The Rust rewrite must not replace the current app until this document and the live TODO agree that parity is verified.

## Current Verified Foundation

- Rust workspace exists in the rewrite worktree.
- GPUI app shell builds and launches on Windows.
- Platform adapters exist for Windows, macOS, and Linux.
- Native UI intent is represented in code:
  - Windows: WinUI 3 aligned.
  - macOS: Liquid Glass aligned.
  - Linux: freedesktop adaptive.
- Legacy settings JSON can be located and parsed without requiring the full old C# schema.
- Windows monitor enumeration returns the virtual-screen bounds through the platform service.
- Windows can capture a screen region through the Rust platform service and write a BMP file.
- GPUI shell can invoke the Windows capture service through a local smoke action.
- Shared capture trait can capture the full virtual screen by reusing monitor enumeration and region capture.
- Windows can detect active-window bounds and capture that window through the shared capture service.
- Windows can copy generated BMP captures to the system clipboard as image data.
- Shared capture persistence can save generated captures into a stable output directory.
- GPUI shell exposes full-screen and active-window capture smoke controls with a local recent-captures list.
- Rust app settings can load capture output and clipboard preferences from a JSON settings file.
- macOS and Linux adapters expose explicit pending capture/window/clipboard service implementations instead of missing trait surfaces.
- Windows monitor enumeration now reports real display entries with DPI-derived scale percentages.
- Rust captures can append durable JSON history entries and reload recent captures on startup.
- GPUI recent-capture rows can reveal saved files through the host file browser.
- Rust startup can import legacy save directory, history, and copy-after-capture settings when no Rust settings file exists.
- Rust startup can import legacy capture image format and JPEG quality settings.
- Rust startup can import legacy file-name template and monthly-folder save settings.
- Rust startup can import existing image/media history from the current SQLite history database or legacy JSON indexes when no Rust history file exists.
- Windows can copy UTF-16 text payloads to the system clipboard through the shared clipboard trait.
- Windows can parse, register, listen for, and unregister process-local global hotkeys.
- Windows has a Rust screenshot-exclusion service boundary backed by `SetWindowDisplayAffinity`.
- Rust startup can import legacy capture and recording hotkey settings; the capture listener uses the imported capture hotkey.
- Windows hotkey listener can dispatch both capture and recording events into the GPUI shell.
- Capture hotkey routing uses the supported imported default capture mode, including active-window capture.
- Windows hotkey listener can dispatch imported full-screen and active-window capture hotkeys into the GPUI shell.
- Windows can install a shell tray icon with the legacy menu commands, dispatch tray capture/recording/settings/history/quit events into GPUI, and update the tray recording state.
- Rust startup can import legacy capture UX preferences including delay, cursor, magnifier, crosshair, UI scale, toast position, default capture mode, startup, and update toggles.
- GPUI capture smoke honors the imported capture delay and surfaces imported capture UX preferences.
- GPUI can persistently cycle implemented capture preferences for image format, clipboard copy, and cursor inclusion.
- Rust settings preserve advanced legacy preferences for OCR, translation, uploads, image search, tool visibility, custom tool hotkeys, toast timing, screenshot styling, setup state, and open-with apps.
- Rust core can discover `ffmpeg` and optional `ffprobe` from PATH, and the GPUI shell reports media-tool availability.
- Rust FFmpeg discovery also checks bundled executable and legacy AppData locations before PATH.
- Rust startup can import legacy recording format, quality, FPS, and audio-device preferences.
- Rust core can build format-specific FFmpeg output arguments for GIF, MP4, WebM, and MKV recording targets.
- Windows has a Rust platform service boundary for starting/stopping FFmpeg-backed desktop video recordings from the GPUI shell.
- Windows FFmpeg recording requests can carry capture-region bounds into `gdigrab` offset/video-size arguments.
- GPUI can start active-window recording through the explicit-region FFmpeg path.
- Rust recording history can store FFmpeg-generated JPG thumbnails for saved GIF/video entries.
- GPUI can persistently cycle implemented recording preferences for format and quality.
- GPUI history rows show media kind and legacy upload metadata, and can copy stored upload links.
- Rust capture persistence can save Windows BMP captures as PNG, JPEG, or BMP according to Rust settings.
- Rust capture persistence uses the configured file-name template and optional `yyyy-MM` monthly folder.
- GPUI shows the newest saved image capture as an inline preview when the file still exists.
- Windows still capture can honor the imported cursor-inclusion preference for full-screen and active-window captures.

## Current Non-Parity State

The rewrite does not yet implement the full production capture overlay, annotation, region recording/audio parity, OCR, translation, upload, full history actions, local runtimes, packaging, or update behavior.
The Windows tray foundation is present, but the Rust menu still routes text capture, color picker, and scroll capture to pending-status messages until those feature backends land.
Those remain tracked in `docs/rust-rewrite-todo.md` and GitHub issue #40.
