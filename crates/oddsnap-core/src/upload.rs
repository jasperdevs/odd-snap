use std::{fmt, path::Path};

use serde::{de, Deserialize, Deserializer, Serialize};
use serde_json::Value;

use crate::{AppSettings, HistoryKind};

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum UploadDestination {
    None,
    Imgur,
    ImgBb,
    Catbox,
    Litterbox,
    Gyazo,
    FileIo,
    Uguu,
    TransferSh,
    Dropbox,
    GoogleDrive,
    OneDrive,
    AzureBlob,
    GitHub,
    Immich,
    Ftp,
    Sftp,
    WebDav,
    S3Compatible,
    CustomHttp,
    AiChat,
    TempHosts,
    TmpFiles,
    Gofile,
    ImgPile,
    Unknown(String),
}

impl UploadDestination {
    pub fn from_legacy_name(value: &str) -> Self {
        let trimmed = value.trim();
        match normalized_key(trimmed).as_str() {
            "" | "none" => Self::None,
            "imgur" => Self::Imgur,
            "imgbb" => Self::ImgBb,
            "catbox" => Self::Catbox,
            "litterbox" => Self::Litterbox,
            "gyazo" => Self::Gyazo,
            "fileio" => Self::FileIo,
            "uguu" => Self::Uguu,
            "transfersh" => Self::TransferSh,
            "dropbox" => Self::Dropbox,
            "googledrive" => Self::GoogleDrive,
            "onedrive" => Self::OneDrive,
            "azureblob" => Self::AzureBlob,
            "github" => Self::GitHub,
            "immich" => Self::Immich,
            "ftp" => Self::Ftp,
            "sftp" => Self::Sftp,
            "webdav" => Self::WebDav,
            "s3compatible" | "s3" => Self::S3Compatible,
            "customhttp" | "custom" => Self::CustomHttp,
            "aichat" | "airedirect" | "airedirects" => Self::AiChat,
            "temphosts" => Self::TempHosts,
            "tmpfiles" | "tmpfilesorg" => Self::TmpFiles,
            "gofile" => Self::Gofile,
            "imgpile" => Self::ImgPile,
            _ => Self::Unknown(trimmed.into()),
        }
    }

    pub fn display_name(&self) -> String {
        match self {
            Self::None => String::new(),
            Self::Imgur => "Imgur".into(),
            Self::ImgBb => "ImgBB".into(),
            Self::Catbox => "Catbox".into(),
            Self::Litterbox => "Litterbox".into(),
            Self::Gyazo => "Gyazo".into(),
            Self::FileIo => "file.io".into(),
            Self::Uguu => "Uguu".into(),
            Self::TransferSh => "transfer.sh".into(),
            Self::Dropbox => "Dropbox".into(),
            Self::GoogleDrive => "Google Drive".into(),
            Self::OneDrive => "OneDrive".into(),
            Self::AzureBlob => "Azure Blob".into(),
            Self::GitHub => "GitHub".into(),
            Self::Immich => "Immich".into(),
            Self::Ftp => "FTP".into(),
            Self::Sftp => "SFTP".into(),
            Self::WebDav => "WebDAV".into(),
            Self::S3Compatible => "S3".into(),
            Self::CustomHttp => "Custom".into(),
            Self::AiChat => "AI Redirects".into(),
            Self::TempHosts => "Filter between free/no-setup hosts".into(),
            Self::TmpFiles => "tmpfiles.org".into(),
            Self::Gofile => "Gofile".into(),
            Self::ImgPile => "imgpile".into(),
            Self::Unknown(value) => value.clone(),
        }
    }

