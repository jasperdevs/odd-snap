<p align="center">
  <img src="assets/banner.svg" alt="Yoink" width="100%"/>
</p>

<p align="center">
  <strong>A fast, lightweight screenshot tool for Windows.</strong><br>
  Capture any region, annotate it, and share — all from the system tray.
</p>

<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases">Download</a>
</p>

---

## What is Yoink?

Yoink is a screenshot tool that sits in your system tray and lets you capture, annotate, and copy screenshots instantly. Think Snipping Tool meets ShareX, but lighter.

Press a hotkey, select a region (or let it auto-detect the window), draw on it, and it's on your clipboard. Done.

## Features

**Screenshot capture**
- Region select with automatic window detection
- Freeform lasso selection
- Fullscreen capture
- OCR text extraction from screen
- Color picker with hex copy

**Annotation tools**
- Freehand draw
- Straight lines and arrows (straight + curved bezier)
- Text with font picker
- Highlight marker
- Step numbers (auto-incrementing circles)
- Blur and pixelate regions
- Smart eraser
- Magnifier zoom
- Color emoji stamps with search
- Color palette

**After capture**
- Auto-copies to clipboard
- Floating preview you can drag-and-drop into any app
- Saves to Pictures/Yoink
- Capture history with thumbnails, OCR text history, color history
- Customizable hotkeys
- Start with Windows

## Hotkeys

| Action | Default |
|--------|---------|
| Screenshot | `Alt + \`` |
| OCR Text Capture | `Alt + Shift + \`` |
| Color Picker | `Alt + C` |

All hotkeys can be changed in settings.

## Get it

Head to [Releases](https://github.com/jasperdevs/yoink/releases) and grab the latest version. Extract and run. It lives in your system tray.

Needs [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) on Windows 10 or 11.

## Build from source

```
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained false -o publish
```

## License

MIT
