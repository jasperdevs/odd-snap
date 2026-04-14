# Yoink v0.8.16

## Changed
- Move local sticker and upscale runtimes to compatible Yoink-managed Python environments and rebuild incompatible cached runtimes automatically.
- Make semantic search lazy and trim CLIP sessions, search caches, and history thumbnail caches more aggressively while idle.
- Route sound playback through a bounded shared worker instead of spawning a thread per sound.

## Fixed
- Terminate local Python helper processes when sticker, upscale, and translation jobs are canceled.
- Dispose OCR translation cancellation tokens and clear stale hotkey editor textbox references.
- Clean up temporary preview PNGs after drag-out from unsaved captures.
