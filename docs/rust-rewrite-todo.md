# OddSnap Rust Rewrite Live TODO

This checklist is the persistent GitHub-facing tracker for the Rust rewrite branch.
Keep it updated whenever a rewrite milestone lands.

GitHub tracker: https://github.com/jasperdevs/odd-snap/issues/40

## Ground Rules

- [x] Create isolated sibling worktree: `C:\Users\bunny\Downloads\odd-snap-rust`.
- [x] Keep the current WPF/WinForms OddSnap app untouched while the Rust rewrite is incomplete.
- [x] Use GPUI-first for the app shell.
- [x] Track native UI goals for Windows, macOS, and Linux from the start.
- [ ] Do not replace the shipping app until all parity items below are verified on Windows, macOS, and Linux.

## Milestone 1 - Rust Workspace And GPUI Shell

- [x] Add Rust workspace.
- [x] Add shared core crate.
- [x] Add platform trait crate.
- [x] Add Windows platform adapter stub.
- [x] Add macOS platform adapter stub.
- [x] Add Linux platform adapter stub.
- [x] Add legacy settings migration reader.
- [x] Add GPUI app shell.
- [x] Verify `cargo check --workspace`.
- [x] Verify `cargo test --workspace`.
- [x] Launch the GPUI shell on Windows.

## Milestone 2 - Platform Risk Proofs

- [ ] Windows tray icon.
- [ ] Windows global hotkey.
- [ ] Windows topmost transparent overlay.
- [ ] Windows screenshot exclusion.
- [ ] Windows monitor enumeration and DPI mapping.
- [ ] macOS screen recording permission detection.
- [ ] macOS tray/menu bar presence.
- [ ] macOS global hotkey.
- [ ] Linux portal-aware screen capture plan.
- [ ] Linux tray/appindicator strategy.
- [ ] Linux global hotkey strategy.

## Milestone 3 - Capture Core

- [ ] Full-screen capture.
- [ ] Region capture.
- [ ] Window capture.
- [ ] Region selection overlay.
- [ ] Crosshair guides.
- [ ] Magnifier.
- [ ] Annotation drawing.
- [ ] Save to file.
- [ ] Copy to clipboard.
- [ ] Post-capture preview.

## Milestone 4 - Recording And Media

- [ ] GIF recording.
- [ ] MP4/WebM recording.
- [ ] FFmpeg discovery.
- [ ] Microphone recording.
- [ ] System audio recording where supported.
- [ ] Video thumbnail generation.
- [ ] Media history.

## Milestone 5 - Advanced OddSnap Features

- [ ] OCR.
- [ ] Translation.
- [ ] Image search.
- [ ] Upload destinations.
- [ ] Stickers/background removal.
- [ ] Upscale.
- [ ] Local runtime management.
- [ ] Update/install flow.

## Milestone 6 - Migration And Parity Closure

- [ ] Import existing settings.
- [ ] Import existing history.
- [ ] Import existing media metadata where practical.
- [ ] Windows full parity verification.
- [ ] macOS full parity verification.
- [ ] Linux full parity verification.
- [ ] Release decision document.
