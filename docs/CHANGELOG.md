# Yoink v0.8.22

## Added
- Add Gofile as a no-setup upload host and include it in the free/no-setup host filter.
- Add imgpile as an API-token image host with protected token storage.

## Changed
- Make MP4 the default recording format for new settings.
- Use the free/no-setup host filter as the default Google Lens hosted-image uploader.
- Sort upload providers by recommended, temporary, free, API-key, cloud, custom, and unavailable categories.
- Order free/no-setup uploads through Litterbox, tmpfiles.org, Uguu, Gofile, then file.io.
- Keep file.io last for Lens uploads because file.io links can expire after first download.

## Removed
- Remove unused CLIP model assets from the source tree.

## Fixed
- Update Uguu uploads to the current `/upload` endpoint.
- Exclude unavailable transfer.sh from the Google Lens host picker.
