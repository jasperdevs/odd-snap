# Rust packaging readiness

This branch is not a release branch. Do not publish, tag, notarize, or replace the existing OddSnap release pipeline until full feature parity is verified.

## CI gate

The Rust rewrite workflow is CI-only and should prove:

- `cargo fmt --all --check`
- `cargo check --workspace`
- `cargo test --workspace`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo build -p oddsnap-app --bin oddsnap-rust`
- Linux runtime command preflight for the external tools used by current capture, clipboard, region-selection, active-window, color-picker, and recording foundations
- unsigned macOS `.app` bundle creation on macOS runners, including host-architecture validation and no artifact upload

The matrix runs on Windows, Ubuntu, primary Apple Silicon macOS (`macos-26`), and Intel macOS compatibility (`macos-26-intel`) runners so platform-specific `cfg` paths are compiled on native runners instead of relying on weak cross-compilation from Windows.

## Linux runner dependencies

Ubuntu runners need native desktop libraries before the GPUI app and future appindicator tray work can compile:

- `libgtk-3-dev`
- `libxdo-dev`
- `libayatana-appindicator3-dev`

Do not install `libappindicator3-dev` and `libayatana-appindicator3-dev` together on Ubuntu 24.04 runners; their runtime packages conflict.

Current Linux runtime foundations also expect optional host tools for feature use:

- still capture: `grim`, `gnome-screenshot`, `spectacle`, or `scrot`
- region selection: `slurp` on Wayland or `slop` on X11
- X11 active window and color picker: `xdotool`
- X11 monitor geometry: `xrandr`
- clipboard: `wl-copy`, `xclip`, or `xsel`
- X11 recording: `ffmpeg` with `x11grab` support and a valid `DISPLAY`

The CI lane installs and checks representative command coverage for these foundations (`ffmpeg`, `grim`, `gnome-screenshot`, `scrot`, `slurp`, `slop`, `xdotool`, `xrandr`, `wl-copy`, `xclip`, and `xsel`). It does not prove real desktop capture in a granted graphical session.

## macOS runner and release notes

macOS CI can compile and test command construction and parsers, but real capture requires user-granted permissions on a desktop session:

- Screen & System Audio Recording for screenshot capture.
- Accessibility for System Events active-window bounds.

The primary macOS CI lane is latest Apple Silicon (`macos-26`). Intel macOS is kept as a compatibility lane (`macos-26-intel`), not the main product target. GitHub's hosted runner labels can move over time, so re-check the official runner table before release hardening.

Distribution outside the Mac App Store still requires a Developer ID Application certificate, hardened runtime, notarization, and stapling. Those steps are documented in `docs/macos-readiness.md` and are not performed by this branch workflow.

The repo now includes local-only macOS package scaffolding:

- `packaging/macos/Info.plist`
- `packaging/macos/OddSnap.entitlements`
- `scripts/macos/package-oddsnap-rust.sh`

That script is intentionally not wired to publish artifacts. CI runs it only as an unsigned package smoke on the macOS lanes after the debug binary build, and the script rejects a binary that does not include the current Mac host architecture. It also validates required macOS privacy strings and the Apple Events entitlement before the signing/notarization branch. Locally, it can build an unsigned `.app`/ZIP for smoke testing, or use `CODESIGN_IDENTITY` and `NOTARY_PROFILE` on a Mac when Developer ID signing and notarization are ready.

## Current release pipeline boundary

`.github/workflows/build.yml` and `.github/workflows/release.yml` still package the existing Windows .NET app. The Rust rewrite workflow intentionally does not upload artifacts or publish releases yet.
