# OddSnap v0.8.39

## Changed
- lower idle RAM use by trimming OCR engines, DXGI capture resources, icon caches, and history thumbnail caches.
- reduce GIF/video recording memory spikes with smaller queues and preview frames.
- make automatic image indexing lighter by downscaling large screenshots for fast OCR.
- cut search/cache allocations in history and image indexing paths.
