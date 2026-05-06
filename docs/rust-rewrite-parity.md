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

## Current Non-Parity State

The rewrite does not yet implement real screenshot capture, annotation, recording, OCR, translation, upload, history, local runtimes, packaging, or update behavior.
Those remain tracked in `docs/rust-rewrite-todo.md` and GitHub issue #40.
