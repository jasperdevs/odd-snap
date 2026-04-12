# Yoink v0.8.15

## Fixed
- Fixed local sticker model downloads across all CPU and GPU models by moving them to isolated Yoink-managed runtimes.
- Fixed local upscale model downloads and runtime setup to use isolated Yoink-managed runtimes instead of the user global Python install.
- Fixed sticker model cache detection so rembg-managed downloads are recognized correctly, including legacy `.u2net` caches.
- Fixed local upscale inference tiling for fixed-size ONNX models so every bundled local model can download and run successfully.
- Added copy-error actions for failed local sticker and upscale runtime/model jobs in Settings.
