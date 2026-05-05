# OddSnap v0.8.35

## Added
- add confirmations and visible cancel status for destructive History, Settings reset, model removal, runtime uninstall, image-index reset, and app uninstall flows.
- add retry and recovery states for failed History loads, uploads, runtime jobs, updates, imports, resets, external links, and file actions.
- add keyboard access, automation names, help text, tooltips, and live-region metadata across Settings, History cards, generated text/code/color cards, media cards, toast layout controls, overlays, and support/about links.
- add diagnostics for install/uninstall cleanup, temp-file cleanup, thumbnail generation, runtime jobs, uploads, preview/toast failures, OCR cleanup, capture output, and media recording.
- add safeguards against repeated activation for preview saves, toast actions, upload tests, update actions, runtime/model actions, project links, local model removal, and AI Redirect actions.

## Changed
- make destructive History prompts and follow-up status count-aware, category-aware, and clear about preserved items.
- make upload, AI Redirect, preview, toast, History, runtime, and external-link failures show concrete recovery steps instead of raw errors.
- make Settings preference failures roll back the UI and leave durable inline status across upload, AI Redirect, tools, capture, toast, general, recording, history, OCR, translation, sticker, upscale, and setup wizard flows.
- make credential and upload-provider fields safer with masked secrets, bounded widths, clearer labels, and explicit help text.
- make History search, upload filters, generated cards, media metadata, thumbnail placeholders, and empty states reflect the active filter/category.
- make OCR result translation, source editing, setup, copy, cancellation, and layout paths reset stale state and clean up correctly.
- make preview/toast hover, pin, save, Office export, drag, refresh, and force-close behavior preserve or restore dismiss state correctly.
- make the Settings window use a normal fixed minimum size for the two-column layout and keep Capture from creating horizontal overflow.
- make capture menus and toolbar popups clamp to the active monitor and keep oversized toolbars inside the visible screen.

## Fixed
- fix stale preview/toast callbacks, drag/click/open/save paths, and missing-file cases that could act on closed windows or lose user feedback.
- fix upload retry, provider normalization, and failure precedence paths so failed uploads keep useful provider/file recovery context.
- fix copy failures for screenshots, text, code, colors, media, stickers, local model details, OCR output, and upload links.
- fix runtime job persistence, cancellation, refresh, removal, and failure states that could leave confusing stale status or locked buttons.
- fix History delete, search, reindex, upload-filter, missing-file, and thumbnail failure paths so recoverable status remains visible.
- fix import/reset, app update, startup, install, uninstall, and cleanup failures so previous state is restored or clearly reported.
- fix thumbnail cache waiters, stale placeholders, unreadable video thumbnails, blank thumbnail rejection, orphan cleanup, and recording temp-file cleanup.
- fix capture/recording cleanup, clipboard failure feedback, DXGI capture fallback diagnostics, GIF/video placeholder retry, and capture output cleanup.
- fix OCR result wrapping, footer layout, translation cancellation, source edit reset, setup retry, copy no-op feedback, and translation failure states.
- fix controls with missing labels, missing help text, missing tooltip binding, missing live status announcement, or Windows 10/11 styling mismatches.

# OddSnap v0.8.34

## Added
- add a dedicated Codes history page split out from the Text history view.

## Changed
- match history filter dropdowns to the rest of Settings so they look consistent.
- shrink website screenshots by replacing PNG sources with WebP files.
- simplify the website icon and shape context modules.

## Fixed
- eliminate paint smears around the ruler and other annotation tools by tracking the live preview's full paint extent each frame.
- show a close button on every toast, including text-only and inline-color toasts.
- restore page-title clearance on the History tab so the dropdown row no longer sits behind the heading.

# OddSnap v0.8.33

## Added
- add an optional toast `Open with` button for Windows app picker and installed Office apps.
- add a manual UI scale setting for OddSnap windows, toasts, and capture controls.
- add history upload filters for uploaded, failed, and provider-specific captures.

## Changed
- speed up video and GIF history by warming thumbnails in small background batches.
- update local sticker and upscale runtimes for Python 3.11-3.14 and newer ONNX packages.
- keep Windows release coverage for x64, x86, and ARM64 builds checked in tests.

## Fixed
- save upload failures in history so provider errors stay visible after the toast closes.
- harden file.io, Google Drive, Dropbox, Immich, and temporary-host upload paths.
- fix light-mode colors in the toast layout designer and upscale result window.
- prevent recording startup failures from leaving capture state half-open.
- cache Windows OCR engines and language lists to reduce repeated setup work.

# OddSnap v0.8.32

## Fixed
- smooth draw, arrow, and curved arrow annotation rendering.
- fix blur, highlight, emoji picker, and magnifier annotation paint issues.
- keep recording and capture chrome out of captured output more reliably.
- reduce annotation preview repaint work during mouse movement.

# OddSnap v0.8.31

## Added
- add automatic and manual scrolling capture modes.
- add Escape handling for capture overlays, recording, and scrolling capture.

## Fixed
- reduce scrolling capture bitmap memory use by stitching accepted frames as capture runs.
- keep release asset names aligned across build and release outputs.
- split the website bundle into smaller chunks and clear the PostCSS audit advisory.
