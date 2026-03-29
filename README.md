<p align="center">
  <img src="assets/banner-minimal.svg" alt="Yoink" width="100%"/>
</p>

<p align="center">
  <strong>Yoink: All-in-one open-source fast, clean ShareX alternative for Windows</strong>
</p>

<p align="center">
  Capture, annotate, OCR, drag out, save locally, and move on.
</p>

<p align="center">
  <a href="https://github.com/jasperdevs/yoink/releases/latest">
    <img src="https://img.shields.io/github/v/release/jasperdevs/yoink?style=flat-square&color=1962F4" alt="Release" />
  </a>
  <a href="https://github.com/jasperdevs/yoink/releases">
    <img src="https://img.shields.io/github/downloads/jasperdevs/yoink/latest/total?style=flat-square" alt="Downloads" />
  </a>
  <a href="https://github.com/jasperdevs/yoink/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/jasperdevs/yoink?style=flat-square" alt="License" />
  </a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square" alt="Platform" />
</p>

<img width="947" height="490" alt="image" src="https://github.com/user-attachments/assets/9609fa02-b030-46ed-8f20-cf0b116bd9e0" />


Yoink is a free, open-source screenshot tool for Windows that stays out of the way until you need it. Capture part of the screen, mark it up, copy it, save it, drag it out, or upload it without breaking your flow.

## Download

Grab the latest release from the [**Releases page**](https://github.com/jasperdevs/yoink/releases/latest). Extract the zip and run `Yoink.exe`.

## Why Yoink

- Fast region capture with window snapping and a tray-first workflow
- Built-in annotation tools for quick explanations and feedback
- OCR, color picking, QR scanning, and local GIF recording
- Drag-and-drop preview plus local history that stays easy to browse
- Optional screenshot uploads to public hosts, cloud storage, or self-hosted targets
- More to come (and more i didnt mention lol)

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

<a href="https://www.star-history.com/#jasperdevs/yoink&Date">
  <picture>
    <source
      media="(prefers-color-scheme: dark)"
      srcset="https://api.star-history.com/svg?repos=jasperdevs/yoink&type=Date&theme=dark"
    />
    <source
      media="(prefers-color-scheme: light)"
      srcset="https://api.star-history.com/svg?repos=jasperdevs/yoink&type=Date"
    />
    <img
      alt="Star History Chart"
      src="https://api.star-history.com/svg?repos=jasperdevs/yoink&type=Date"
    />
  </picture>
</a>
