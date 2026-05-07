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

- [x] Windows tray icon and menu foundation.
- [x] Windows global hotkey registration foundation.
- [x] Windows global hotkey event loop integration.
- [x] Legacy capture/recording hotkey settings import.
- [x] Windows recording hotkey event loop integration.
- [x] Capture hotkey honors supported imported default capture modes.
- [x] Windows dedicated full-screen and active-window hotkey routing.
- [ ] Windows topmost transparent overlay.
- [x] Windows screenshot exclusion service foundation.
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
- [x] Windows still-capture cursor inclusion preference.
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
- [x] GPUI persisted controls for image format, clipboard copy, and cursor inclusion.
- [x] Rust JSON media history store for saved image captures.
- [x] Post-capture image preview.
- [x] GPUI recent-captures list can reveal saved files in the system file browser.

## Milestone 4 - Recording And Media

- [ ] GIF recording.
- [x] Windows FFmpeg-backed desktop video recording start/stop foundation.
- [x] Windows FFmpeg recording can target an explicit capture region.
- [ ] Region GIF recording.
- [x] GPUI active-window recording uses the explicit-region FFmpeg path.
- [ ] Freeform region MP4/WebM recording.
- [x] FFmpeg discovery.
- [x] Bundled/AppData FFmpeg discovery parity before PATH fallback.
- [x] Rust recording settings import and FFmpeg output argument model.
- [x] GPUI persisted controls for recording format and quality.
- [ ] Microphone recording.
- [ ] System audio recording where supported.
- [x] Video thumbnail generation for Rust recording history.
- [x] Basic persisted media history index.
- [x] GPUI history rows show media kind and legacy upload metadata, with upload-link copy action.
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

- [x] Import existing save directory/history/copy/image-format/save-naming/recording settings into Rust settings.
- [x] Import legacy capture UX preferences: delay, cursor, magnifier, crosshair, UI scale, toast position, default mode, startup/update toggles.
- [x] Preserve legacy advanced settings for OCR, translation, uploads, image search, tools, toast timing, styling, and custom hotkeys.
- [ ] Import all existing settings.
- [x] Import existing history.
- [ ] Import existing media metadata where practical.
- [ ] Windows full parity verification.
- [ ] macOS full parity verification.
- [ ] Linux full parity verification.
- [ ] Release decision document.
