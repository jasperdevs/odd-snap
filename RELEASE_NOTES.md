# Yoink v0.8.3.3

## Changed
- Capture overlay startup now defers full-frame pixel caching until a tool actually needs per-pixel reads, so screenshot mode opens faster.
- Toast timeout progress now sits on a proper bottom rail inside the toast shell instead of an inset floating strip.
- OCR result text areas now use cleaner local scrollbar chrome.
- Window snapping now resolves windows from the live top-level z-order instead of relying on stale cached rectangles or owner promotion.

## Fixed
- Win and Hyper hotkey chords now capture correctly in Settings, Setup, and tool hotkey UI.
- OCR output now preserves line breaks, paragraph spacing, and visible first-line indents more reliably.
- Toasts no longer show the white box artifact under rounded corners.
- Window detection no longer snaps to shell surfaces, helper overlays, or other non-window elements.
- Fullscreen fallback and detected-window outlines no longer appear active at the same time.
- Invisible owner/helper windows no longer win over the actual front-most window under the cursor.
- OCR text capture UI no longer shows the stray `X`-looking control in the scroll area.
- Screenshot capture feels more responsive on open by removing eager pixel-cache work from the hot path.
