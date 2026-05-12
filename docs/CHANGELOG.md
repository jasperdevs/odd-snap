# OddSnap v0.8.37

## Changed
- reduce first region-capture latency with warmed capture, overlay, and selection chrome paths.
- coalesce region-selection drag repaint and magnifier work to the display frame interval.
- reduce recording CPU pressure with realtime FFmpeg presets and bounded encoder threads.
- keep the tray icon visible on dark Windows taskbars and add full-screen capture to the tray menu.

## Fixed
- preserve crosshair and pixel magnifier chrome while selecting without extra overlay windows.
- fix toast preview entry and update ghosting with a stroke-free shell and atomic content swaps.
- capture layered windows consistently in BitBlt screenshot and recording paths.
- harden OCR, upload, history indexing, scrolling capture, and recording cancellation cleanup.

## Fixed issues
- fixes #47
- fixes #48
