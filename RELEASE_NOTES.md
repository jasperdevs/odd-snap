# Yoink v0.6.1

## Highlights
- Reworked screenshot-mode hover performance so window detection, magnifier updates, and selection feedback no longer feel delayed behind the cursor.
- Fixed multiple capture-overlay paint artifacts across normal screenshot, scrolling capture, and recording selection flows.
- Restored real annotation hotkeys and tooltip labels so the overlay matches the configured tool bindings instead of shifted tool positions.
- Added capture magnifier setup controls to onboarding and kept uninstall cleanup explicit for downloaded local sticker model caches.

## Added
- Added a capture-magnifier preference to onboarding so new installs can enable or disable the loupe immediately.
- Added a lightweight dedicated crosshair guide window path instead of routing guide rendering through the main overlay paint path.
- Added explicit uninstall cleanup for local runtime caches used by sticker/background-removal models.

## Changed
- Changed hover window detection to use direct point lookup with top-level z-order probing instead of desktop-wide window enumeration.
- Changed selection overlay invalidation to avoid creating a graphics context on every drag frame just to measure the size label.
- Changed capture magnifier behavior so it stays visible during active selection, hides over dock/popup UI, and follows the cursor more tightly.
- Changed active-tool visuals so the selected tool indicator is dimmer and no longer uses the overly bright stroked circle.

## Removed
- Removed position-based annotation hotkey switching in the overlay; tool switching now follows saved hotkey mappings only.
- Removed the reversible/XOR crosshair experiment that caused visual corruption and clipping artifacts.
- Removed reliance on implicit app-data deletion alone for local model cleanup during uninstall.

## Fixed
- Fixed screenshot-mode magnifier updates stalling or only refreshing every few seconds.
- Fixed hover tooltip hotkeys showing the wrong number even when the configured tool hotkey was different.
- Fixed scrolling capture and recording selection leaving paint trails or stale UI behind the moving selection/magnifier.
- Fixed window-detection preview regressing into a broken or inconsistent border path.
- Fixed screenshot crosshair guides lagging behind cursor movement and glitching when crossing overlay UI.
- Fixed uninstall cleanup leaving downloaded local sticker model/runtime data behind.
