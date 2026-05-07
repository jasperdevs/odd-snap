# Rust GPUI Architecture

This rewrite should behave like a native shell over reusable feature modules, not like a hard-coded UI clone.

## Boundaries

- `crates/oddsnap-core`: portable rules, settings, history records, upload plans, translation parsing, image-search ranking, OCR/index state, and other logic that should not know about GPUI or an operating system.
- `crates/oddsnap-platform`: shared platform traits and portable helpers for images, capture persistence, OCR fallback, and service contracts.
- `crates/oddsnap-platform-windows`: Windows adapters for capture, clipboard, hotkeys, tray, color picking, recording, screenshot exclusion, and native WinRT OCR.
- `crates/oddsnap-platform-macos`: macOS adapters for capture, clipboard, menu bar, hotkeys, color picking, recording, and permissions.
- `crates/oddsnap-platform-linux`: Linux adapters for X11 capture/hotkeys/color picking, clipboard, and recording foundations.
- `crates/oddsnap-app/src/actions.rs`: app action enums and routing metadata.
- `crates/oddsnap-app/src/ui.rs`: reusable GPUI skin, colors, spacing, and button variants.
- `crates/oddsnap-app/src/hotkeys.rs`: imported hotkey accelerator mapping, duplicate checks, cross-platform listener startup, and Linux session gating.
- `crates/oddsnap-app/src/image_search.rs`: GPUI-facing image-search state and view helpers.
- `crates/oddsnap-app/src/media_history.rs`: GPUI-facing media-history filters.
- `crates/oddsnap-app/src/ocr_translation.rs`: GPUI-facing OCR translation workflow.
- `crates/oddsnap-app/src/main.rs`: application composition, state ownership, render tree, and orchestration. New feature logic should move out once it grows beyond simple wiring.

## Adding A Feature

1. Put shared data types, parsing, persistence, status text, and tests in `oddsnap-core`.
2. Add or reuse a trait in `oddsnap-platform` when the feature touches OS services.
3. Implement the trait per OS crate. Prefer native APIs first, then command/runtime fallbacks with clear errors.
4. Add a small app module when the feature has workflow state or command execution.
5. Keep GPUI code thin: controls call app methods, app methods call core/platform/workflow modules.
6. Update `docs/rust-rewrite-todo.md` and `docs/rust-rewrite-parity.md` with exact parity state.
7. Verify the smallest relevant tests plus `cargo check -p oddsnap-app`.

## Current Risk

`main.rs` is still too large. It is acceptable as the composition root, but new feature-specific workflow code should not stay there. If a feature needs retries, subprocesses, parsing, indexing, or multi-step state, extract it into an app module or core/platform module before adding more UI around it.
