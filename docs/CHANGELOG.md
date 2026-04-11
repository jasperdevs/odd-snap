# Yoink v0.8.12

## Highlights
- Added the new Upscale capture flow with local model/runtime management and preview controls.
- Reworked AI Redirects so the toast button, hotkey flow, and Google Lens upload handling behave consistently.
- Cleaned up the settings UI to reduce redundant labels and helper text across uploads, toast, OCR, and related pages.

## Changed
- Added the Upscale tool, local model/runtime management, preview window, model switching, and multiplier controls.
- Added an AI Redirect uploads tab and toast button flow, including direct reuse of the active screenshot toast for ChatGPT, Claude, and Gemini.
- Refreshed the embedded Upscale and AI Redirect icon assets and tightened tray/theme icon handling.

## Fixed
- Fixed local upscale model download file-lock failures.
- Fixed multiple initialization and null-state issues in the upscale preview window.
- Fixed the before/after compare behavior so drag direction and fitted-image alignment behave correctly.
- Fixed the AI Redirect toast button so it appears when placed, follows the toast layout editor, and no longer spawns an extra toast for non-Lens providers.
- Fixed shimmer/loading behavior so status text animates more cleanly and duplicate progress copy is suppressed.
- Fixed OCR translation setup messaging so blocked automatic installs now use a manual-setup flow instead of a failing install action.
- Fixed release packaging so user-facing setup/portable asset names are clean and the installer uses the app icon.
