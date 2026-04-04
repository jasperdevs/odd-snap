<p align="center">
  <img src="assets/banner-minimal.svg" alt="Yoink" width="95%"/>
</p>

<p align="center">
  <strong>Yoink: All-in-one open-source ShareX alternative</strong>
</p>

<p align="center">
  Capture, annotate, OCR, make stickers, record video, save locally, search images with OCR, and many more features.
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

Yoink is a free, open-source screenshot tool for capture, annotation, OCR, stickers, recording, and uploads.

## Download

Grab the latest release from the [**Releases page**](https://github.com/jasperdevs/yoink/releases/latest).

## Winget

```powershell
winget install --id JasperDevs.Yoink -e
winget upgrade --id JasperDevs.Yoink -e
```

## Why Yoink

- Fast region, fullscreen, active-window, and scroll capture with window snapping and a tray-first workflow
- Built-in annotation tools and configurable toolbar hotkeys for quick explanations and feedback
- OCR, color picking, QR/barcode scanning, stickers, and screen recording with GIF/MP4/WebM/MKV output
- Drag-and-drop preview plus local history for images, text, colors, stickers, and recordings
- Optional uploads for screenshots, stickers, and recordings to public hosts, cloud storage, or self-hosted targets

## Stickers

Yoink can turn captures into stickers by removing the background, then saving, previewing, copying, and uploading them like normal images.

<p align="center">
  <img src="assets/sticker-showcase.png" alt="Before and after sticker example" width="92%" />
</p>

- Cloud sticker providers: `remove.bg`, `Photoroom`
- Local sticker models: `U2Netp`, `BRIA RMBG`
- Optional sticker finishing: drop shadow and white stroke

## Search

Yoink can search through your image history using filenames, OCR text, and semantic image matching so you can find old screenshots without manually digging through folders.


<p align="left">
  <img src="assets/image.png" alt="Searching image history with OCR and semantic matching" width="60%" />
</p>


- OCR search can find screenshots by text that appeared inside the image
- Semantic search can surface visually related screenshots even when the exact words are not present
- Search works across your local Yoink image history so old captures stay useful

## Default hotkeys

| Action | Hotkey |
|---|---|
| Screenshot | `Alt + `` ` |
| OCR | `Alt + Shift + `` ` |
| Color picker | `Alt + C` |
| QR/barcode scanner | `Unassigned` |
| Sticker | `Unassigned` |
| Fullscreen capture | `Unassigned` |
| Active window capture | `Unassigned` |
| Scroll capture | `Unassigned` |
| Ruler | `Unassigned` |
| Record | `Unassigned` |
| Annotation tools | `1-9`, `0`, `-`, `=`, `[`, `]`, `\` |

Annotation tool hotkeys can be configured in settings, and hover tooltips reflect the real assigned key.

## Build from source

```
git clone https://github.com/jasperdevs/yoink.git
cd yoink
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

## Uploads

Yoink can upload screenshots, stickers, and recordings after capture. Upload targets include:

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
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left" />
 </picture>
</a>

<a href="https://www.producthunt.com/products/yoink-5?embed=true&amp;utm_source=badge-featured&amp;utm_medium=badge&amp;utm_campaign=badge-yoink-7" target="_blank" rel="noopener noreferrer"><img alt="Yoink - Free, open-source screenshot tool thats CLEAN | Product Hunt" width="250" height="54" src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1114055&amp;theme=light&amp;t=1775118335168"></a>

