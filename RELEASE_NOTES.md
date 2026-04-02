# Yoink v0.5.2.1

## Highlights
- Optimized the hotkey-to-overlay path so capture starts faster with less dispatcher overhead.
- Reused DXGI capture resources and tightened capture-path allocation churn for better steady-state performance.
- Freed recording selection memory earlier so screenshots and recordings keep less data resident.
- Kept the existing UI, features, and release packaging behavior intact.

## Notes
- ZIP assets remain the format used for winget and portable installs.
- The direct EXE assets are included for users who want a simple download from the GitHub release page.
