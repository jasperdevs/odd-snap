# OddSnap v0.8.44

## Improved
- Switching the Windows light/dark theme now restyles all open OddSnap windows instantly instead of requiring them to be reopened.
- Keyboard navigation now shows a visible focus outline on buttons and settings tabs.
- Window borders and shadows now follow the system dark mode.
- The upscale Before/After labels now stay readable over light images.

## Security
- Immich and custom HTTP upload destinations now require HTTPS, matching WebDAV and S3.
- Diagnostics logs now redact SAS and presigned URL signatures.
- Release code signing now uses the HTTPS timestamp server.
