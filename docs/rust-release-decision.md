# Rust Release Decision

This branch is not approved for release.

The Rust/GPUI port can replace the current OddSnap app only after all of these are true:

- `docs/rust-rewrite-todo.md` has no unchecked parity items.
- `docs/rust-rewrite-parity.md` no longer lists non-parity state.
- Windows, macOS, and Linux have real-device smoke results for capture, recording, hotkeys, tray/menu, clipboard, OCR, scan, uploads, sticker/background removal, upscale, image search, history, lifecycle, and packaging.
- The production capture overlay supports region selection, crosshair guides, magnifier, annotation, ruler, scroll capture controls, and screenshot exclusion where the host OS supports it.
- macOS packaging is signed, hardened, notarized, stapled, and validated on current Apple Silicon macOS before any public replacement build.
- Linux packaging has a documented appindicator/status-icon dependency path and desktop-session smoke evidence for X11 and Wayland-supported features.
- The Rust release/update channel is wired to a tested artifact pipeline and does not reuse the old .NET release assets by accident.
- The old Windows release workflow is either intentionally retired or explicitly kept separate from Rust artifacts.

Until then:

- Do not publish a Rust release.
- Do not tag a Rust version.
- Do not replace the existing app installer.
- Do not wire the Rust workflow to upload public artifacts.
- Keep work local to the rewrite branch except normal commits/pushes for checkpointing.
