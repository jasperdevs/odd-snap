# Yoink v0.8.3.5

## Added
- `AI Redirects` upload destination for opening ChatGPT, Claude, Claude Opus, Gemini, or Google Lens in the browser.
- Optional dedicated AI redirect hotkey in Upload settings.
- `Google Lens` redirect target with hosted-image handoff.
- `Filter between free temporary hosts` upload option that rotates through free temporary hosts until one works.

## Changed
- Renamed the old AI upload entry to `✽ AI Redirects`.
- AI redirect captures now use a pinned preview toast for drag-and-drop or `Ctrl+V` fallback instead of brittle browser auto-paste.
- Google Lens now uses hosted-image redirect behavior and can use the rotating temporary-host option.
- Upload settings hide the generic auto-upload toggles while `AI Redirects` is selected.
- Upload and Lens host lists now keep temporary hosts grouped at the top.
- `AI Redirects` is pinned to the top of the upload destination list.

## Fixed
- OCR result window now closes with `Esc` or by clicking away.
- Dragging a preview toast now dismisses it after the drag session ends.
- Successful Google Lens redirects no longer leave a pinned drag/drop toast behind.
- Temporary-host fallback reporting now shows the host that actually succeeded.
- Settings footer actions now use a stable layout instead of the broken wrapped row.
- Recording and scrolling capture overlays were hardened after the recent UI regressions; scrolling capture was rolled back to a conservative non-sticky path so it cannot trap the desktop.
- Recording start was adjusted to reduce first-frame UI leakage and startup transition artifacts.
