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
