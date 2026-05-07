param(
    [ValidateSet("debug", "release")]
    [string] $Profile = "debug",
    [switch] $SkipBuild,
    [string] $Output = "dist/windows",
    [string] $Version = "0.1.0-local",
    [switch] $Zip
)

$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $PSCommandPath
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..\..")
Push-Location $RepoRoot
try {
    if (-not $SkipBuild) {
        $buildArgs = @("build", "-p", "oddsnap-app", "--bin", "oddsnap-rust")
        if ($Profile -eq "release") {
            $buildArgs += "--release"
        }
        & cargo @buildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "cargo build failed with exit code $LASTEXITCODE"
        }
    }

    $BinaryPath = Join-Path $RepoRoot "target\$Profile\oddsnap-rust.exe"
    if (-not (Test-Path -LiteralPath $BinaryPath)) {
        throw "Rust app binary not found: $BinaryPath"
    }

    $OutputRoot = if ([System.IO.Path]::IsPathRooted($Output)) {
        $Output
    } else {
        Join-Path $RepoRoot $Output
    }
    $PackageRoot = Join-Path $OutputRoot "OddSnap-Rust"
    if (Test-Path -LiteralPath $PackageRoot) {
        Remove-Item -LiteralPath $PackageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null

    $PackagedExe = Join-Path $PackageRoot "OddSnap.exe"
    Copy-Item -LiteralPath $BinaryPath -Destination $PackagedExe -Force

    $Manifest = [ordered]@{
        app = "OddSnap Rust"
        version = $Version
        profile = $Profile
        entrypoint = "OddSnap.exe"
        sourceBinary = (Resolve-Path $BinaryPath).Path
        releaseChannelEnabled = $false
        publicArtifact = $false
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    $ManifestPath = Join-Path $PackageRoot "oddsnap-rust-package.json"
    $Manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

    if ($Zip) {
        $ZipPath = Join-Path $OutputRoot "OddSnap-Rust-$Version-$Profile.zip"
        if (Test-Path -LiteralPath $ZipPath) {
            Remove-Item -LiteralPath $ZipPath -Force
        }
        Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $ZipPath
        Write-Output "Created $ZipPath"
    }

    Write-Output "Packaged $PackageRoot"
} finally {
    Pop-Location
}
