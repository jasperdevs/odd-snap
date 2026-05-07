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
- [x] Centralize initial GPUI skin tokens and reusable panel/button chrome for easier feature-panel additions.

## Milestone 2 - Platform Risk Proofs

- [x] Windows tray icon and menu foundation.
- [x] Windows global hotkey registration foundation.
- [x] Windows global hotkey event loop integration.
- [x] Legacy capture/recording hotkey settings import.
- [x] Windows recording hotkey event loop integration.
- [x] Capture hotkey honors supported imported default capture modes.
- [x] Unsupported imported default capture modes report pending parity instead of silently falling back to full-screen.
- [x] Windows dedicated full-screen and active-window hotkey routing.
- [x] Windows imported color-picker hotkey routing.
- [x] Imported OCR hotkey routing reports pending OCR parity instead of being dropped.
- [x] Imported scan/sticker/upscale/center/ruler/scroll/AI hotkey routing reports pending parity instead of being dropped.
- [x] Pending advanced tool metadata is centralized for default capture routing, hotkey summaries, duplicate checks, and cross-platform hotkey registration.
- [x] Duplicate imported hotkeys are rejected before platform registration.
- [x] Windows topmost transparent overlay window foundation.
- [x] Windows screenshot exclusion service foundation.
- [x] Windows monitor enumeration.
- [x] Windows monitor enumeration fails explicitly instead of silently falling back to synthetic virtual-screen data.
- [x] Windows system DPI mapping for monitor scale.
- [ ] Windows per-monitor DPI verification.
- [x] macOS command-backed screen recording and Accessibility permission detection and failure guidance.
- [x] GPUI startup surfaces missing macOS Screen & System Audio Recording and Accessibility permissions.
- [x] macOS menu bar status item foundation.
- [x] macOS app-level global hotkey listener foundation.
- [x] macOS explicit unsupported capture/clipboard adapter surfaces.
- [x] macOS command-backed still screenshot capture foundation.
- [x] macOS monitor enumeration foundation through AppKit/JXA.
- [x] macOS active-window bounds discovery foundation through `osascript`/System Events.
- [x] macOS native interactive rectangle/window selection foundation through `screencapture -i`.
- [ ] Linux portal-aware screen capture plan.
- [ ] Linux tray/appindicator strategy.
- [x] Linux app-level X11 global hotkey listener foundation.
- [x] Linux hotkey startup blocks Wayland/headless sessions with explicit guidance instead of attempting the X11 path.
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
- [x] Cross-platform explicit-region capture foundations.
- [ ] Production cross-platform region selection UX parity.
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
- [x] macOS cursor color sampling and clipboard copy foundation.
- [x] Linux cursor color sampling and clipboard copy foundation.
- [x] Rust color history persistence and recent-color copy actions.
- [x] Cross-platform image clipboard foundation.
- [x] Cross-platform text clipboard.
- [x] GPUI recent-captures list for capture smoke results.
- [x] Rust settings store for capture output and clipboard preferences.
- [x] GPUI persisted controls for image format, clipboard copy, and cursor inclusion.
- [x] GPUI persisted controls for default capture mode, delay, crosshair, magnifier, and window detection preferences.
- [x] Rust JSON media history store for saved image captures.
- [x] Startup reports corrupt/unreadable Rust history instead of silently showing empty history.
- [x] Post-capture image preview.
- [x] GPUI recent-captures list can reveal saved files on Windows/macOS and open their containing folder on Linux.
- [x] History reveal/open actions check host command exit status before reporting success.

## Milestone 4 - Recording And Media

- [x] Desktop/window GIF recording through the shared FFmpeg recording path.
- [x] Windows FFmpeg-backed desktop video recording start/stop foundation.
- [x] Windows FFmpeg recording can target an explicit capture region.
- [x] Windows recording falls back to video-only when imported audio settings are enabled.
- [x] Linux X11 FFmpeg-backed desktop video recording start/cancel/stop foundation.
- [x] Linux X11 FFmpeg recording can target an explicit capture region.
- [x] Linux recording falls back to video-only when imported audio settings are enabled.
- [x] macOS desktop video recording start/cancel/stop foundation through `screencapture` plus FFmpeg conversion.
- [x] Recording handles fail explicitly when stop is called without a running process.
- [x] Freeform region GIF recording through the GPUI region recording action and shared FFmpeg path.
- [x] GPUI active-window recording uses the explicit-region FFmpeg path.
- [x] Freeform region MP4/WebM recording through the GPUI region recording action and shared FFmpeg path.
- [x] FFmpeg discovery.
- [x] Bundled/AppData FFmpeg discovery parity before PATH fallback.
- [x] Rust recording settings import and FFmpeg output argument model.
- [x] GPUI persisted controls for recording format and quality.
- [x] macOS microphone recording request through native `screencapture -g`, with GPUI status no longer calling it pending.
- [ ] Microphone recording.
- [ ] System audio recording where supported.
- [x] Video thumbnail generation for Rust recording history.
- [x] Recording thumbnails are validated as decodable image files before being shown in history.
- [x] Basic persisted media history index.
- [x] GPUI history rows show media kind and legacy upload metadata, with upload-link copy action.
- [x] GPUI history rows can open stored upload links through the platform browser opener.
- [x] GPUI history rows can copy saved file paths and image captures.
- [x] GPUI history rows can remove entries from the persisted Rust history index without deleting media files.
- [x] GPUI history rows can manually retry uploads with the current upload destination.
- [ ] Full media history UI and actions.
- [x] GPUI app has a dedicated action model module for capture modes, recording targets, pending tool routes, hotkey events, and settings actions.

