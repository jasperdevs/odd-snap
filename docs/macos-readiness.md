# macOS readiness notes

This is local planning only. Do not treat it as a release checklist until the Rust port is feature-complete.

## Local development

- Screen capture and recording require macOS permission in System Settings > Privacy & Security > Screen & System Audio Recording.
- While running from a terminal or dev tool, macOS may grant the permission to the launcher instead of the final bundled app.
- After the app is bundled, re-check the bundled OddSnap app in the same settings pane.

## Distribution outside the Mac App Store

- Use an Apple Developer Program account to create a Developer ID Application certificate.
- Sign the `.app` bundle with hardened runtime enabled.
- Notarize the signed app with Apple's notary service before shipping a DMG, ZIP, or PKG.
- Staple the accepted notarization ticket to the distributed artifact.
- Do not use a Mac App Distribution, ad hoc, Apple Development, or local development certificate for Developer ID distribution.

## Current Rust-port implications

- The current macOS capture path shells out to `screencapture`, so permission failures should point users to Screen & System Audio Recording.
- Global hotkeys use the app-level `global-hotkey` foundation and need real-device validation on macOS before this can be marked fully available.
- No release, tag, notarized artifact, or PR is part of this branch-local work.

## Apple references

- Screen & System Audio Recording settings: https://support.apple.com/guide/mac-help/control-access-screen-system-audio-recording-mchld6aa7d23/mac
- Developer ID signing: https://developer.apple.com/developer-id/
- Notarization: https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
