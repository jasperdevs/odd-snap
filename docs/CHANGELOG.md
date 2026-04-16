# Yoink v0.8.19

## Added
- Add persisted video History entries for MP4, WebM, and MKV recordings.
- Add themed confirmation dialogs for destructive Settings and History actions.

## Changed
- Cache History thumbnails on disk so images, GIFs, stickers, and videos reload faster after restart.
- Resize History cards with the settings window across images, videos/GIFs, and stickers.
- Render section headers for Settings cards and upload provider panels.

## Fixed
- Keep Select mode active across image, video/GIF, sticker, text, and color History lists.
- Make Escape close the screenshot overlay from text input, pickers, flyouts, and toolbar focus.
- Remove the idle screenshot overlay dash border that intersected the toolbar dock.
- Persist and recover recorded videos in History instead of only discovering top-level files.
- Use the correct DXGI raw-rectangle conversion during capture.
