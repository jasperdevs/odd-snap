# Contributing to Yoink

Thanks for your interest in contributing! Here's how to get started.

## Getting started

1. Fork the repository
2. Clone your fork and create a new branch
3. Make your changes
4. Run `dotnet test Yoink.sln -c Release` to verify tests pass
5. Open a pull request against `main`

## Build from source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```
dotnet publish src/Yoink/Yoink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Reporting bugs

Use the [bug report template](https://github.com/jasperdevs/yoink/issues/new?template=bug_report.yml) and include steps to reproduce.

## Suggesting features

Use the [feature request template](https://github.com/jasperdevs/yoink/issues/new?template=feature_request.yml).

## Code style

- Follow existing conventions in the codebase
- Keep pull requests focused on a single change
- Include a clear description in your PR
