# Yoink v0.8.10

## Highlights
- Added the new Upscale capture flow and cleaned up the surrounding uploads, AI Redirect, toast, and settings UX so the release is consistent end to end.

## Changed
- Added the Upscale tool, local model/runtime management, preview window, model switching, and multiplier controls.
- Added an AI Redirect uploads tab and toast button flow, including direct reuse of the active screenshot toast for ChatGPT, Claude, and Gemini.
- Simplified the settings window layout by trimming redundant headers and copy, tightening toast/button layout editing, and reducing provider-card clutter.
- Updated the embedded Streamline icon set for Upscale and AI Redirects and refreshed theme-aware tray icon rendering.

## Fixed
- Fixed local upscale model download file-lock failures.
- Fixed multiple null/initialization errors in the upscale preview window.
- Fixed the before/after compare behavior so the drag direction and fitted-image alignment behave correctly.
- Fixed the AI Redirect toast button so it appears when placed, follows the toast layout editor, and no longer spawns an extra toast for non-Lens providers.
- Fixed shimmer/loading behavior so status text animates more cleanly and duplicate progress copy is suppressed.

# Yoink v0.8.9

## Highlights
- Reissued the latest Velopack-based release from the current verified mainline after the previous release was removed.

## Changed
- Refreshed the shipped patch release tag on top of the same validated Velopack/update and security-fix baseline.

# Yoink v0.8.8

## Highlights
- Blocked insecure automatic installs for Argos Translate and the open-source local translation runtime.

## Fixed
- Yoink no longer auto-installs `argostranslate` or auto-downloads Argos language packs at runtime.
- Yoink no longer auto-installs the open-source local translation Python/model stack at runtime.
- Translation settings now fail closed with a clear manual-install message instead of downloading unverified packages or models.

# Yoink v0.8.7

## Highlights
- Switched release packaging and app updates over to Velopack.

## Changed
- Release publishing now packages Yoink with Velopack instead of the old hand-rolled zip/update flow.
- The app now initializes through `VelopackApp.Build().Run()` on startup.
- Settings updates now use Velopack's `UpdateManager` against GitHub releases when running in a normal installed/packaged context.
- The old extra winget-on-release workflow was removed in favor of a single Velopack-driven release lane.

## Fixed
- Removes the custom self-updater from the normal update path.
- Removes the custom install wizard from the normal startup path.

# Yoink v0.8.4.9

## Highlights
- Switched Yoink to a portable-first flow: no custom install wizard on launch and no self-applying updater in Settings.

## Changed
- Yoink no longer auto-opens the custom installer wizard when run from an arbitrary folder.
- Settings update actions now open the latest GitHub release instead of trying to self-apply an in-place update.

## Fixed
- Removes the broken custom install/update path from normal user flows while a better installer/updater solution is chosen.

# Yoink v0.8.4.8

## Highlights
- Installer failures now produce an actionable message and a saved log path instead of a bare null-path crash.

## Fixed
- Install-time path resolution now throws clearer installer-specific errors when parent folders cannot be determined.
- Failed installs now write a log under `%TEMP%\\Yoink\\InstallLogs\\` and tell users to try the default path or disable shortcut creation.

# Yoink v0.8.4.7

## Highlights
- The installer now completes even if bundled semantic-search preparation hits a machine-specific failure.

## Fixed
- Runtime preparation during install is now non-fatal; Yoink installs successfully and can finish preparing semantic search after launch instead of showing `Installation failed`.

# Yoink v0.8.4.6

## Highlights
- Hardened installer steps so optional shortcut and registration work cannot fail the full install.

## Fixed
- Install now treats desktop shortcut creation, Start Menu shortcut creation, app registration, and startup registration as best-effort instead of fatal.
- Target-directory normalization now falls back to the default install location if Windows or UI state provides an unusable path.

# Yoink v0.8.4.5

## Highlights
- Fixed another installer crash path so first-run installs no longer fail when shortcut-folder resolution returns an empty directory path.

## Fixed
- Desktop/Start Menu shortcut creation now safely no-ops when Windows does not provide a usable shortcut directory instead of crashing with `Value cannot be null. (Parameter 'path')`.

# Yoink v0.8.4.4

## Highlights
- Fixed in-app install/update path handling so blank or missing target paths fall back safely instead of failing with a null-path error.

## Fixed
- Install and update flows now normalize target directories before touching `Path` or `File` APIs.
- The install wizard now falls back to the installed/default Yoink path when its path textbox is blank.
- Launch/install/update path resolution is consistent across fresh installs, installed copies, and update helper flows.

