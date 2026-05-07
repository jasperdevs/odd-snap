#!/usr/bin/env bash
set -euo pipefail

profile="debug"
skip_build=0
output="dist/linux"
version="0.1.0-local"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --profile)
      profile="${2:?missing profile}"
      shift 2
      ;;
    --skip-build)
      skip_build=1
      shift
      ;;
    --output)
      output="${2:?missing output}"
      shift 2
      ;;
    --version)
      version="${2:?missing version}"
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ "$profile" != "debug" && "$profile" != "release" ]]; then
  echo "profile must be debug or release" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
cd "$repo_root"

if [[ "$skip_build" -eq 0 ]]; then
  build_args=(build -p oddsnap-app --bin oddsnap-rust)
  if [[ "$profile" == "release" ]]; then
    build_args+=(--release)
  fi
  cargo "${build_args[@]}"
fi

binary_path="$repo_root/target/$profile/oddsnap-rust"
if [[ ! -x "$binary_path" ]]; then
  echo "Rust app binary not found or not executable: $binary_path" >&2
  exit 1
fi

if [[ "$output" = /* ]]; then
  output_root="$output"
else
  output_root="$repo_root/$output"
fi

app_dir="$output_root/OddSnap-Rust.AppDir"
rm -rf "$app_dir"
mkdir -p "$app_dir/usr/bin" "$app_dir/usr/share/applications" "$app_dir/usr/share/metainfo"

cp "$binary_path" "$app_dir/usr/bin/oddsnap-rust"
chmod 755 "$app_dir/usr/bin/oddsnap-rust"

cat > "$app_dir/usr/share/applications/dev.jasper.OddSnapRust.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=OddSnap Rust
Exec=oddsnap-rust
Terminal=false
Categories=Graphics;Utility;
DESKTOP

cat > "$app_dir/usr/share/metainfo/dev.jasper.OddSnapRust.metainfo.xml" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>dev.jasper.OddSnapRust</id>
  <name>OddSnap Rust</name>
  <summary>Local Rust rewrite package smoke for OddSnap</summary>
  <metadata_license>CC0-1.0</metadata_license>
  <project_license>GPL-3.0-or-later</project_license>
  <releases>
    <release version="$version"/>
  </releases>
</component>
EOF

cat > "$app_dir/oddsnap-rust-package.json" <<EOF
{
  "app": "OddSnap Rust",
  "version": "$version",
  "profile": "$profile",
  "entrypoint": "usr/bin/oddsnap-rust",
  "sourceBinary": "$binary_path",
  "releaseChannelEnabled": false,
  "publicArtifact": false
}
EOF

echo "Packaged $app_dir"
