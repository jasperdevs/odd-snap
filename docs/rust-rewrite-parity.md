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

## Current Non-Parity State

The rewrite does not yet implement real screenshot capture, annotation, recording, OCR, translation, upload, history, local runtimes, packaging, or update behavior.
Those remain tracked in `docs/rust-rewrite-todo.md` and GitHub issue #40.
