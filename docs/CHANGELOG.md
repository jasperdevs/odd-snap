# Yoink v0.8.18

## Added
- Add month folders for new automatic screenshot, sticker, upscale, GIF, and video saves.
- Add History timing diagnostics for image, media, text, color, thumbnail, and tab-load paths.

## Changed
- Page History items in smaller automatic batches across images, videos/GIFs, stickers, text, and colors.
- Cache media History entries and move video thumbnail cleanup off the tab-load path.
- Protect saved API keys and tokens with user-scoped DPAPI and redact secrets from exports and logs.
- Use cached recording frames for video preview to avoid a second FFmpeg thumbnail pass.
- Speed up GIF and WebM encoding with FFmpeg palette rectangle diffing and VP9 row multithreading.

## Fixed
- Open History directly from the tray menu without flashing or sticking on the General tab.
- Keep runtime/model install buttons in sync after successful setup for translation, semantic search, rembg, and upscale runtimes.
- Avoid blocking UI-thread garbage collections during idle memory trimming.
- Preserve existing root-level screenshots while keeping new captures organized by month.

# Yoink v0.8.17

## Added
- Add shared app-model settings schemas, job contracts, and a WinUI 3 migration shell to the solution.
- Add recorder and sound-service regression tests plus a temp PNG capture-output helper.

## Changed
- Refresh the docs site home, downloads, changelog, donate, hotkeys, and not-found pages.
- Tighten local sticker, upscale, and translation runtimes around Yoink-managed Python environments and shared process helpers.

## Fixed
- Keep video recordings on the wall-clock timeline by filling missed frame slots instead of shortening the output when capture falls behind.
- Validate and repair recording duration after encode when FFmpeg output lands outside the expected timeline tolerance.
- Suppress Yoink UI sounds during desktop-audio recording so the app does not capture its own cues.
- Preserve history entry classification during migration and expose runtime job snapshots through the shared job model.

# Yoink v0.8.16

## Changed
- Move local sticker and upscale runtimes to compatible Yoink-managed Python environments and rebuild incompatible cached runtimes automatically.
- Make semantic search lazy and trim CLIP sessions, search caches, and history thumbnail caches more aggressively while idle.
- Route sound playback through a bounded shared worker instead of spawning a thread per sound.

## Fixed
- Terminate local Python helper processes when sticker, upscale, and translation jobs are canceled.
- Dispose OCR translation cancellation tokens and clear stale hotkey editor textbox references.
- Clean up temporary preview PNGs after drag-out from unsaved captures.
