#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/macos/package-oddsnap-rust.sh [options]

Build a local macOS OddSnap.app bundle for the Rust rewrite. This script does
not publish, tag, upload, or create a GitHub release.

Options:
  --profile debug|release     Cargo profile to package. Defaults to release.
  --output DIR                Output directory. Defaults to dist/macos.
  --bundle-id ID              Bundle identifier. Defaults to dev.jasper.oddsnap.
  --version VERSION           Bundle short version/build version. Defaults to 0.1.0.
  --skip-build                Reuse the existing target binary.
  --unsigned                  Do not codesign, even if CODESIGN_IDENTITY is set.
  -h, --help                  Show this help.

Optional environment:
  CODESIGN_IDENTITY           Developer ID Application signing identity.
  NOTARY_PROFILE              notarytool keychain profile to submit the zip.
USAGE
}

profile="release"
output_dir="dist/macos"
bundle_id="dev.jasper.oddsnap"
version="0.1.0"
skip_build=0
force_unsigned=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --profile)
      profile="${2:?missing --profile value}"
      shift 2
      ;;
    --output)
      output_dir="${2:?missing --output value}"
      shift 2
      ;;
    --bundle-id)
      bundle_id="${2:?missing --bundle-id value}"
      shift 2
      ;;
    --version)
      version="${2:?missing --version value}"
      shift 2
      ;;
    --skip-build)
      skip_build=1
      shift
      ;;
    --unsigned)
      force_unsigned=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging must run on macOS." >&2
  exit 1
fi

if [[ "$profile" != "debug" && "$profile" != "release" ]]; then
  echo "--profile must be debug or release." >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

if [[ "$skip_build" -eq 0 ]]; then
  if [[ "$profile" == "release" ]]; then
    cargo build -p oddsnap-app --bin oddsnap-rust --release
  else
    cargo build -p oddsnap-app --bin oddsnap-rust
  fi
fi

binary_path="target/${profile}/oddsnap-rust"
if [[ ! -x "$binary_path" ]]; then
  echo "missing built binary: $binary_path" >&2
  exit 1
fi

app_path="${output_dir}/OddSnap.app"
contents_path="${app_path}/Contents"
macos_path="${contents_path}/MacOS"
resources_path="${contents_path}/Resources"
zip_path="${output_dir}/OddSnap-macos-${version}.zip"

rm -rf "$app_path" "$zip_path"
mkdir -p "$macos_path" "$resources_path"

cp "$binary_path" "${macos_path}/oddsnap-rust"
chmod 755 "${macos_path}/oddsnap-rust"

if [[ -f "oddsnap.png" ]]; then
  cp "oddsnap.png" "${resources_path}/oddsnap.png"
fi

sed \
  -e "s#<string>dev.jasper.oddsnap</string>#<string>${bundle_id}</string>#" \
  -e "s#<string>0.1.0</string>#<string>${version}</string>#g" \
  "packaging/macos/Info.plist" > "${contents_path}/Info.plist"

plutil -lint "${contents_path}/Info.plist" "packaging/macos/OddSnap.entitlements" >/dev/null

if [[ "$force_unsigned" -eq 0 && -n "${CODESIGN_IDENTITY:-}" ]]; then
  codesign \
    --force \
    --timestamp \
    --options runtime \
    --entitlements "packaging/macos/OddSnap.entitlements" \
    --sign "$CODESIGN_IDENTITY" \
    "$app_path"
  codesign --verify --deep --strict --verbose=2 "$app_path"
else
  echo "built unsigned bundle; set CODESIGN_IDENTITY or remove --unsigned to sign." >&2
fi

ditto -c -k --keepParent "$app_path" "$zip_path"

if [[ "$force_unsigned" -eq 0 && -n "${NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$zip_path" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$app_path"
  ditto -c -k --keepParent "$app_path" "$zip_path"
else
  echo "skipped notarization; set NOTARY_PROFILE after signing to submit locally." >&2
fi

echo "$app_path"
echo "$zip_path"
