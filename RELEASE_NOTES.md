# Yoink v0.8.2

## Changed
- OCR now uses Windows built-in text recognition — no downloads, no setup, works instantly
- OCR languages come from your Windows language packs — add more in Windows Settings
- Removed Tesseract dependency and all native DLL bundling for a much smaller app
- File name patterns: choose how screenshots are named in Settings → Capture → Saving
- Tray menu shows "PrtSc" instead of "Snapshot" for PrintScreen hotkeys

## Fixed
- Hotkey registration no longer falsely reports conflicts from stale registrations
- Hotkey conflict error now shows the exact key combo and what's blocking it
- Annotation tool hotkeys no longer conflict with capture tool hotkeys (different contexts)
- Improved hotkey conflict detection message with actionable details
