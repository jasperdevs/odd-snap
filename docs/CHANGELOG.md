# OddSnap v0.8.46

## Added

- Added **Show OddSnap UI in screenshots**. Disable it to exclude OddSnap windows, previews, notifications, and other OddSnap-owned UI from captures.

## Fixed

- Restricted automatic history retention to tracked OddSnap files so unrelated images are never recursively deleted ([#56](https://github.com/jasperdevs/odd-snap/issues/56)).
- Added an advanced-color compatibility capture path to prevent overexposed HDR screenshots ([#62](https://github.com/jasperdevs/odd-snap/issues/62)).
- Skipped image-preview toasts when desktop composition is unavailable instead of terminating OddSnap ([#64](https://github.com/jasperdevs/odd-snap/issues/64)).
- Normalized Japanese and Chinese OCR spacing while preserving Korean spacing ([#66](https://github.com/jasperdevs/odd-snap/issues/66)).
- Forced UTF-8 input for local translation so non-ASCII text is preserved ([#67](https://github.com/jasperdevs/odd-snap/issues/67)).
- Installed PyTorch with the open-source local translation runtime ([#68](https://github.com/jasperdevs/odd-snap/issues/68)).
- Reconstructed Arabic and Hebrew OCR lines from word geometry while preserving embedded Latin runs ([#69](https://github.com/jasperdevs/odd-snap/issues/69)).
- Made toast fade and slide animations transparent, monitor-local, and reliable.
- Preserved the aspect ratio of narrow image-only notification previews.
- Restored History upload actions after refusal, failure, rate limiting, and successful uploads.
- Reduced History resize stutter by reusing clipping geometry and debouncing viewport rebuilds.
- Centered the OddSnap mark and added contrast-safe application, installer, website, and package icons.
