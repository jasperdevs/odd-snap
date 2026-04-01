# Contributing

Yoink is Windows-first and intentionally focused. Pull requests should improve the default screenshot workflow, not turn the app into a generic workflow platform.

## Before you open a PR

- Search existing issues before filing a new one
- Keep changes small and scoped
- Update the README when behavior or setup changes
- Do not touch unrelated code or formatting

## Local setup

```bash
dotnet test tests/Yoink.Tests/Yoink.Tests.csproj -c Release
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

## What gets reviewed faster

- Clear bug fixes with reproduction steps
- UI simplifications that reduce clicks or reduce confusion
- Reliability fixes, especially for capture, history, upload, or install flows

## What will usually be pushed back on

- Cross-platform work before the Windows path is stable
- Large feature dumps with no user flow argument
- Copying ShareX behavior without a reasoned difference
- PRs that change many files without a focused explanation

## Testing

If you add logic that can be tested, add a test for it. The repo should not rely on manual clicking for everything.