    pub fn max_size_bytes(&self, file_path: &Path) -> u64 {
        let is_gif = file_path
            .extension()
            .and_then(|extension| extension.to_str())
            .is_some_and(|extension| extension.eq_ignore_ascii_case("gif"));

        match self {
            Self::Imgur if is_gif => 200 * MIB,
            Self::Imgur => 20 * MIB,
            Self::ImgBb => 32 * MIB,
            Self::Catbox => 200 * MIB,
            Self::Litterbox => 1024 * MIB,
            Self::Gyazo => 25 * MIB,
            Self::FileIo => 100 * MIB,
            Self::Uguu => 128 * MIB,
            Self::TransferSh => 10 * GIB,
            Self::Dropbox => 150 * MIB,
            Self::GoogleDrive => 5 * GIB,
            Self::OneDrive => 250 * MIB,
            Self::AzureBlob => 5 * GIB,
            Self::GitHub => 100 * MIB,
            Self::Immich => 5 * GIB,
            Self::Ftp | Self::Sftp | Self::WebDav | Self::S3Compatible => 5 * GIB,
            Self::TempHosts | Self::TmpFiles | Self::ImgPile => 100 * MIB,
            Self::AiChat | Self::Gofile | Self::CustomHttp | Self::None | Self::Unknown(_) => {
                u64::MAX
            }
        }
    }

    pub fn configuration_error(&self, settings: &UploadSettings) -> Option<String> {
        match self {
            Self::None => Some(missing_upload_setting("upload destination")),
            Self::Imgur if settings.imgur_client_id.trim().is_empty() => {
                Some(missing_upload_setting("Imgur client ID"))
            }
            Self::ImgBb if settings.imgbb_api_key.trim().is_empty() => {
                Some(missing_upload_setting("ImgBB API key"))
            }
            Self::ImgPile if settings.imgpile_api_token.trim().is_empty() => {
                Some(missing_upload_setting("imgpile API token"))
            }
            Self::Gyazo if settings.gyazo_access_token.trim().is_empty() => {
                Some(missing_upload_setting("Gyazo access token"))
            }
            Self::Dropbox if settings.dropbox_access_token.trim().is_empty() => {
                Some(missing_upload_setting("Dropbox access token"))
            }
            Self::GoogleDrive if settings.google_drive_access_token.trim().is_empty() => {
                Some(missing_upload_setting("Google Drive access token"))
            }
            Self::OneDrive if settings.one_drive_access_token.trim().is_empty() => {
                Some(missing_upload_setting("OneDrive access token"))
            }
            Self::AzureBlob if settings.azure_blob_sas_url.trim().is_empty() => {
                Some(missing_upload_setting("Azure Blob SAS URL"))
            }
            Self::GitHub if settings.github_token.trim().is_empty() => {
                Some(missing_upload_setting("GitHub token"))
            }
            Self::GitHub if settings.github_repo.trim().is_empty() => {
                Some(missing_upload_setting("GitHub repo"))
            }
            Self::Immich if settings.immich_base_url.trim().is_empty() => {
                Some(missing_upload_setting("Immich base URL"))
            }
            Self::Immich if settings.immich_api_key.trim().is_empty() => {
                Some(missing_upload_setting("Immich API key"))
            }
            Self::Ftp if settings.ftp_url.trim().is_empty() => {
                Some(missing_upload_setting("FTP URL"))
            }
            Self::Ftp if settings.ftp_username.trim().is_empty() => {
                Some(missing_upload_setting("FTP username"))
            }
            Self::Sftp if settings.sftp_host.trim().is_empty() => {
                Some(missing_upload_setting("SFTP host"))
            }
            Self::Sftp if settings.sftp_username.trim().is_empty() => {
                Some(missing_upload_setting("SFTP username"))
            }
            Self::Sftp if settings.sftp_host_key_fingerprint.trim().is_empty() => {
                Some(missing_upload_setting("SFTP host key fingerprint"))
            }
            Self::WebDav if settings.web_dav_url.trim().is_empty() => {
                Some(missing_upload_setting("WebDAV URL"))
            }
            Self::WebDav if settings.web_dav_username.trim().is_empty() => {
                Some(missing_upload_setting("WebDAV username"))
            }
            Self::S3Compatible if settings.s3_endpoint.trim().is_empty() => {
                Some(missing_upload_setting("S3 endpoint"))
            }
            Self::S3Compatible if settings.s3_bucket.trim().is_empty() => {
                Some(missing_upload_setting("S3 bucket"))
            }
            Self::S3Compatible if settings.s3_access_key.trim().is_empty() => {
                Some(missing_upload_setting("S3 access key"))
            }
            Self::S3Compatible if settings.s3_secret_key.trim().is_empty() => {
                Some(missing_upload_setting("S3 secret key"))
            }
            Self::CustomHttp if settings.custom_upload_url.trim().is_empty() => {
                Some(missing_upload_setting("Custom upload URL"))
            }
            Self::WebDav if !is_https_url(&settings.web_dav_url) => {
                Some("WebDAV uploads require an HTTPS URL.".into())
            }
            Self::S3Compatible
                if settings.s3_endpoint.contains("://")
                    && !is_https_url(&settings.s3_endpoint) =>
            {
                Some("S3 uploads require an HTTPS endpoint.".into())
            }
            Self::Unknown(value) => Some(format!(
                "Unknown upload destination '{value}'. Choose a supported destination in Settings -> Uploads."
            )),
            _ => None,
        }
    }
}

