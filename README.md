<p align="center">
  <img src="assets/banner-minimal.svg" alt="Yoink" width="100%"/>
</p>

<p align="center">
  <strong>All-in-one open-source screenshot tool for Windows.</strong>
</p>

<p align="center">
  Capture, annotate, OCR, drag out, save locally, and move on.
</p>

<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases/latest"><img src="https://img.shields.io/github/v/release/jasperdevs/yoink?style=flat-square&color=1962F4" alt="Release"/></a>
  <a href="https://github.com/jasperdevs/yoink/releases/latest"><img src="https://img.shields.io/github/downloads/jasperdevs/yoink/total?style=flat-square" alt="Downloads"/></a>
  <a href="https://github.com/jasperdevs/yoink/blob/main/LICENSE"><img src="https://img.shields.io/github/license/jasperdevs/yoink?style=flat-square" alt="License"/></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square" alt="Platform"/>
</p>

Yoink is a free, open-source screenshot tool for Windows that stays out of the way until you need it. Capture part of the screen, mark it up, copy it, save it, drag it out, or upload it without breaking your flow.

## Download

Grab the latest release from the [**Releases page**](https://github.com/jasperdevs/yoink/releases/latest). Extract the zip and run `Yoink.exe`.

## Why Yoink

- Fast region capture with window snapping and a tray-first workflow
- Built-in annotation tools for quick explanations and feedback
- OCR, color picking, QR scanning, and local GIF recording
- Drag-and-drop preview plus local history that stays easy to browse
- Optional screenshot uploads to public hosts, cloud storage, or self-hosted targets

## Features

- **Capture modes** — region capture with window detection, freeform lasso, fullscreen, OCR text extraction, color picker, QR/barcode scanning, and GIF recording

- **Annotation tools** — draw, lines, arrows, curved arrows, text with inline formatting, highlight, rectangle and circle shapes, step numbers, blur, eraser, magnifier, emoji stamps, and system font search

- **Workflow** — auto-copy to clipboard, floating drag-and-drop preview, local history, quick save, customizable toolbar and hotkeys, and tray-first behavior

- **Uploads** — optional upload support for screenshots with public hosts, cloud storage, and self-hosted targets

## Default hotkeys

| Action | Hotkey |
|---|---|
| Screenshot | `Alt + `` ` |
| OCR | `Alt + Shift + `` ` |
| Color picker | `Alt + C` |

Hotkeys can be changed in settings.

## Build from source

```
git clone https://github.com/jasperdevs/yoink.git
cd yoink
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

## Uploads

Yoink can upload screenshots after capture. Upload targets include:

- Public hosts like `Imgur`, `ImgBB`, `Catbox`, `Litterbox`, `Gyazo`, `file.io`, and `Uguu`
- Cloud targets like `Dropbox`, `Google Drive`, `OneDrive`, `Azure Blob`, and `S3-compatible storage`
- Self-hosted and developer targets like `GitHub`, `Immich`, `FTP`, `SFTP`, `WebDAV`, and `Custom HTTP`

Availability depends on the target service and your credentials.

## License

[MIT](LICENSE)
