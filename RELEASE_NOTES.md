# Yoink v0.8.3.2

## Added
- Bundled native semantic image search runtime with local CLIP ONNX assets
- Open-source local translation engine option alongside Argos and Google Translate
- Background runtime/model installs that keep running after Settings closes
- Completion/failure toasts for runtime installs
- Colors History search
- Capture dock position setting: top, bottom, left, right
- `Preview only` after-capture mode
- App diagnostics logging for startup, runtime setup, and background jobs

## Changed
- Unified the app onto one toast system
- Rebuilt image/sticker/inline preview toasts on the same shell and animation path
- Recreated QR/barcode previews instead of showing screenshot crops
- QR/barcode toasts now use clearer labels
- Image preview toast sizing is more consistent across captures
- History image search uses bundled local runtime assets instead of first-run model download
- History now uses SQLite as the live search/OCR source of truth
- Runtime install state is persisted instead of being tied to the Settings window
- Settings layout was cleaned up across Capture, OCR, History, Search, and About
- OCR and Colors search bars now match the Images search bar style
- Capture dock, flyouts, and pickers now adapt to dock side
- History thumbnails warm in the background and survive window reopen
- Screenshot history no longer writes disk thumbnails
- Video thumbnails now use one central cache folder
- Duplicate screenshot files between `Yoink` and `Yoink History` were removed for new captures

## Fixed
- History tray open freeze/crash path
- History not refreshing until Settings reopened
- History cards getting stuck on placeholders until a later screenshot
- History loading the whole image library at once on open
- History search showing unrelated matches too easily
- History search/search-result rerenders causing extra UI churn
- OCR card state now distinguishes `OCR ready`, `No text`, and real failures
- OCR state refresh now updates cards without needing an active search query
- Stale legacy OCR/search state no longer poisons the current index
- Missing files are pruned from search state instead of lingering as failures
- Windows OCR is serialized to avoid concurrent-recognition issues
- Open-source local translation install now repairs incomplete runtimes correctly
- OCR runtime install state no longer flashes `Not installed` on open
- OCR runtime checks are faster
- Translation loading UI now shows proper progress/shimmer states
- `Copy OCR` action was added to the OCR window
- Color picker magnifier now works even when pixel magnifier is off
- `Window detection: Off` now fully disables live window snapping
- Freeform selection preview no longer disappears or repaint-lag as badly
- Capture overlay startup is more consistent on first open
- Save dialogs no longer leave overlay/magnifier UI hanging on screen
- Video previews no longer grab the overlay-tainted first frame
- Toast placement now uses the correct monitor work area
- Toast replacement no longer stacks multiple windows on top of each other
- Toast hover/dismiss state no longer gets stuck
- Toast fade/slide dismiss paths now run through the same animation lifecycle
- Toast preview shell no longer leaves layout gaps around the progress strip/frame
- Bottom/top and left/right dock flyouts now animate on the correct axis
- `More tools` flyout clipping and shadow issues were fixed
- Capture toolbar icon alignment and dock/flyout polish were tightened
- Startup/runtime maintenance failures are logged instead of silently swallowed