impl fmt::Display for UploadDestination {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.display_name())
    }
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum AiChatProvider {
    #[serde(alias = "None")]
    None,
    #[serde(alias = "ChatGpt")]
    ChatGpt,
    #[serde(alias = "Claude")]
    Claude,
    #[serde(alias = "ClaudeOpus")]
    ClaudeOpus,
    #[serde(alias = "Gemini")]
    Gemini,
    #[serde(alias = "GoogleLens")]
    #[default]
    GoogleLens,
}

impl AiChatProvider {
    pub fn display_name(self) -> &'static str {
        match self {
            Self::None => "None",
            Self::ChatGpt => "ChatGPT",
            Self::Claude | Self::ClaudeOpus => "Claude",
            Self::Gemini => "Gemini",
            Self::GoogleLens => "Google Lens",
        }
    }

    pub fn start_url(self) -> &'static str {
        match self {
            Self::None => "",
            Self::ChatGpt => "https://chatgpt.com/",
            Self::Claude | Self::ClaudeOpus => "https://claude.ai/new",
            Self::Gemini => "https://gemini.google.com/app",
            Self::GoogleLens => "https://lens.google.com/search?hl=en&country=us",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default)]
pub struct UploadSettings {
    #[serde(alias = "ImgurClientId")]
    pub imgur_client_id: String,
    #[serde(alias = "ImgurAccessToken")]
    pub imgur_access_token: String,
    #[serde(alias = "ImgBBApiKey")]
    pub imgbb_api_key: String,
    #[serde(alias = "ImgPileApiToken")]
    pub imgpile_api_token: String,
    #[serde(alias = "GyazoAccessToken")]
    pub gyazo_access_token: String,
    #[serde(alias = "DropboxAccessToken")]
    pub dropbox_access_token: String,
    #[serde(alias = "DropboxPathPrefix")]
    pub dropbox_path_prefix: String,
    #[serde(alias = "GoogleDriveAccessToken")]
    pub google_drive_access_token: String,
    #[serde(alias = "GoogleDriveFolderId")]
    pub google_drive_folder_id: String,
    #[serde(alias = "OneDriveAccessToken")]
    pub one_drive_access_token: String,
    #[serde(alias = "OneDriveFolder")]
    pub one_drive_folder: String,
    #[serde(alias = "AzureBlobSasUrl")]
    pub azure_blob_sas_url: String,
    #[serde(alias = "GitHubToken")]
    pub github_token: String,
    #[serde(alias = "GitHubRepo")]
    pub github_repo: String,
    #[serde(alias = "GitHubBranch")]
    pub github_branch: String,
    #[serde(alias = "GitHubPathPrefix")]
    pub github_path_prefix: String,
    #[serde(alias = "ImmichBaseUrl")]
    pub immich_base_url: String,
    #[serde(alias = "ImmichApiKey")]
    pub immich_api_key: String,
    #[serde(alias = "FtpUrl")]
    pub ftp_url: String,
    #[serde(alias = "FtpUsername")]
    pub ftp_username: String,
    #[serde(alias = "FtpPassword")]
    pub ftp_password: String,
    #[serde(alias = "FtpPublicUrl")]
    pub ftp_public_url: String,
    #[serde(alias = "SftpHost")]
    pub sftp_host: String,
    #[serde(alias = "SftpPort")]
    pub sftp_port: u16,
    #[serde(alias = "SftpUsername")]
    pub sftp_username: String,
    #[serde(alias = "SftpPassword")]
    pub sftp_password: String,
    #[serde(alias = "SftpRemotePath")]
    pub sftp_remote_path: String,
    #[serde(alias = "SftpPublicUrl")]
    pub sftp_public_url: String,
    #[serde(alias = "SftpHostKeyFingerprint")]
    pub sftp_host_key_fingerprint: String,
    #[serde(alias = "WebDavUrl")]
    pub web_dav_url: String,
    #[serde(alias = "WebDavUsername")]
    pub web_dav_username: String,
    #[serde(alias = "WebDavPassword")]
    pub web_dav_password: String,
    #[serde(alias = "WebDavPublicUrl")]
    pub web_dav_public_url: String,
    #[serde(alias = "S3Endpoint")]
    pub s3_endpoint: String,
    #[serde(alias = "S3Region")]
    pub s3_region: String,
    #[serde(alias = "S3Bucket")]
    pub s3_bucket: String,
    #[serde(alias = "S3AccessKey")]
    pub s3_access_key: String,
    #[serde(alias = "S3SecretKey")]
    pub s3_secret_key: String,
    #[serde(alias = "S3PathPrefix")]
    pub s3_path_prefix: String,
    #[serde(alias = "S3PublicUrl")]
    pub s3_public_url: String,
    #[serde(alias = "CustomUploadUrl")]
    pub custom_upload_url: String,
    #[serde(alias = "CustomFileFormName")]
    pub custom_file_form_name: String,
    #[serde(alias = "CustomResponseUrlPath")]
    pub custom_response_url_path: String,
    #[serde(alias = "CustomHeaders")]
    pub custom_headers: String,
    #[serde(
        alias = "AiChatProvider",
        deserialize_with = "deserialize_ai_chat_provider"
    )]
    pub ai_chat_provider: AiChatProvider,
    #[serde(alias = "AiChatUploadDestinationSynced")]
    pub ai_chat_upload_destination_synced: bool,
    #[serde(
        alias = "AiChatUploadDestination",
        deserialize_with = "deserialize_legacy_destination_name"
    )]
    pub ai_chat_upload_destination: String,
}

