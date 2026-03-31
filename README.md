<p align="center">
  <img src="assets/banner-minimal.svg" alt="Yoink" width="95%"/>
</p>

<p align="center">
  <strong>Yoink: All-in-one open-source fast, clean ShareX alternative</strong>
</p>

<p align="center">
  Capture, annotate, OCR, drag out, save locally, and move on.
</p>

<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases/latest">
    <img src="https://img.shields.io/github/v/release/jasperdevs/yoink?style=flat-square&color=1962F4" alt="Release" />
  </a>
  <a href="https://github.com/jasperdevs/yoink/releases">
    <img src="https://img.shields.io/github/downloads/jasperdevs/yoink/latest/total?style=flat-square&cacheSeconds=300" alt="Downloads" />
  </a>
  <a href="https://github.com/jasperdevs/yoink/stargazers">
  <img src="https://img.shields.io/github/stars/jasperdevs/yoink?style=flat-square" alt="Stars" />
</a>
  <a href="https://github.com/jasperdevs/yoink/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/jasperdevs/yoink?style=flat-square" alt="License" />
  </a>
</p>


<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases/latest">
    <img src="https://img.shields.io/badge/windows-download-1962F4?style=for-the-badge&logo=windows&logoColor=white" alt="Download for Windows" />
  </a>
  <img src="https://img.shields.io/badge/macos-planned-6b7280?style=for-the-badge&logo=apple&logoColor=white" alt="macOS Planned" />
  <img src="https://img.shields.io/badge/linux-planned-6b7280?style=for-the-badge&logo=linux&logoColor=white" alt="Linux Planned" />
</p>

<p align="center">
<img width="947" height="490" alt="image" src="assets/banner3333.png" />
</p>

Yoink is a free, open-source screenshot tool that stays out of the way until you need it. Capture part of the screen, mark it up, copy it, save it, drag it out, or upload it without breaking your flow.

## Download

Grab the latest release from the [**Releases page**](https://github.com/jasperdevs/yoink/releases/latest)

## Why Yoink

- Fast region, fullscreen, and active-window capture with window snapping and a tray-first workflow
- Built-in annotation tools for quick explanations and feedback
- OCR, color picking, QR/barcode scanning, stickers, and local GIF recording
- Drag-and-drop preview plus local history that stays easy to browse
- Optional screenshot uploads to public hosts, cloud storage, or self-hosted targets
- More to come (and more i didnt mention lol)

## Stickers

Yoink can turn captures into stickers by removing the background, then saving, previewing, copying, and uploading them like normal images.

<p align="center">
  <img src="assets/sticker-showcase.png" alt="Before and after sticker example" width="92%" />
</p>

- Cloud sticker providers: `remove.bg`, `Photoroom`
- Local sticker models: `U2Netp`, `BRIA RMBG`
- Optional sticker finishing: drop shadow and white stroke

## Default hotkeys

| Action | Hotkey |
|---|---|
| Screenshot | `Alt + `` ` |
| OCR | `Alt + Shift + `` ` |
| Color picker | `Alt + C` |
| QR/barcode scanner | `N/A` |
| Sticker | `N/A` |
| Fullscreen capture | `N/A` |
| Active window capture | `N/A` |
| Ruler | `N/A` |
| GIF recording | `N/A` |

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

Sticker uploads use the same upload destinations as normal image uploads.

## License

[MIT](LICENSE)

## Star History

<a href="https://www.star-history.com/?repos=jasperdevs%2Fyoink&type=timeline&legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&theme=dark&legend=top-left&v=1" />
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left&v=1" />
    <img alt="Star History Chart" src="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left&v=1" />
  </picture>
</a>
