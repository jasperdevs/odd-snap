# OddSnap v0.8.45

## New
- Settings search: type in the new "Find a setting" box in the sidebar to jump straight to any setting on any page, with the matching card highlighted.
- The settings sidebar now shows the app logo and version, with a roomier Windows 11-style navigation rail and selection indicator.

## Improved
- Larger settings window with bigger page titles and more comfortable spacing throughout.
- The OCR translation loading shimmer is now visible in light mode.
- The color palette is now single-sourced across the WPF windows and the capture chrome, so light/dark styling can no longer drift between surfaces.

## Under the hood
- Added the project's first automated test suite (292 tests covering settings persistence and migrations, upload URL validation, localization fallback, filename templates, hotkey formatting, history utilities, and toolbar layout).
- CI now builds the full solution and runs the test suite on every push and pull request, and releases are blocked if tests fail.
- Website: added a web app manifest and icons, canonical/social metadata, structured data, a skip-to-content link, and image sizing for faster, steadier loading.
