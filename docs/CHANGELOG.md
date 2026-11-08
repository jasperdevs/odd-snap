# OddSnap v0.8.50

## Added

- Add a compact Recent captures sidebar with thumbnails, copy/open/reveal/delete actions, keyboard navigation, and optional automatic opening. (#44)
- Add a configurable Copy action to screenshot previews, with optional close-after-copy. (#77)
- Add an optional show/hide toolbar hotkey and make the tray menu follow the configured capture-tool order. (#81, #82)
- Expand interface localization across 40 languages, including runtime text updates and language-aware fonts. (#91)
- Keep a click-through, capture-excluded region outline visible during scrolling capture. (#75)

## Fixed

- Hide Settings and Recent captures to the tray when minimized instead of leaving a desktop strip; restore the existing window from the tray.
- Preserve resized window bounds when switching focus. (#90)
- Restore editable Settings language-selector text and visible, themed OCR dropdown scrollbars. (#87, #88)
- Close pinned screenshot notifications reliably, including after copying. (#80)
- Preserve Dropbox access parameters when generating direct-image links. (#83)
- Keep saved credentials when importing a redacted settings export; confirm persistence before reporting success and apply imported preferences immediately.
- Write settings exports atomically and reject invalid or unrelated import files.
- Prevent shared icon bitmaps and DXGI resources from being disposed during active use.
- Align rounded window corners and show the current GPL-3.0 license in About. (#74, #86)

## Security

- Update SSH.NET to 2026.0.0 and refresh vulnerable website build dependencies.
