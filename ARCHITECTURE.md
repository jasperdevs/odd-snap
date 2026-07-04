# OddSnap Architecture & Modernization Plan

This document records how the app is put together today, the design-system
rules every UI change must follow, and the staged plan for evolving the shell
toward a fully native Windows 11 experience without destabilizing the working
capture engines.

## 1. Current architecture

```
OddSnap.sln
├── src/OddSnap          WPF + WinForms production app (ships to users)
├── src/OddSnap.AppModel UI-agnostic contracts (net9.0, zero dependencies)
├── src/OddSnap.WinUI    WinUI 3 shell prototype (schema-driven, not shipped)
└── src/OddSnap.Tests    xUnit suite for pure, non-UI logic
```

### src/OddSnap (production app)

- **Coordinator:** `App` is a partial class split across `App/` —
  `App.Startup.cs` (boot, wizard, tray, hotkeys, warmup), `App.Hotkeys.cs`
  (hotkey → capture launch), `App.Capture.cs` / `App.Capture.Handlers.cs`
  (capture orchestration + overlay event wiring), `App.Upload.cs`,
  `App.Lifecycle.cs` (settings window, history init, idle memory trim).
- **Services (`Services/`):** settings persistence (`SettingsService`,
  debounced atomic JSON writes + legacy migration + DPAPI protection of
  secrets), history (`HistoryService` + SQLite `HistoryStore`), search index,
  uploads (24 destinations), OCR/translation/sticker/upscale local runtimes,
  hotkeys, sounds, install/update. Services are where business logic belongs —
  views must not grow capture/OCR/upload logic.
- **Capture engines (`Capture/`):** GDI + DXGI screen capture,
  `RegionOverlayForm` (annotation overlay, ~15 partials), recording
  (ffmpeg/NAudio/AnimatedGif), scrolling capture. These are mature and
  intentionally conservative: **do not rewrite them as part of UI work.**
  Overlays run on a dedicated STA thread (`CaptureOverlayThread`); results
  marshal back via `App.TryPostToAppDispatcher`.
- **UI (`UI/`):** WPF windows (Settings, Toast, Preview, OCR/Upscale results,
  SetupWizard) + WinForms capture surfaces. No MVVM layer today; settings
  logic lives in `UI/Settings/*` partials.

### Threading rules

- WPF UI on the main dispatcher; capture overlays/recording on dedicated STA
  threads; always come back through `TryPostToAppDispatcher`.
- Persistence flushes are debounced on timer threads; never block the UI
  thread on disk or network.

## 2. Design system (the rules)

Single source of truth for color/typography/motion:

| Concern | Source of truth | Notes |
|---|---|---|
| WPF palette | `UI/Theme.cs` | Semantic tokens only (`BgCard`, `TextSecondary`, …). Never hardcode hex in XAML or code-behind; use `DynamicResource` keys published by `Theme.ApplyTo`. |
| WinForms/GDI palette | `Helpers/UiChrome.cs` | Must mirror `Theme` values; when adding a token add it to both (or derive it — see Stage 1). |
| Motion | `UI/Motion.cs` | All durations/easings; respects the reduced-motion setting via `Motion.Disabled`. |
| Window chrome | `UI/OddSnapWindowChrome.cs` | Rounded corners + DWM dark mode + pre-Win11 fallback. Every top-level window goes through it. |
| Spacing/control metrics | `SettingsControlHeight/Padding/FontSize/MinWidth` resources | Reuse; do not invent per-view sizes. |

Hard rules for new/modified UI:

1. **Live theming:** every window subscribes to `Theme.Changed` and re-applies
   brushes; unsubscribe on `Closed`. Light and dark must both be checked.
2. **DPI:** no pixel-snapped absolute layouts; run at 100/125/150/200 %.
   WinForms surfaces scale through `UiScale`.
3. **Localization:** every visible string goes through
   `LocalizationService.ApplyTo`/`Translate`; layouts must survive +40 %
   string growth and RTL.
4. **Keyboard & accessibility:** focus visuals from `OddSnapFocusVisual`,
   `AutomationProperties.Name` on icon-only buttons, full tab order.
5. **Restraint:** monochrome accent, subtle borders, no gradients, no
   decorative animation. Match Windows 11 Settings visual density.

## 3. Testing & safety rules

- `src/OddSnap.Tests` covers pure logic (settings normalization, filename
  templates, localization fallback, history utilities, hotkey formatting,
  upload validation). CI runs it on every push/PR (`build.yml`, `test` job).
- Tests must never: open windows, register global hotkeys, capture the
  screen, touch the network, or write outside the temp directory. The user's
  real settings/history under AppData are off-limits.
- Visual verification of app windows uses `--settings` plus
  `PrintWindow(PW_RENDERFULLCONTENT)` so occluded windows can be captured
  without disturbing the desktop. Never trigger real capture hotkeys during
  automated checks.

## 4. Staged Windows 11 native migration

The WinUI 3 shell (`src/OddSnap.WinUI`) is a schema-driven prototype: it
renders `OddSnap.AppModel.SettingsSchemaCatalog` generically and shows
placeholder job snapshots. A big-bang migration is off the table — the WPF
app is stable and ships. The staged path:

- **Stage 0 — stop the rot (done):** build the whole solution (including
  WinUI + AppModel + tests) in CI so the prototype can no longer silently
  break.
- **Stage 1 — one palette:** derive `UiChrome` colors from `Theme` so the
  WPF and GDI palettes cannot drift (done — `UiChrome` converts `Theme`
  tokens instead of duplicating hex values).
- **Stage 2 — extract view-independent state:** move settings-page state and
  validation out of `UI/Settings/*` code-behind into plain classes in
  `OddSnap.AppModel` (grow `SettingsSchemaCatalog` binding paths into real
  binding). Cover with tests as it moves.
- **Stage 3 — bridge real data into the WinUI shell:** feed
  `BackgroundRuntimeJobService.GetSnapshots()` and live settings values into
  `OddSnap.WinUI` via a thin IPC or shared-process host, replacing the
  hardcoded placeholders. The shell becomes a real (optional) settings
  front-end.
- **Stage 4 — window-by-window swap:** replace WPF windows with WinUI
  equivalents one at a time (Settings first, then result windows, toasts
  last), keeping the WinForms capture overlays untouched — they are
  framework-agnostic and already native-feeling.
- **Stage 5 — retire the WPF shell** once every window has a WinUI
  equivalent and a release has soaked.

Non-goals at every stage: rewriting `RegionOverlayForm`, the recorders, DXGI
capture, or the upload engine for UI reasons; introducing web-based UI.

## 5. Website

`site/` (React 19 + Vite + Tailwind v4) builds into `docs/`, which is the
committed GitHub Pages artifact — after editing `site/src`, run
`npm run build` in `site/` and commit the regenerated `docs/`. CI type-checks
and builds the site on every push (`build.yml`, `site` job) but does not
deploy; deployment remains the committed `docs/` folder.
