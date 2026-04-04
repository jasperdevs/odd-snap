# Yoink v0.6.0

## Highlights
- Smart image search now supports prefix matching, so partial queries like `broke` can find `brokecli`.
- Settings opens with fail-soft startup handling, so one broken subpanel no longer crashes the entire app.
- Removed the always-on image-card outline in History so screenshots no longer show a stray border on hover or selection.
- Made OCR preprocessing faster by switching the grayscale/threshold pass to a lock-bits pipeline.
- Kept the local image search backlog stable while preserving the existing exact/OCR search behavior.
- Fixed standalone exe crashing on launch due to missing native WPF libraries.
- Fixed installer not working — it now copies the single-file exe correctly to the install directory.
- Installer detects existing installs, pre-fills the path, and kills old instances before upgrading.
- Fixed version display dropping the 4th component.
- Fixed update flow failing to relaunch after applying an update.
- Removed portable mode.
