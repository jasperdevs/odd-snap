param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackageIdentifier = 'JasperDevs.OddSnap',
    [string]$PackageName = 'OddSnap',
    [string]$Publisher = 'JasperDevs',
    [string]$PublisherUrl = 'https://github.com/jasperdevs/odd-snap',
    [string]$PublisherSupportUrl = 'https://github.com/jasperdevs/odd-snap/issues',
    [string]$License = 'GPL-3.0',
    [string]$LicenseUrl = 'https://github.com/jasperdevs/odd-snap/blob/main/LICENSE',
    [string]$Moniker = 'oddsnap',
    [string]$Description = 'Screenshot, annotation, OCR, sticker, and recording tool for Windows',
    [string[]]$Tags = @('screenshot', 'capture', 'annotation', 'ocr', 'recording', 'stickers'),
    [Parameter(Mandatory = $true)]
    [string]$X64InstallerUrl,
    [Parameter(Mandatory = $true)]
    [string]$X64InstallerSha256,
    [Parameter(Mandatory = $true)]
    [string]$X86InstallerUrl,
    [Parameter(Mandatory = $true)]
    [string]$X86InstallerSha256,
    [Parameter(Mandatory = $true)]
    [string]$Arm64InstallerUrl,
    [Parameter(Mandatory = $true)]
    [string]$Arm64InstallerSha256
)

$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Format-YamlList {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Items
    )

    return ($Items | ForEach-Object { "  - $_" }) -join [Environment]::NewLine
}

$versionDir = Join-Path $OutputDirectory $PackageVersion
New-Item -ItemType Directory -Force -Path $versionDir | Out-Null

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.9.0.schema.json
PackageIdentifier: $PackageIdentifier
PackageVersion: $PackageVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.9.0
"@

$defaultLocaleYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.9.0.schema.json
PackageIdentifier: $PackageIdentifier
PackageVersion: $PackageVersion
PackageLocale: en-US
Publisher: $Publisher
PublisherUrl: $PublisherUrl
PublisherSupportUrl: $PublisherSupportUrl
PackageName: $PackageName
License: $License
LicenseUrl: $LicenseUrl
ShortDescription: $Description
Moniker: $Moniker
Tags:
$(Format-YamlList -Items $Tags)
ManifestType: defaultLocale
ManifestVersion: 1.9.0
"@

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.9.0.schema.json
PackageIdentifier: $PackageIdentifier
PackageVersion: $PackageVersion
MinimumOSVersion: 10.0.0.0
InstallerType: exe
Scope: user
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: $X64InstallerUrl
    InstallerSha256: $X64InstallerSha256
  - Architecture: x86
    InstallerUrl: $X86InstallerUrl
    InstallerSha256: $X86InstallerSha256
  - Architecture: arm64
    InstallerUrl: $Arm64InstallerUrl
    InstallerSha256: $Arm64InstallerSha256
ManifestType: installer
ManifestVersion: 1.9.0
"@

Write-Utf8NoBom -Path (Join-Path $versionDir "$PackageIdentifier.yaml") -Content $versionYaml.TrimEnd()
Write-Utf8NoBom -Path (Join-Path $versionDir "$PackageIdentifier.locale.en-US.yaml") -Content $defaultLocaleYaml.TrimEnd()
Write-Utf8NoBom -Path (Join-Path $versionDir "$PackageIdentifier.installer.yaml") -Content $installerYaml.TrimEnd()
