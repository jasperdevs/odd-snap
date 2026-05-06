# OddSnap Rust Rewrite Live TODO

This checklist is the persistent local tracker for the Rust rewrite branch.
Keep it updated whenever a rewrite milestone lands.

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
- [x] Wire GPUI shell smoke button to Windows capture service.

## Milestone 2 - Platform Risk Proofs

- [ ] Windows tray icon.
- [x] Windows global hotkey registration foundation.
- [x] Windows global hotkey event loop integration.
- [ ] Windows topmost transparent overlay.
- [ ] Windows screenshot exclusion.
- [x] Windows monitor enumeration.
- [x] Windows system DPI mapping for monitor scale.
- [ ] Windows per-monitor DPI verification.
- [ ] macOS screen recording permission detection.
- [ ] macOS tray/menu bar presence.
- [ ] macOS global hotkey.
- [x] macOS explicit unsupported capture/clipboard adapter surfaces.
- [ ] Linux portal-aware screen capture plan.
- [ ] Linux tray/appindicator strategy.
- [ ] Linux global hotkey strategy.
- [x] Linux explicit unsupported capture/clipboard adapter surfaces.

## Milestone 3 - Capture Core

- [x] Windows full-screen capture path.
- [x] Windows GDI region capture foundation.
- [ ] Cross-platform region capture.
- [x] Windows active-window capture foundation.
- [ ] Cross-platform window capture.
- [ ] Region selection overlay.
- [ ] Crosshair guides.
- [ ] Magnifier.
- [ ] Annotation drawing.
- [x] Shared save-to-file persistence helper.
- [x] User-configurable save destination.
- [x] Legacy file-name template persistence for saved captures.
- [x] Monthly save folder persistence for saved captures.
- [x] Capture image format persistence for PNG/JPEG/BMP.
- [x] Windows image clipboard foundation.
- [x] Windows text clipboard foundation.
- [ ] Cross-platform image clipboard.
- [ ] Cross-platform text clipboard.
- [x] GPUI recent-captures list for capture smoke results.
- [x] Rust settings store for capture output and clipboard preferences.
- [x] Rust JSON media history store for saved image captures.
- [x] Post-capture image preview.

## Milestone 4 - Recording And Media

- [ ] GIF recording.
- [ ] MP4/WebM recording.
- [x] FFmpeg discovery.
- [ ] Microphone recording.
- [ ] System audio recording where supported.
- [ ] Video thumbnail generation.
- [x] Basic persisted media history index.
- [ ] Full media history UI and actions.

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

- [x] Import existing save directory/history/copy/image-format/save-naming settings into Rust settings.
- [ ] Import all existing settings.
- [x] Import existing history.
- [ ] Import existing media metadata where practical.
- [ ] Windows full parity verification.
- [ ] macOS full parity verification.
- [ ] Linux full parity verification.
- [ ] Release decision document.
