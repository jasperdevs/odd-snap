use regex::Regex;
use serde::Deserialize;

pub const LATEST_RELEASE_API_URL: &str =
    "https://api.github.com/repos/jasperdevs/odd-snap/releases/latest";
pub const RELEASES_PAGE_URL: &str = "https://github.com/jasperdevs/odd-snap/releases/latest";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UpdatePlatform {
    WindowsX64,
    WindowsArm64,
    MacosUniversal,
    MacosAarch64,
    MacosX64,
    LinuxX64,
    LinuxArm64,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ReleaseVersion {
    pub major: u64,
    pub minor: u64,
    pub patch: u64,
    pub revision: u64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UpdateAsset {
    pub name: String,
    pub download_url: String,
    pub sha256: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UpdateCheckSummary {
    pub current_version: ReleaseVersion,
    pub latest_version: ReleaseVersion,
    pub latest_label: String,
    pub release_url: String,
    pub asset: Option<UpdateAsset>,
    pub published_at: Option<String>,
    pub is_update_available: bool,
    pub status_message: String,
}

#[derive(Debug, thiserror::Error)]
pub enum UpdateCheckParseError {
    #[error("failed to parse GitHub release JSON: {0}")]
    Json(#[from] serde_json::Error),
}

#[derive(Debug, Deserialize)]
struct GithubRelease {
    #[serde(default)]
    tag_name: String,
    #[serde(default)]
    html_url: String,
    #[serde(default)]
    published_at: Option<String>,
    #[serde(default)]
    assets: Vec<GithubAsset>,
}

#[derive(Debug, Deserialize)]
struct GithubAsset {
    #[serde(default)]
    name: String,
    #[serde(default)]
    browser_download_url: String,
    #[serde(default)]
    digest: Option<String>,
}

pub fn parse_release_version(tag_name: &str) -> ReleaseVersion {
    let raw = tag_name.trim().trim_start_matches(['v', 'V']);
    let version_pattern = Regex::new(r"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?")
        .expect("version regex should compile");
    let Some(captures) = version_pattern.captures(raw) else {
        return ReleaseVersion::default();
    };

    ReleaseVersion {
        major: captures
            .get(1)
            .and_then(|part| part.as_str().parse().ok())
            .unwrap_or_default(),
        minor: captures
            .get(2)
            .and_then(|part| part.as_str().parse().ok())
            .unwrap_or_default(),
        patch: captures
            .get(3)
            .and_then(|part| part.as_str().parse().ok())
            .unwrap_or_default(),
        revision: captures
            .get(4)
            .and_then(|part| part.as_str().parse().ok())
            .unwrap_or_default(),
    }
}

pub fn build_update_check_summary(
    release_json: &str,
    current_version: &str,
    platform: UpdatePlatform,
) -> Result<UpdateCheckSummary, UpdateCheckParseError> {
    let release = serde_json::from_str::<GithubRelease>(release_json)?;
    let current_version = parse_release_version(current_version);
    let latest_version = parse_release_version(&release.tag_name);
    let latest_label = if release.tag_name.trim().is_empty() {
        latest_version.label()
    } else {
        release.tag_name.trim().to_string()
    };
    let release_url = if release.html_url.trim().is_empty() {
        RELEASES_PAGE_URL.to_string()
    } else {
        release.html_url.trim().to_string()
    };
    let asset = pick_best_update_asset(&release.assets, platform);
    let is_update_available = latest_version > current_version;
    let status_message = if is_update_available {
        format!(
            "Update available: {latest_label} (current {})",
            current_version.label()
        )
    } else {
        format!("You're up to date on {}", current_version.label())
    };

    Ok(UpdateCheckSummary {
        current_version,
        latest_version,
        latest_label,
        release_url,
        asset,
        published_at: release.published_at,
        is_update_available,
        status_message,
    })
}

impl PartialOrd for ReleaseVersion {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for ReleaseVersion {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        (self.major, self.minor, self.patch, self.revision).cmp(&(
            other.major,
            other.minor,
            other.patch,
            other.revision,
        ))
    }
}

impl ReleaseVersion {
    pub fn label(&self) -> String {
        if self.revision > 0 {
            format!(
                "v{}.{}.{}.{}",
                self.major, self.minor, self.patch, self.revision
            )
        } else {
            format!("v{}.{}.{}", self.major, self.minor, self.patch)
        }
    }
}

fn pick_best_update_asset(assets: &[GithubAsset], platform: UpdatePlatform) -> Option<UpdateAsset> {
    preferred_asset(assets, platform)
        .or_else(|| fallback_asset(assets, platform))
        .filter(|asset| !asset.browser_download_url.trim().is_empty())
        .map(|asset| UpdateAsset {
            name: asset.name.trim().to_string(),
            download_url: asset.browser_download_url.trim().to_string(),
            sha256: extract_sha256_hex(asset.digest.as_deref()),
        })
}

fn preferred_asset(assets: &[GithubAsset], platform: UpdatePlatform) -> Option<&GithubAsset> {
    assets
        .iter()
        .filter(|asset| {
            let name = asset.name.to_ascii_lowercase();
            platform_name_matches(&name, platform) && platform_asset_kind_matches(&name, platform)
        })
        .min_by_key(|asset| platform_asset_rank(&asset.name.to_ascii_lowercase(), platform))
}

fn fallback_asset(assets: &[GithubAsset], platform: UpdatePlatform) -> Option<&GithubAsset> {
    assets
        .iter()
        .find(|asset| platform_asset_kind_matches(&asset.name.to_ascii_lowercase(), platform))
        .or_else(|| {
            assets
                .iter()
                .find(|asset| asset.name.to_ascii_lowercase().ends_with(".zip"))
        })
}

fn platform_asset_kind_matches(name: &str, platform: UpdatePlatform) -> bool {
    match platform {
        UpdatePlatform::WindowsX64 | UpdatePlatform::WindowsArm64 => {
            name.ends_with(".exe")
                || name.ends_with(".msi")
                || name.ends_with(".msix")
                || name.ends_with(".msixbundle")
                || name.ends_with(".appinstaller")
                || name.ends_with(".zip")
        }
        UpdatePlatform::MacosUniversal
        | UpdatePlatform::MacosAarch64
        | UpdatePlatform::MacosX64 => {
            name.ends_with(".dmg")
                || name.ends_with(".pkg")
                || name.ends_with(".app.tar.gz")
                || name.ends_with(".zip")
        }
        UpdatePlatform::LinuxX64 | UpdatePlatform::LinuxArm64 => {
            name.ends_with(".appimage")
                || name.ends_with(".deb")
                || name.ends_with(".rpm")
                || name.ends_with(".tar.gz")
                || name.ends_with(".zip")
        }
    }
}

fn platform_name_matches(name: &str, platform: UpdatePlatform) -> bool {
    match platform {
        UpdatePlatform::WindowsX64 => name.contains("win-x64") || name.contains("windows-x64"),
        UpdatePlatform::WindowsArm64 => {
            name.contains("win-arm64") || name.contains("windows-arm64")
        }
        UpdatePlatform::MacosUniversal => {
            name.contains("macos-universal") || name.contains("darwin-universal")
        }
        UpdatePlatform::MacosAarch64 => {
            name.contains("macos-arm64")
                || name.contains("macos-aarch64")
                || name.contains("darwin-arm64")
                || name.contains("darwin-aarch64")
        }
        UpdatePlatform::MacosX64 => {
            name.contains("macos-x64")
                || name.contains("macos-x86_64")
                || name.contains("darwin-x64")
                || name.contains("darwin-x86_64")
        }
        UpdatePlatform::LinuxX64 => name.contains("linux-x64") || name.contains("linux-x86_64"),
        UpdatePlatform::LinuxArm64 => {
            name.contains("linux-arm64") || name.contains("linux-aarch64")
        }
    }
}

fn platform_asset_rank(name: &str, platform: UpdatePlatform) -> u8 {
    match platform {
        UpdatePlatform::WindowsX64 | UpdatePlatform::WindowsArm64 => {
            if name.ends_with(".exe")
                || name.ends_with(".msi")
                || name.ends_with(".msix")
                || name.ends_with(".msixbundle")
                || name.ends_with(".appinstaller")
            {
                0
            } else {
                1
            }
        }
        UpdatePlatform::MacosUniversal
        | UpdatePlatform::MacosAarch64
        | UpdatePlatform::MacosX64 => {
            if name.ends_with(".dmg") || name.ends_with(".pkg") {
                0
            } else if name.ends_with(".app.tar.gz") {
                1
            } else {
                2
            }
        }
        UpdatePlatform::LinuxX64 | UpdatePlatform::LinuxArm64 => {
            if name.ends_with(".appimage") || name.ends_with(".deb") || name.ends_with(".rpm") {
                0
            } else if name.ends_with(".tar.gz") {
                1
            } else {
                2
            }
        }
    }
}

fn extract_sha256_hex(digest: Option<&str>) -> Option<String> {
    let digest = digest?.trim();
    let hash = digest.strip_prefix("sha256:").or_else(|| {
        digest
            .strip_prefix("SHA256:")
            .or_else(|| digest.strip_prefix("Sha256:"))
    })?;
    let hash = hash.trim();
    (hash.len() == 64 && hash.chars().all(|char| char.is_ascii_hexdigit()))
        .then(|| hash.to_ascii_uppercase())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn release_versions_parse_labels_and_compare_like_legacy_versions() {
        assert_eq!(
            parse_release_version("v1.2.3-beta"),
            ReleaseVersion {
                major: 1,
                minor: 2,
                patch: 3,
                revision: 0,
            }
        );
        assert!(parse_release_version("1.2.4") > parse_release_version("1.2.3.9"));
        assert_eq!(parse_release_version("bad").label(), "v0.0.0");
        assert_eq!(parse_release_version("1.2.3.4").label(), "v1.2.3.4");
    }

    #[test]
    fn update_summary_picks_windows_arch_asset_and_digest() {
        let json = r#"{
            "tag_name": "v2.0.0",
            "html_url": "https://example.test/releases/v2",
            "published_at": "2026-01-02T03:04:05Z",
            "assets": [
                {"name": "OddSnap-win-x64.zip", "browser_download_url": "https://example.test/zip"},
                {"name": "OddSnap-win-x64-setup.exe", "browser_download_url": "https://example.test/exe", "digest": "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd"}
            ]
        }"#;

        let summary = build_update_check_summary(json, "1.0.0", UpdatePlatform::WindowsX64)
            .expect("release JSON should parse");

        assert!(summary.is_update_available);
        assert_eq!(summary.latest_label, "v2.0.0");
        assert_eq!(summary.release_url, "https://example.test/releases/v2");
        assert_eq!(
            summary.asset.as_ref().map(|asset| asset.name.as_str()),
            Some("OddSnap-win-x64-setup.exe")
        );
        assert_eq!(
            summary.asset.and_then(|asset| asset.sha256),
            Some("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD".into())
        );
        assert_eq!(
            summary.status_message,
            "Update available: v2.0.0 (current v1.0.0)"
        );
    }

    #[test]
    fn update_summary_prefers_cross_platform_asset_kinds() {
        let json = r#"{
            "tag_name": "v1.0.0",
            "assets": [
                {"name": "OddSnap-linux-x64.AppImage", "browser_download_url": "https://example.test/appimage"},
                {"name": "OddSnap-macos-universal.dmg", "browser_download_url": "https://example.test/dmg"},
                {"name": "OddSnap-win-arm64.msix", "browser_download_url": "https://example.test/msix"}
            ]
        }"#;

        let mac = build_update_check_summary(json, "1.0.0", UpdatePlatform::MacosUniversal)
            .expect("mac release JSON should parse")
            .asset
            .expect("mac asset");
        assert_eq!(mac.name, "OddSnap-macos-universal.dmg");

        let linux = build_update_check_summary(json, "1.0.0", UpdatePlatform::LinuxX64)
            .expect("linux release JSON should parse")
            .asset
            .expect("linux asset");
        assert_eq!(linux.name, "OddSnap-linux-x64.AppImage");

        let windows_arm = build_update_check_summary(json, "1.0.0", UpdatePlatform::WindowsArm64)
            .expect("windows arm release JSON should parse")
            .asset
            .expect("windows arm asset");
        assert_eq!(windows_arm.name, "OddSnap-win-arm64.msix");
    }
}
