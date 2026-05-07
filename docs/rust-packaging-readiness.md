# Rust packaging readiness

This branch is not a release branch. Do not publish, tag, notarize, or replace the existing OddSnap release pipeline until full feature parity is verified.

## CI gate

The Rust rewrite workflow is CI-only and should prove:

- `cargo fmt --all --check`
- `cargo check --workspace`
- `cargo test --workspace`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo build -p oddsnap-app --bin oddsnap-rust`

The matrix runs on Windows, macOS, and Ubuntu so platform-specific `cfg` paths are compiled on their native runners instead of relying on weak cross-compilation from Windows.

## Linux runner dependencies

Ubuntu runners need native desktop libraries before the GPUI app and future appindicator tray work can compile:

- `libgtk-3-dev`
- `libxdo-dev`
- `libappindicator3-dev`
- `libayatana-appindicator3-dev`

Current Linux runtime foundations also expect optional host tools for feature use:

- still capture: `grim`, `gnome-screenshot`, `spectacle`, or `scrot`
- region selection: `slurp` on Wayland or `slop` on X11
- X11 active window and color picker: `xdotool`
- X11 monitor geometry: `xrandr`
- clipboard: `wl-copy`, `xclip`, or `xsel`

## macOS runner and release notes

macOS CI can compile and test command construction and parsers, but real capture requires user-granted permissions on a desktop session:

- Screen & System Audio Recording for screenshot capture.
- Accessibility for System Events active-window bounds.

Distribution outside the Mac App Store still requires a Developer ID Application certificate, hardened runtime, notarization, and stapling. Those steps are documented in `docs/macos-readiness.md` and are not performed by this branch workflow.

## Current release pipeline boundary

`.github/workflows/build.yml` and `.github/workflows/release.yml` still package the existing Windows .NET app. The Rust rewrite workflow intentionally does not upload artifacts or publish releases yet.
