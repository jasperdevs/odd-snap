# Winget packaging

This folder contains the manifest generator used by the release workflow.

Release jobs:

- publish the Windows ZIPs
- generate versioned winget manifests from the exact uploaded ZIP hashes
- attach the manifests to the GitHub release

To submit or update OddSnap in the winget community repository, use the generated manifests from the matching release tag.

First submission, exact flow:

1. Create a classic PAT with `public_repo`.
2. Fork `microsoft/winget-pkgs`.
3. Run `scripts/winget/New-WingetManifests.ps1` with the release ZIP URLs and SHA256 values.
4. Validate the output with `winget validate <manifest-folder>`.
5. Submit the folder with `scripts/winget/Submit-WingetSubmission.ps1`.

Official docs:

- https://learn.microsoft.com/en-us/windows/package-manager/package/manifest
- https://learn.microsoft.com/en-us/windows/package-manager/package/repository
- https://github.com/microsoft/winget-pkgs
