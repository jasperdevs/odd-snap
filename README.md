<p align="center">
  <img src="assets/banner-minimal.svg" alt="Yoink" width="95%"/>
</p>

<p align="center">
  <strong>Yoink: All-in-one open-source ShareX alternative</strong>
</p>

<p align="center">
  Capture, annotate, OCR, translate, make stickers, record video, save locally, search images with OCR, and many more features.
</p>

<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases/latest">
    <img src="https://img.shields.io/github/v/release/jasperdevs/yoink?style=flat-square&color=1962F4" alt="Release" />
  </a>
  <a href="https://github.com/jasperdevs/yoink/releases">
    <img src="https://img.shields.io/github/downloads/jasperdevs/yoink/total?style=flat-square&cacheSeconds=300" alt="Downloads" />
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

## Download

Grab the latest release from the [**Releases page**](https://github.com/jasperdevs/yoink/releases/latest).

## Winget

```powershell
winget install --id JasperDevs.Yoink -e
winget upgrade --id JasperDevs.Yoink -e
```

## Why Yoink

- Region, fullscreen, active-window, and scrolling capture
- Built-in annotation tools (arrows, text, shapes, blur, freehand) with stroke/shadow effects
- OCR text extraction with built-in translation (Argos Translate offline, Google Translate API)
- Auto-download language packs for 100+ OCR languages
- Color picking, QR/barcode scanning, ruler, and step numbering
- Sticker creation with background removal
- Screen recording (GIF, MP4, WebM, MKV) with microphone and desktop audio
- Local history with image search using OCR and semantic matching
- Optional uploads to 15+ services including Imgur, S3, Dropbox, and self-hosted targets

## Stickers

Yoink can turn captures into stickers by removing the background, then saving, previewing, copying, and uploading them like normal images.

<p align="left">
  <img src="assets/sticker-showcase.png" alt="Before and after sticker example" width="92%" />
</p>

- Cloud sticker providers: `remove.bg`, `Photoroom`
- Local sticker models: `U2Netp`, `BRIA RMBG`
- Optional sticker finishing: drop shadow and white stroke

## OCR & Translate

Yoink can extract text from any region of your screen and translate it instantly. OCR results open in a dedicated window where you can edit, copy, or translate the text.

<p align="left">
  <img src="assets/yoink_20260404_235253.png" alt="OCR result window with translation" width="60%" />
</p>

- Extract text from screenshots with Tesseract OCR
- Auto-download language packs for 100+ languages
- Translate with Argos Translate (offline) or Google Translate API
- Dedicated result window with copy and translate buttons

## Search

Search your image history by filename, OCR text, and semantic matching, so you can find screenshots by what they say or by what they show.


<p align="left">
  <img src="assets/image.png" alt="Searching image history with OCR and semantic matching" width="60%" />
</p>


- Search by text inside the image with OCR
- Search by semantic similarity to find visually related screenshots

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

## Uploads

Yoink can upload screenshots, stickers, and recordings after capture. Upload targets include:

- Public hosts like `Imgur`, `ImgBB`, `Catbox`, `Litterbox`, `Gyazo`, `file.io`, and `Uguu`
- Cloud targets like `Dropbox`, `Google Drive`, `OneDrive`, `Azure Blob`, and `S3-compatible storage`
- Self-hosted and developer targets like `GitHub`, `Immich`, `FTP`, `SFTP`, `WebDAV`, and `Custom HTTP`

Availability depends on the target service and your credentials.

Sticker uploads use the same upload destinations as normal image uploads.

## Build from source

```
git clone https://github.com/jasperdevs/yoink.git
cd yoink
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

## Star History

<a href="https://www.star-history.com/?repos=jasperdevs%2Fyoink&type=timeline&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&legend=top-left" />
 </picture>
</a>

<a href="https://www.producthunt.com/products/yoink-5?embed=true&amp;utm_source=badge-featured&amp;utm_medium=badge&amp;utm_campaign=badge-yoink-7" target="_blank" rel="noopener noreferrer"><img alt="Yoink - Free, open-source screenshot tool thats CLEAN | Product Hunt" width="250" height="54" src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1114055&amp;theme=light&amp;t=1775118335168"></a>