## Milestone 5 - Advanced OddSnap Features

- [ ] OCR.
- [x] Core OCR line-layout text formatter ported from the legacy Windows OCR result flow.
- [ ] Translation.
- [x] Core translation model, supported-language normalization, source/target resolution, and runtime configuration error rules ported from legacy.
- [ ] Image search.
- [x] Core image-search query normalization, source filtering, scoring, and ranking ported from the legacy matcher.
- [x] Core image-search index record, OCR status labels, diagnostics text, and match-source descriptions ported from legacy.
- [x] GPUI advanced settings summary uses core translation labels and image-search source/exact-match state instead of raw legacy numeric values.
- [ ] Production color picker overlay polish.
- [x] AI Redirect hotkey can open configured chat providers that do not require hosted-image upload and copies the newest saved image.
- [x] Google Lens AI Redirect can upload the newest saved image through the configured AI temporary host and open the Lens URL.
- [x] Upload destination model, credential/HTTPS preflight, size limits, AI Redirect routing, and explicit history status for auto-upload attempts.
- [x] Curl-backed public upload hosts for Catbox, Litterbox, file.io, Uguu, tmpfiles.org, Gofile, and temporary-host fallback.
- [x] Curl-backed credentialed Imgur and ImgBB upload request/response support using imported upload settings.
- [x] Curl-backed credentialed Gyazo and imgpile upload request/response support using imported upload settings.
- [x] Curl-backed custom HTTP multipart uploads with imported field name, headers, and response URL extraction.
- [x] Curl-backed WebDAV and Azure Blob SAS uploads with imported credentials/URLs and empty-success response handling.
- [x] Curl-backed FTP/FTPS uploads with imported credentials/public URL and empty-success response handling.
- [x] Curl-backed S3-compatible uploads using curl SigV4 support with imported endpoint/bucket/keys/public URL.
- [x] Curl-backed SFTP uploads with imported credentials/public URL and host-key SHA-256 fingerprint pinning.
- [x] Curl-backed GitHub uploads with imported token/repo/branch/path-prefix settings and stdin JSON payload support.
- [x] Curl-backed Immich uploads with imported base URL/API key settings and legacy multipart metadata fields.
- [x] Curl-backed Dropbox upload/share-link flow with imported access token/path-prefix settings and existing-link fallback.
- [ ] Upload destinations.
- [ ] Stickers/background removal.
- [ ] Upscale.
- [ ] Local runtime management.
- [ ] Update/install flow.

## Milestone 6 - Migration And Parity Closure

- [x] Import existing save directory/history/copy/image-format/save-naming/recording settings into Rust settings.
- [x] Import legacy capture UX preferences: delay, cursor, magnifier, crosshair, UI scale, toast position, default mode, startup/update toggles.
- [x] Preserve legacy advanced settings for OCR, translation, uploads, image search, tools, toast timing, styling, and custom hotkeys.
- [x] Preserve legacy last capture mode and toast button layout metadata in Rust settings.
- [ ] Import all existing settings.
- [x] Import existing history.
- [x] Import existing color history.
- [ ] Import existing media metadata where practical.
- [ ] Windows full parity verification.
- [ ] macOS full parity verification.
- [ ] Linux full parity verification.
- [x] CI-only Rust rewrite matrix runs fmt/check/test/clippy/build on Windows, Ubuntu, latest Apple Silicon macOS, and Intel macOS compatibility.
- [x] Linux CI lane checks runtime command coverage for current external-tool foundations.
- [x] macOS CI lanes create an unsigned local `.app` bundle smoke artifact without uploading or releasing it.
- [x] macOS package smoke validates the bundled binary includes the current Mac host architecture.
- [x] macOS package smoke validates required privacy strings and Apple Events entitlement.
- [x] Rust packaging readiness notes document current non-release boundary and host dependency caveats.
- [ ] Release decision document.
