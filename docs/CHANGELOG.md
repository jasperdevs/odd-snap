# OddSnap v0.8.40

## Changed
- encode MP4/MKV recordings with OBS-style x264 settings: CRF 18, High profile, 2-second keyframes, and no zerolatency tune.
- sharpen scaled video recordings with Lanczos filtering.

## Fixed
- find FFmpeg from common local WinGet, Scoop, Chocolatey, and app-folder installs instead of caching a missing encoder result.
