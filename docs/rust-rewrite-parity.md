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
- Rust startup can import legacy save directory, history, and copy-after-capture settings when no Rust settings file exists.
- Rust startup can import legacy capture image format and JPEG quality settings.
- Rust startup can import legacy file-name template and monthly-folder save settings.
- Rust startup can import existing image/media history from the current SQLite history database or legacy JSON indexes when no Rust history file exists.
- Windows can copy UTF-16 text payloads to the system clipboard through the shared clipboard trait.
- Windows can parse, register, listen for, and unregister process-local global hotkeys.
- Rust core can discover `ffmpeg` and optional `ffprobe` from PATH, and the GPUI shell reports media-tool availability.
- Rust startup can import legacy recording format, quality, FPS, and audio-device preferences.
- Rust core can build format-specific FFmpeg output arguments for GIF, MP4, WebM, and MKV recording targets.
- Windows has a Rust platform service boundary for starting/stopping FFmpeg-backed desktop video recordings from the GPUI shell.
- Rust capture persistence can save Windows BMP captures as PNG, JPEG, or BMP according to Rust settings.
- Rust capture persistence uses the configured file-name template and optional `yyyy-MM` monthly folder.
- GPUI shows the newest saved image capture as an inline preview when the file still exists.

## Current Non-Parity State

The rewrite does not yet implement real screenshot capture, annotation, recording, OCR, translation, upload, history, local runtimes, packaging, or update behavior.
Those remain tracked in `docs/rust-rewrite-todo.md` and GitHub issue #40.