impl Default for UploadSettings {
    fn default() -> Self {
        Self {
            imgur_client_id: String::new(),
            imgur_access_token: String::new(),
            imgbb_api_key: String::new(),
            imgpile_api_token: String::new(),
            gyazo_access_token: String::new(),
            dropbox_access_token: String::new(),
            dropbox_path_prefix: "OddSnap".into(),
            google_drive_access_token: String::new(),
            google_drive_folder_id: String::new(),
            one_drive_access_token: String::new(),
            one_drive_folder: "OddSnap".into(),
            azure_blob_sas_url: String::new(),
            github_token: String::new(),
            github_repo: String::new(),
            github_branch: "main".into(),
            github_path_prefix: "uploads".into(),
            immich_base_url: String::new(),
            immich_api_key: String::new(),
            ftp_url: String::new(),
            ftp_username: String::new(),
            ftp_password: String::new(),
            ftp_public_url: String::new(),
            sftp_host: String::new(),
            sftp_port: 22,
            sftp_username: String::new(),
            sftp_password: String::new(),
            sftp_remote_path: "/".into(),
            sftp_public_url: String::new(),
            sftp_host_key_fingerprint: String::new(),
            web_dav_url: String::new(),
            web_dav_username: String::new(),
            web_dav_password: String::new(),
            web_dav_public_url: String::new(),
            s3_endpoint: String::new(),
            s3_region: "auto".into(),
            s3_bucket: String::new(),
            s3_access_key: String::new(),
            s3_secret_key: String::new(),
            s3_path_prefix: String::new(),
            s3_public_url: String::new(),
            custom_upload_url: String::new(),
            custom_file_form_name: "file".into(),
            custom_response_url_path: "url".into(),
            custom_headers: String::new(),
            ai_chat_provider: AiChatProvider::default(),
            ai_chat_upload_destination_synced: false,
            ai_chat_upload_destination: "TempHosts".into(),
        }
    }
}

