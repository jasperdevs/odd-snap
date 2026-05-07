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
- [x] Windows imported color-picker hotkey routing.
- [x] Windows topmost transparent overlay window foundation.
- [x] Windows screenshot exclusion service foundation.
- [x] Windows monitor enumeration.
- [x] Windows system DPI mapping for monitor scale.
- [ ] Windows per-monitor DPI verification.
- [x] macOS command-backed screen recording permission detection and failure guidance.
- [ ] macOS tray/menu bar presence.
- [x] macOS app-level global hotkey listener foundation.
- [x] macOS explicit unsupported capture/clipboard adapter surfaces.
- [x] macOS command-backed still screenshot capture foundation.
- [x] macOS active-window bounds discovery foundation through `osascript`/System Events.
- [x] macOS native interactive rectangle/window selection foundation through `screencapture -i`.
- [ ] Linux portal-aware screen capture plan.
- [ ] Linux tray/appindicator strategy.
- [x] Linux app-level X11 global hotkey listener foundation.
- [ ] Linux Wayland global hotkey support.
- [x] Linux explicit unsupported capture/clipboard adapter surfaces.
- [x] Linux X11 monitor enumeration foundation through `xrandr`.
- [x] Linux command-backed still screenshot capture foundation.
- [x] Linux command-backed interactive region selection foundation through `slurp`/`slop`.
- [x] Linux X11 active-window discovery foundation through `xdotool`.
- [x] Linux X11 color picker foundation through cursor coordinate screenshot sampling.

## Milestone 3 - Capture Core

- [x] Windows full-screen capture path.
- [x] Windows GDI region capture foundation.
- [x] Windows still-capture cursor inclusion preference.
- [ ] Cross-platform region capture.
- [x] Windows active-window capture foundation.
- [x] Cross-platform active-window capture foundation.
- [ ] Production region selection overlay.
- [x] Windows primitive drag region selection capture path.
- [x] Windows primitive region-selection frame and crosshair visual feedback.
- [x] Windows primitive region-selection window detection/snap foundation.
- [ ] Production crosshair guides across overlay tools.
- [ ] Magnifier.
- [ ] Annotation drawing.
- [x] Shared save-to-file persistence helper.
- [x] User-configurable save destination.
- [x] Legacy file-name template persistence for saved captures.
- [x] Monthly save folder persistence for saved captures.
- [x] Capture image format persistence for PNG/JPEG/BMP.
- [x] Windows image clipboard foundation.
- [x] Windows image clipboard supports saved PNG/JPEG/BMP captures.
- [x] Linux command-backed image clipboard foundation.
- [x] macOS command-backed image clipboard foundation.
- [x] Windows text clipboard foundation.
- [x] macOS/Linux command-backed text clipboard foundation.
- [x] Windows cursor color sampling and clipboard copy foundation.
- [x] Rust color history persistence and recent-color copy actions.
- [x] Cross-platform image clipboard foundation.
- [x] Cross-platform text clipboard.
- [x] GPUI recent-captures list for capture smoke results.
- [x] Rust settings store for capture output and clipboard preferences.
- [x] GPUI persisted controls for image format, clipboard copy, and cursor inclusion.
- [x] GPUI persisted controls for default capture mode, delay, crosshair, magnifier, and window detection preferences.
- [x] Rust JSON media history store for saved image captures.
- [x] Post-capture image preview.
- [x] GPUI recent-captures list can reveal saved files in the system file browser.

## Milestone 4 - Recording And Media

- [ ] GIF recording.
- [x] Windows FFmpeg-backed desktop video recording start/stop foundation.
- [x] Windows FFmpeg recording can target an explicit capture region.
- [x] Linux X11 FFmpeg-backed desktop video recording start/cancel/stop foundation.
- [x] Linux X11 FFmpeg recording can target an explicit capture region.
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
- [ ] Production color picker overlay polish.
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
- [x] Import existing color history.
- [ ] Import existing media metadata where practical.
- [ ] Windows full parity verification.
- [ ] macOS full parity verification.
- [ ] Linux full parity verification.
- [x] CI-only Rust rewrite matrix runs fmt/check/test/clippy/build on Windows, macOS, and Ubuntu.
- [x] Rust packaging readiness notes document current non-release boundary and host dependency caveats.
- [ ] Release decision document.