# Yoink v0.8.4.3

## Highlights
- Fixed the in-app self-updater so update installs can complete from Settings without tripping over the downloaded ZIP file.

## Fixed
- The update package is now fully closed before checksum verification runs, which prevents the `Yoink-win-x64.zip is being used by another process` failure during in-app updates.

# Yoink v0.8.4.2

## Highlights
- Simplified AI Redirect targets to the four primary destinations: ChatGPT, Claude, Gemini, and Google Lens.
- Improved text annotation editing so placed text can be repositioned directly while editing.
- Added a background text style for callout-style annotations.
- Expanded upload destination coverage in the settings UI to surface the full provider set.

## Added
- Inline `Bg` text-style toggle that renders selected-color text backgrounds with white text and optional stroke/shadow.
- `tmpfiles.org` and `transfer.sh` surfaced as explicit upload destination options in the upload selectors.

## Changed
- The AI Redirect provider picker now presents `Claude` as a single option instead of splitting `Claude` and `Claude Opus`.
- Legacy `Claude Opus` selections now normalize to the standard Claude redirect flow.
- Active text annotations can be dragged directly without switching to Select mode first.
- Upload destination lists now expose the complete supported provider lineup in the app UI.

## Improved
- Text annotation bounds now account for the new background style so selection, hit testing, and resizing stay aligned.
- AI redirect workflows are easier to reason about with a smaller provider list focused on the main supported destinations.

## Fixed
- Re-editing existing text annotations now preserves and restores the new text background style correctly.
- Upload destination pickers no longer hide supported temporary-host options that already exist in the app.

# Yoink v0.8.4.1

## Highlights
- Redesigned the capture dock with support for top, bottom, left, and right placement.
- Refreshed the capture, toast, and settings surfaces with a full Streamline-based icon set.
- Added a drag-and-drop toast button layout editor with configurable close, pin, save, and delete placement.
- Expanded local history and image search with better storage, migration, diagnostics, and runtime preparation.

## Added
- Streamline icon rendering helpers for both WinForms and WPF UI surfaces.
- New embedded icon assets for capture tools, toast actions, and settings UI.
- Persistent background runtime job tracking for long-running setup tasks.
- Bundled local semantic-search runtime assets and setup flow for CLIP-based search.
- SQLite-backed history storage for screenshot, OCR, and color history.
- Settings infrastructure for media caching plus reorganized settings/history/media code paths.

## Changed
- The capture dock now supports configurable placement on all four screen edges.
- Annotation interactions were refined with better text editing behavior, larger handles, and cleaner repainting while dragging or resizing.
- Toast previews now support richer layout options, auto-pin behavior, and configurable overlay buttons.
- Capture preferences now include dock placement and all-monitor overlay behavior.
- Image-search preferences now expose visibility, source selection, exact-match mode, and diagnostics.
- Startup, shutdown, capture, upload, and settings code were reorganized into more focused partial files and folders.

## Improved
- Multi-monitor dock anchoring behaves more reliably across mixed monitor layouts, including negative-coordinate displays.
- Overlay hit targets and picker layout were tightened up for easier interaction.
- Settings persistence now buffers disk writes, keeps a process cache, and applies migrations/defaults more safely.
- Legacy history data can be migrated into the new storage layout automatically.
- Semantic-search preparation is surfaced more clearly through runtime status and settings workflows.
- Upload settings migration now normalizes older AI chat and temporary-host configurations more safely.

## Fixed
- History no longer depends on the old JSON-only AppData layout; screenshots, OCR history, and color history migrate into the newer Pictures-based store.
- Temporary-host uploads now support `tmpfiles.org` as a first-class destination and normalize older host settings correctly.
- Hotkey registration is more reliable when only secondary hotkeys are enabled or when hotkeys are re-registered from changed settings.
- OCR cleanup now disposes WinRT imaging objects deterministically during recognition.
- Shared DXGI capture resources are guarded more safely during warm-up and capture.
- Video recording cleans up desktop-audio and mux-process resources more deterministically.
- Clipboard PNG copy avoids an unnecessary extra buffer clone in the common image-copy path.
- Installer payload selection now copies the full published app instead of an incomplete exe-only subset when installing from a published build.

## Internal
- Release packaging continues to produce per-architecture portable `.exe` and `.zip` artifacts for GitHub releases and winget.
- Tests cover toolbar placement, toast specs, history location, upload normalization, and settings migrations tied to this release.