impl UploadSettings {
    pub fn from_json_value(value: Option<&Value>) -> Self {
        value
            .cloned()
            .and_then(|value| serde_json::from_value(value).ok())
            .unwrap_or_default()
    }

    pub fn ai_chat_upload_destination(&self) -> UploadDestination {
        normalize_ai_chat_upload_destination(UploadDestination::from_legacy_name(
            &self.ai_chat_upload_destination,
        ))
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum UploadPreflight {
    Disabled,
    Ready {
        destination: UploadDestination,
        provider_name: String,
    },
    Blocked {
        destination: UploadDestination,
        provider_name: String,
        error: String,
    },
}

impl UploadPreflight {
    pub fn provider_name(&self) -> Option<&str> {
        match self {
            Self::Disabled => None,
            Self::Ready { provider_name, .. } | Self::Blocked { provider_name, .. } => {
                Some(provider_name)
            }
        }
    }
}

pub fn upload_preflight_for_media(
    settings: &AppSettings,
    kind: HistoryKind,
    file_path: &Path,
    use_ai_redirect: bool,
) -> UploadPreflight {
    let destination = UploadDestination::from_legacy_name(&settings.image_upload_destination);
    if !should_upload_media(settings, kind, file_path, use_ai_redirect) {
        return UploadPreflight::Disabled;
    }

    let upload_settings = UploadSettings::from_json_value(settings.image_upload_settings.as_ref());
    let effective_destination = if destination == UploadDestination::AiChat {
        upload_settings.ai_chat_upload_destination()
    } else {
        destination
    };
    let provider_name = effective_destination.display_name();

    if let Some(error) = effective_destination.configuration_error(&upload_settings) {
        return UploadPreflight::Blocked {
            destination: effective_destination,
            provider_name,
            error,
        };
    }

    let file_size = file_path
        .metadata()
        .map(|metadata| metadata.len())
        .unwrap_or(0);
    let max_size = effective_destination.max_size_bytes(file_path);
    if file_size > max_size {
        let error = format!(
            "File too large ({}MB). {} limit is {}.",
            file_size / MIB,
            provider_name,
            format_size_limit(max_size)
        );
        return UploadPreflight::Blocked {
            destination: effective_destination,
            provider_name,
            error,
        };
    }

    UploadPreflight::Ready {
        destination: effective_destination,
        provider_name,
    }
}

pub fn should_upload_media(
    settings: &AppSettings,
    kind: HistoryKind,
    file_path: &Path,
    use_ai_redirect: bool,
) -> bool {
    if file_path.as_os_str().is_empty() {
        return false;
    }

    let destination = UploadDestination::from_legacy_name(&settings.image_upload_destination);
    if destination == UploadDestination::None {
        return false;
    }

    if destination == UploadDestination::AiChat {
        return kind == HistoryKind::Image && use_ai_redirect;
    }

    match kind {
        HistoryKind::Image | HistoryKind::Sticker => settings.auto_upload_screenshots,
        HistoryKind::Gif => settings.auto_upload_gifs,
        HistoryKind::Video => settings.auto_upload_videos,
    }
}

pub fn normalize_ai_chat_upload_destination(destination: UploadDestination) -> UploadDestination {
    match destination {
        UploadDestination::None | UploadDestination::AiChat => UploadDestination::TempHosts,
        other => other,
    }
}

pub fn build_google_lens_url(image_url: &str) -> Result<String, String> {
    if !(image_url.starts_with("http://") || image_url.starts_with("https://")) {
        return Err("Google Lens needs an absolute image URL.".into());
    }

    Ok(format!(
        "https://lens.google.com/uploadbyurl?url={}&hl=en&country=us",
        percent_encode_url(image_url)
    ))
}

fn missing_upload_setting(setting_name: &str) -> String {
    format!("{setting_name} not configured. Add or update it in Settings -> Uploads.")
}

fn normalized_key(value: &str) -> String {
    value
        .chars()
        .filter(|character| character.is_ascii_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect()
}

fn is_https_url(value: &str) -> bool {
    value.trim().to_ascii_lowercase().starts_with("https://")
}

fn format_size_limit(bytes: u64) -> String {
    if bytes == u64::MAX {
        "unlimited".into()
    } else if bytes >= GIB {
        format!("{}GB", bytes / GIB)
    } else if bytes >= MIB {
        format!("{}MB", bytes / MIB)
    } else {
        format!("{}KB", bytes / 1024)
    }
}

fn percent_encode_url(value: &str) -> String {
    value
        .bytes()
        .flat_map(|byte| match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                vec![byte as char]
            }
            _ => {
                let encoded = format!("%{byte:02X}");
                encoded.chars().collect()
            }
        })
        .collect()
}

fn deserialize_ai_chat_provider<'de, D>(deserializer: D) -> Result<AiChatProvider, D::Error>
where
    D: Deserializer<'de>,
{
    let value = Value::deserialize(deserializer)?;
    Ok(match value {
        Value::Number(number) => match number.as_i64() {
            Some(-1) => AiChatProvider::None,
            Some(0) => AiChatProvider::ChatGpt,
            Some(1) => AiChatProvider::Claude,
            Some(2) => AiChatProvider::ClaudeOpus,
            Some(3) => AiChatProvider::Gemini,
            Some(4) => AiChatProvider::GoogleLens,
            _ => AiChatProvider::default(),
        },
        Value::String(value) => match normalized_key(&value).as_str() {
            "none" => AiChatProvider::None,
            "chatgpt" => AiChatProvider::ChatGpt,
            "claude" => AiChatProvider::Claude,
            "claudeopus" => AiChatProvider::ClaudeOpus,
            "gemini" => AiChatProvider::Gemini,
            "googlelens" => AiChatProvider::GoogleLens,
            _ => AiChatProvider::default(),
        },
        Value::Null => AiChatProvider::default(),
        other => {
            return Err(de::Error::custom(format!(
                "unsupported AI chat provider value: {other}"
            )))
        }
    })
}

fn deserialize_legacy_destination_name<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: Deserializer<'de>,
{
    let value = Value::deserialize(deserializer)?;
    Ok(match value {
        Value::String(value) => value,
        Value::Number(number) => number
            .as_u64()
            .and_then(legacy_upload_destination_name_from_index)
            .unwrap_or("TempHosts")
            .into(),
        Value::Null => "TempHosts".into(),
        other => {
            return Err(de::Error::custom(format!(
                "unsupported upload destination value: {other}"
            )))
        }
    })
}

fn legacy_upload_destination_name_from_index(index: u64) -> Option<&'static str> {
    Some(match index {
        0 => "None",
        1 => "Imgur",
        2 => "ImgBB",
        3 => "Catbox",
        4 => "Litterbox",
        5 => "Gyazo",
        6 => "FileIo",
        7 => "Uguu",
        8 => "TransferSh",
        9 => "Dropbox",
        10 => "GoogleDrive",
        11 => "OneDrive",
        12 => "AzureBlob",
        13 => "GitHub",
        14 => "Immich",
        15 => "Ftp",
        16 => "Sftp",
        17 => "WebDav",
        18 => "S3Compatible",
        19 => "CustomHttp",
        20 => "AiChat",
        21 => "TempHosts",
        22 => "TmpFiles",
        23 => "Gofile",
        24 => "ImgPile",
        _ => return None,
    })
}

