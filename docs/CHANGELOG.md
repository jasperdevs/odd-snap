# Yoink v0.8.18

## Added
- Add month folders for new automatic screenshot, sticker, upscale, GIF, and video saves.
- Add History timing diagnostics for image, media, text, color, thumbnail, and tab-load paths.

## Changed
- Page History items in smaller automatic batches across images, videos/GIFs, stickers, text, and colors.
- Cache media History entries and move video thumbnail cleanup off the tab-load path.
- Protect saved API keys and tokens with user-scoped DPAPI and redact secrets from exports and logs.
- Use cached recording frames for video preview instead of launching FFmpeg again after encode.
- Speed up GIF encoding with FFmpeg palette rectangle diffing.
- Speed up WebM encoding with VP9 row multithreading while keeping constant-quality output.
- Add MP4 faststart metadata for faster playback after sharing or uploading recordings.

## Fixed
- Open History directly from the tray menu without flashing or sticking on the General tab.
- Keep runtime/model install buttons in sync after successful setup for translation, semantic search, rembg, and upscale runtimes.
- Avoid blocking UI-thread garbage collections during idle memory trimming.
- Preserve existing root-level screenshots while keeping new captures organized by month.
- Reduce History lag while capturing by debouncing search indexing and refreshing loaded entries incrementally.
