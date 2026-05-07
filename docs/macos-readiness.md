# macOS readiness notes

This is local planning only. Do not treat it as a release checklist until the Rust port is feature-complete.

## Local development

- Treat current Apple Silicon macOS as the primary Mac target. Intel Mac support is compatibility coverage, not the main optimization target.
- The Rust CI macOS package smoke runs on the latest Apple Silicon lane first and repeats on Intel only as compatibility coverage; the local package script rejects a binary that does not include the current Mac host architecture.
- Screen capture and recording require macOS permission in System Settings > Privacy & Security > Screen & System Audio Recording.
- Active-window capture requires Accessibility access in System Settings > Privacy & Security > Accessibility because the current foundation asks System Events for the front window bounds.
- While running from a terminal or dev tool, macOS may grant the permission to the launcher instead of the final bundled app.
- After the app is bundled, re-check the bundled OddSnap app in the same settings pane.

## Distribution outside the Mac App Store

- Use an Apple Developer Program account to create a Developer ID Application certificate.
- Sign the `.app` bundle with hardened runtime enabled, a secure timestamp, and `packaging/macos/OddSnap.entitlements`.
- Notarize the signed app with Apple's notary service before shipping a DMG, ZIP, or PKG.
- Staple the accepted notarization ticket to the distributed artifact.
- Do not use a Mac App Distribution, ad hoc, Apple Development, or local development certificate for Developer ID distribution.
- `scripts/macos/package-oddsnap-rust.sh` rejects signing identities that do not resolve to an installed `Developer ID Application` certificate.

## Local package dry run

Run this only on macOS. It creates a local app bundle and ZIP; it does not upload or publish anything.

```sh
scripts/macos/package-oddsnap-rust.sh --unsigned
```

For Developer ID distribution, create the certificate in your Apple Developer account, install it in the macOS keychain, then package with:

```sh
export CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
scripts/macos/package-oddsnap-rust.sh --version 0.1.0
```

For notarization, save credentials once with Xcode's notary tool, then rerun packaging with that keychain profile:

```sh
xcrun notarytool store-credentials "OddSnap Notary" --apple-id "you@example.com" --team-id "TEAMID"
export CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
export NOTARY_PROFILE="OddSnap Notary"
scripts/macos/package-oddsnap-rust.sh --version 0.1.0
```

Expected local verification after signing:

```sh
codesign --verify --deep --strict --verbose=2 dist/macos/OddSnap.app
spctl --assess --type execute --verbose dist/macos/OddSnap.app
xcrun stapler validate dist/macos/OddSnap.app
```

## Local device smoke

Run the unsigned package smoke first on a current Apple Silicon Mac:

```sh
scripts/macos/package-oddsnap-rust.sh --unsigned
open dist/macos/OddSnap.app
```

After opening the bundled app, verify full-screen capture, rectangle capture, active-window capture, color picker, recording start/stop, and every configured hotkey on Apple Silicon macOS. Repeat on Intel macOS only for compatibility validation.

## Current Rust-port implications

- The current macOS capture path shells out to `screencapture`, so permission failures should point users to Screen & System Audio Recording.
- Active-window detection shells out to System Events, so startup now reports Accessibility when that permission is missing. The signed app bundle also needs the Apple Events usage string in `packaging/macos/Info.plist` and the Apple Events entitlement in `packaging/macos/OddSnap.entitlements`.
- Microphone recording needs `NSMicrophoneUsageDescription`; system audio recording is still pending.
- Global hotkeys currently use the app-level `global-hotkey` foundation, not a finished macOS platform service, and need real-device validation on macOS before this can be marked fully available.
- The menu bar status item foundation uses the Rust tray/menu bridge and needs real-device validation on Apple Silicon macOS before it can be marked fully available.
- No release, tag, notarized artifact, or PR is part of this branch-local work.

## Apple references

- Screen & System Audio Recording settings: https://support.apple.com/guide/mac-help/control-access-screen-system-audio-recording-mchld6aa7d23/mac
- Developer ID signing: https://developer.apple.com/developer-id/
- Notarization: https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
- notarytool credential/profile workflow: https://developer.apple.com/documentation/technotes/tn3147-migrating-to-the-latest-notarization-tool
- Apple Events entitlement: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.automation.apple-events
- Apple Events usage string: https://developer.apple.com/documentation/bundleresources/information-property-list/nsappleeventsusagedescription
