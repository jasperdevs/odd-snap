# OddSnap v0.8.27

## Added
- add a monthly-folder toggle and editable filename pattern for automatic capture saves (#30).

## Changed
- raise MP4/MKV/WebM recording quality and skip no-op scaling for original-size recordings.
- preserve Velopack release asset names required by in-app updates.

## Fixed
- keep OddSnap recorder, scrolling, and capture overlay chrome out of screenshots and capture frames.
- speed up tray menu opening and allow tray/record-hotkey recording stop.
- fix winget manifest generation for the Velopack setup installer (#31).
- avoid overwriting captures when custom filename patterns repeat within the same minute.
