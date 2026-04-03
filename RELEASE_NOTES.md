# Yoink v0.5.2.9

## Highlights
- Fixed standalone exe crashing on launch due to missing native WPF libraries.
- Fixed installer not working — it now copies the single-file exe correctly to the install directory.
- Installer detects existing installs, pre-fills the path, and kills old instances before upgrading.
- Fixed version display dropping the 4th component (e.g. showing v0.5.2 instead of v0.5.2.9).
- Fixed update flow failing to relaunch after applying an update.
- Removed portable mode.