const MIB: u64 = 1024 * 1024;
const GIB: u64 = 1024 * MIB;

#[cfg(test)]
mod tests {
    use std::{fs, path::PathBuf};

    use super::*;

    #[test]
    fn upload_destination_names_match_legacy_surface() {
        assert_eq!(
            UploadDestination::from_legacy_name("Imgur").display_name(),
            "Imgur"
        );
        assert_eq!(
            UploadDestination::from_legacy_name("S3Compatible").display_name(),
            "S3"
        );
        assert_eq!(
            UploadDestination::from_legacy_name("tmpfiles.org").display_name(),
            "tmpfiles.org"
        );
        assert!(matches!(
            UploadDestination::from_legacy_name("Nope"),
            UploadDestination::Unknown(_)
        ));
    }

    #[test]
    fn configuration_errors_cover_credentialed_destinations() {
        let settings = UploadSettings::default();

        assert_eq!(
            UploadDestination::S3Compatible.configuration_error(&settings),
            Some("S3 endpoint not configured. Add or update it in Settings -> Uploads.".into())
        );
        assert_eq!(
            UploadDestination::WebDav.configuration_error(&UploadSettings {
                web_dav_url: "http://example.test/uploads".into(),
                web_dav_username: "user".into(),
                ..UploadSettings::default()
            }),
            Some("WebDAV uploads require an HTTPS URL.".into())
        );
        assert_eq!(
            UploadDestination::Catbox.configuration_error(&settings),
            None
        );
    }

