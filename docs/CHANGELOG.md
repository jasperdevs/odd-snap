# OddSnap v0.8.29

## Changed
- replace bundled PNG toolbar assets with Windows Fluent/MDL2 icon rendering.
- lower the desktop app target to Windows 10 19041.
- pin WinUI package versions used by the experimental shell.

## Fixed
- fix DXGI region capture to copy only the selected monitor overlap.
- speed up GIF recording by streaming raw frames to FFmpeg.
- reduce selection, toolbar, magnifier, color picker, and history redraw work.
- avoid temp files for PNG saves and DeepAI upscale uploads.
- fix OCR and color history search cache churn.