    #[test]
    fn upload_settings_parse_pascal_case_legacy_json() {
        let settings = UploadSettings::from_json_value(Some(&serde_json::json!({
            "ImgurClientId": "cid",
            "S3Endpoint": "https://s3.example.test",
            "S3Bucket": "bucket",
            "S3AccessKey": "access",
            "S3SecretKey": "secret",
            "AiChatProvider": 2,
            "AiChatUploadDestination": 0
        })));

        assert_eq!(settings.imgur_client_id, "cid");
        assert_eq!(settings.s3_endpoint, "https://s3.example.test");
        assert_eq!(settings.ai_chat_provider, AiChatProvider::ClaudeOpus);
        assert_eq!(
            settings.ai_chat_upload_destination(),
            UploadDestination::TempHosts
        );
    }

    #[test]
    fn upload_preflight_blocks_missing_credentials_and_oversized_files() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-upload-preflight-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let file = root.join("capture.png");
        fs::write(&file, [0_u8; 8]).expect("write temp file");

        let mut app_settings = AppSettings {
            image_upload_destination: "Imgur".into(),
            auto_upload_screenshots: true,
            ..AppSettings::default()
        };

        assert!(matches!(
            upload_preflight_for_media(&app_settings, HistoryKind::Image, &file, false),
            UploadPreflight::Blocked { error, .. } if error.contains("Imgur client ID")
        ));

        app_settings.image_upload_destination = "Catbox".into();
        assert!(matches!(
            upload_preflight_for_media(&app_settings, HistoryKind::Image, &file, false),
            UploadPreflight::Ready { provider_name, .. } if provider_name == "Catbox"
        ));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn upload_preflight_matches_media_kind_toggles() {
        let file = PathBuf::from("capture.mp4");
        let settings = AppSettings {
            image_upload_destination: "Catbox".into(),
            auto_upload_screenshots: false,
            auto_upload_videos: true,
            ..AppSettings::default()
        };

        assert_eq!(
            upload_preflight_for_media(&settings, HistoryKind::Image, &file, false),
            UploadPreflight::Disabled
        );
        assert!(matches!(
            upload_preflight_for_media(&settings, HistoryKind::Video, &file, false),
            UploadPreflight::Ready { provider_name, .. } if provider_name == "Catbox"
        ));
    }

    #[test]
    fn google_lens_url_encodes_absolute_image_url() {
        assert_eq!(
            build_google_lens_url("https://files.example.test/a b.png").expect("build url"),
            "https://lens.google.com/uploadbyurl?url=https%3A%2F%2Ffiles.example.test%2Fa%20b.png&hl=en&country=us"
        );
        assert!(build_google_lens_url("capture.png").is_err());
    }
}
