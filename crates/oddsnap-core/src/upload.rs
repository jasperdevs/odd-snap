use std::{
    fmt,
    path::Path,
    time::{SystemTime, UNIX_EPOCH},
};

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
            Self::TransferSh => Some(
                "The public transfer.sh service is unavailable. Choose Temp Hosts, Catbox, Litterbox, Uguu, or file.io.".into(),
            ),
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
            Self::AzureBlob if !is_https_url(&settings.azure_blob_sas_url) => {
                Some("Azure Blob uploads require an HTTPS SAS URL.".into())
            }
            Self::Unknown(value) => Some(format!(
                "Unknown upload destination '{value}'. Choose a supported destination in Settings -> Uploads."
            )),
            _ => None,
        }
    }

    pub fn curl_upload_supported(&self) -> bool {
        matches!(
            self,
            Self::Imgur
                | Self::ImgBb
                | Self::ImgPile
                | Self::Catbox
                | Self::Litterbox
                | Self::Gyazo
                | Self::FileIo
                | Self::Uguu
                | Self::TmpFiles
                | Self::Gofile
                | Self::CustomHttp
                | Self::WebDav
                | Self::AzureBlob
                | Self::Ftp
                | Self::S3Compatible
                | Self::Sftp
                | Self::GitHub
                | Self::Immich
                | Self::Dropbox
                | Self::OneDrive
                | Self::GoogleDrive
        )
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

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CurlUploadRequest {
    pub destination: UploadDestination,
    pub provider_name: String,
    pub program: String,
    pub args: Vec<String>,
    pub stdin_body: Option<Vec<u8>>,
    pub success_url: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DropboxCurlUploadPlan {
    pub upload: CurlUploadRequest,
    pub create_shared_link: CurlUploadRequest,
    pub list_shared_links: CurlUploadRequest,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OneDriveCurlUploadPlan {
    pub upload: CurlUploadRequest,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GoogleDriveCurlUploadPlan {
    pub kind: GoogleDriveUploadPlanKind,
    pub upload: CurlUploadRequest,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum GoogleDriveUploadPlanKind {
    Multipart,
    Resumable,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UploadSuccess {
    pub url: String,
    pub provider_name: String,
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
    upload_preflight_for_media_inner(settings, kind, file_path, use_ai_redirect, true)
}

pub fn upload_preflight_for_explicit_media(
    settings: &AppSettings,
    kind: HistoryKind,
    file_path: &Path,
    use_ai_redirect: bool,
) -> UploadPreflight {
    upload_preflight_for_media_inner(settings, kind, file_path, use_ai_redirect, false)
}

fn upload_preflight_for_media_inner(
    settings: &AppSettings,
    kind: HistoryKind,
    file_path: &Path,
    use_ai_redirect: bool,
    require_auto_enabled: bool,
) -> UploadPreflight {
    let destination = UploadDestination::from_legacy_name(&settings.image_upload_destination);
    if !should_upload_media_inner(
        settings,
        kind,
        file_path,
        use_ai_redirect,
        require_auto_enabled,
    ) {
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
    should_upload_media_inner(settings, kind, file_path, use_ai_redirect, true)
}

fn should_upload_media_inner(
    settings: &AppSettings,
    kind: HistoryKind,
    file_path: &Path,
    use_ai_redirect: bool,
    require_auto_enabled: bool,
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

    if !require_auto_enabled {
        return matches!(
            kind,
            HistoryKind::Image | HistoryKind::Sticker | HistoryKind::Gif | HistoryKind::Video
        );
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

pub fn temporary_host_fallbacks() -> &'static [UploadDestination] {
    &[
        UploadDestination::Litterbox,
        UploadDestination::TmpFiles,
        UploadDestination::Uguu,
        UploadDestination::Gofile,
        UploadDestination::Catbox,
    ]
}

pub fn build_curl_upload_request(
    destination: UploadDestination,
    file_path: &Path,
) -> Result<CurlUploadRequest, String> {
    build_curl_upload_request_with_settings(destination, file_path, &UploadSettings::default())
}

pub fn build_dropbox_curl_upload_plan(
    file_path: &Path,
    settings: &UploadSettings,
) -> Result<DropboxCurlUploadPlan, String> {
    let token = settings.dropbox_access_token.trim();
    if token.is_empty() {
        return Err(missing_upload_setting("Dropbox access token"));
    }
    let upload_path = dropbox_upload_path(file_path, settings)?;
    let file_body = std::fs::read(file_path)
        .map_err(|error| format!("Dropbox upload could not read file: {error}"))?;

    let upload_api_arg = serde_json::json!({
        "path": upload_path.as_str(),
        "mode": "add",
        "autorename": true,
        "mute": false,
    })
    .to_string();
    let upload = CurlUploadRequest {
        destination: UploadDestination::Dropbox,
        provider_name: UploadDestination::Dropbox.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                format!("Dropbox-API-Arg: {upload_api_arg}"),
                "--header".into(),
                "Content-Type: application/octet-stream".into(),
                "--data-binary".into(),
                "@-".into(),
                "https://content.dropboxapi.com/2/files/upload".into(),
            ])
            .collect(),
        stdin_body: Some(file_body),
        success_url: None,
    };

    let create_body = serde_json::json!({ "path": upload_path.as_str() }).to_string();
    let create_shared_link = dropbox_json_request(
        token,
        "https://api.dropboxapi.com/2/sharing/create_shared_link_with_settings",
        create_body,
    );

    let list_body = serde_json::json!({
        "path": upload_path.as_str(),
        "direct_only": true,
    })
    .to_string();
    let list_shared_links = dropbox_json_request(
        token,
        "https://api.dropboxapi.com/2/sharing/list_shared_links",
        list_body,
    );

    Ok(DropboxCurlUploadPlan {
        upload,
        create_shared_link,
        list_shared_links,
    })
}

pub fn build_onedrive_curl_upload_plan(
    file_path: &Path,
    settings: &UploadSettings,
) -> Result<OneDriveCurlUploadPlan, String> {
    let token = settings.one_drive_access_token.trim();
    if token.is_empty() {
        return Err(missing_upload_setting("OneDrive access token"));
    }
    let body = std::fs::read(file_path)
        .map_err(|error| format!("OneDrive upload could not read file: {error}"))?;
    let upload = CurlUploadRequest {
        destination: UploadDestination::OneDrive,
        provider_name: UploadDestination::OneDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "PUT".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                "Content-Type: application/octet-stream".into(),
                "--data-binary".into(),
                "@-".into(),
                onedrive_upload_url(file_path, settings)?,
            ])
            .collect(),
        stdin_body: Some(body),
        success_url: None,
    };
    Ok(OneDriveCurlUploadPlan { upload })
}

pub fn build_onedrive_create_link_request(
    item_id: &str,
    settings: &UploadSettings,
) -> Result<CurlUploadRequest, String> {
    let token = settings.one_drive_access_token.trim();
    if token.is_empty() {
        return Err(missing_upload_setting("OneDrive access token"));
    }
    let item_id = item_id.trim();
    if item_id.is_empty() {
        return Err("OneDrive returned no item ID.".into());
    }
    let body = serde_json::json!({
        "type": "view",
        "scope": "anonymous",
    })
    .to_string();
    Ok(CurlUploadRequest {
        destination: UploadDestination::OneDrive,
        provider_name: UploadDestination::OneDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                "Content-Type: application/json".into(),
                "--data-binary".into(),
                "@-".into(),
                format!(
                    "https://graph.microsoft.com/v1.0/me/drive/items/{}/createLink",
                    percent_encode_path_component(item_id)
                ),
            ])
            .collect(),
        stdin_body: Some(body.into_bytes()),
        success_url: None,
    })
}

pub fn build_google_drive_curl_upload_plan(
    file_path: &Path,
    settings: &UploadSettings,
) -> Result<GoogleDriveCurlUploadPlan, String> {
    let token = settings.google_drive_access_token.trim();
    if token.is_empty() {
        return Err(missing_upload_setting("Google Drive access token"));
    }
    let metadata = std::fs::metadata(file_path)
        .map_err(|error| format!("Google Drive upload could not read file metadata: {error}"))?;
    if metadata.len() > 5 * MIB {
        return build_google_drive_resumable_session_plan(file_path, settings, metadata.len());
    }
    let boundary = "oddsnap-rust-drive-boundary";
    let body = google_drive_multipart_body(file_path, settings, boundary)?;
    let upload = CurlUploadRequest {
        destination: UploadDestination::GoogleDrive,
        provider_name: UploadDestination::GoogleDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                format!("Content-Type: multipart/related; boundary={boundary}"),
                "--data-binary".into(),
                "@-".into(),
                "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink,webContentLink".into(),
            ])
            .collect(),
        stdin_body: Some(body),
        success_url: None,
    };
    Ok(GoogleDriveCurlUploadPlan {
        kind: GoogleDriveUploadPlanKind::Multipart,
        upload,
    })
}

fn build_google_drive_resumable_session_plan(
    file_path: &Path,
    settings: &UploadSettings,
    file_size: u64,
) -> Result<GoogleDriveCurlUploadPlan, String> {
    let token = settings.google_drive_access_token.trim();
    let metadata = google_drive_metadata(file_path, settings)?.to_string();
    let upload = CurlUploadRequest {
        destination: UploadDestination::GoogleDrive,
        provider_name: UploadDestination::GoogleDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--dump-header".into(),
                "-".into(),
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                format!("X-Upload-Content-Type: {}", upload_content_type(file_path)),
                "--header".into(),
                format!("X-Upload-Content-Length: {file_size}"),
                "--header".into(),
                "Content-Type: application/json; charset=UTF-8".into(),
                "--data-binary".into(),
                "@-".into(),
                "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id"
                    .into(),
            ])
            .collect(),
        stdin_body: Some(metadata.into_bytes()),
        success_url: None,
    };
    Ok(GoogleDriveCurlUploadPlan {
        kind: GoogleDriveUploadPlanKind::Resumable,
        upload,
    })
}

pub fn build_google_drive_resumable_upload_request(
    session_url: &str,
    file_path: &Path,
) -> Result<CurlUploadRequest, String> {
    let session_url = session_url.trim();
    if !(session_url.starts_with("https://") || session_url.starts_with("http://")) {
        return Err("Google Drive upload session URL was not usable.".into());
    }
    let body = std::fs::read(file_path)
        .map_err(|error| format!("Google Drive resumable upload could not read file: {error}"))?;
    Ok(CurlUploadRequest {
        destination: UploadDestination::GoogleDrive,
        provider_name: UploadDestination::GoogleDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "PUT".into(),
                "--header".into(),
                format!("Content-Type: {}", upload_content_type(file_path)),
                "--data-binary".into(),
                "@-".into(),
                session_url.into(),
            ])
            .collect(),
        stdin_body: Some(body),
        success_url: None,
    })
}

pub fn build_google_drive_permission_request(
    file_id: &str,
    settings: &UploadSettings,
) -> Result<CurlUploadRequest, String> {
    let token = settings.google_drive_access_token.trim();
    if token.is_empty() {
        return Err(missing_upload_setting("Google Drive access token"));
    }
    let file_id = file_id.trim();
    if file_id.is_empty() {
        return Err("Google Drive returned no file ID.".into());
    }
    let body = serde_json::json!({
        "role": "reader",
        "type": "anyone",
    })
    .to_string();
    Ok(CurlUploadRequest {
        destination: UploadDestination::GoogleDrive,
        provider_name: UploadDestination::GoogleDrive.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                "Content-Type: application/json".into(),
                "--data-binary".into(),
                "@-".into(),
                format!(
                    "https://www.googleapis.com/drive/v3/files/{}/permissions",
                    percent_encode_path_component(file_id)
                ),
            ])
            .collect(),
        stdin_body: Some(body.into_bytes()),
        success_url: Some(google_drive_file_url(file_id)),
    })
}

pub fn build_curl_upload_request_with_settings(
    destination: UploadDestination,
    file_path: &Path,
    settings: &UploadSettings,
) -> Result<CurlUploadRequest, String> {
    if !destination.curl_upload_supported() {
        return Err(format!(
            "{} upload is not implemented in the Rust curl backend yet.",
            destination.display_name()
        ));
    }

    let mut args = curl_base_args();
    let mut stdin_body = None;
    let mut success_url = None;

    match destination {
        UploadDestination::Imgur => {
            let client_id = settings.imgur_client_id.trim();
            if client_id.is_empty() {
                return Err(missing_upload_setting("Imgur client ID"));
            }
            let access_token = settings.imgur_access_token.trim();
            args.extend(["--header".into()]);
            args.push(if access_token.is_empty() {
                format!("Authorization: Client-ID {client_id}")
            } else {
                format!("Authorization: Bearer {access_token}")
            });
            args.extend([
                "-F".into(),
                curl_file_form("image", file_path),
                "https://api.imgur.com/3/image".into(),
            ]);
        }
        UploadDestination::ImgBb => {
            let api_key = settings.imgbb_api_key.trim();
            if api_key.is_empty() {
                return Err(missing_upload_setting("ImgBB API key"));
            }
            args.extend([
                "-F".into(),
                curl_file_form("image", file_path),
                format!("https://api.imgbb.com/1/upload?key={api_key}"),
            ]);
        }
        UploadDestination::ImgPile => {
            let token = settings.imgpile_api_token.trim();
            if token.is_empty() {
                return Err(missing_upload_setting("imgpile API token"));
            }
            args.extend([
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "-F".into(),
                curl_file_form("file", file_path),
                "https://cdn.imgpile.com/api/v1/media".into(),
            ]);
        }
        UploadDestination::Catbox => {
            args.extend([
                "-F".into(),
                "reqtype=fileupload".into(),
                "-F".into(),
                curl_file_form("fileToUpload", file_path),
                "https://catbox.moe/user/api.php".into(),
            ]);
        }
        UploadDestination::Litterbox => {
            args.extend([
                "-F".into(),
                "reqtype=fileupload".into(),
                "-F".into(),
                "time=72h".into(),
                "-F".into(),
                curl_file_form("fileToUpload", file_path),
                "https://litterbox.catbox.moe/resources/internals/api.php".into(),
            ]);
        }
        UploadDestination::Gyazo => {
            let access_token = settings.gyazo_access_token.trim();
            if access_token.is_empty() {
                return Err(missing_upload_setting("Gyazo access token"));
            }
            args.extend([
                "-F".into(),
                format!("access_token={access_token}"),
                "-F".into(),
                curl_file_form("imagedata", file_path),
                "https://upload.gyazo.com/api/upload".into(),
            ]);
        }
        UploadDestination::FileIo => {
            args.extend([
                "-F".into(),
                curl_file_form("file", file_path),
                "https://file.io".into(),
            ]);
        }
        UploadDestination::Uguu => {
            args.extend([
                "-F".into(),
                curl_file_form("files[]", file_path),
                "https://uguu.se/upload?output=text".into(),
            ]);
        }
        UploadDestination::TmpFiles => {
            args.extend([
                "-F".into(),
                curl_file_form("file", file_path),
                "https://tmpfiles.org/api/v1/upload".into(),
            ]);
        }
        UploadDestination::Gofile => {
            args.extend([
                "-F".into(),
                curl_file_form("file", file_path),
                "https://upload.gofile.io/uploadfile".into(),
            ]);
        }
        UploadDestination::CustomHttp => {
            let upload_url = settings.custom_upload_url.trim();
            if upload_url.is_empty() {
                return Err(missing_upload_setting("Custom upload URL"));
            }
            for line in settings
                .custom_headers
                .lines()
                .filter(|line| !line.trim().is_empty())
            {
                let (name, value) = parse_custom_upload_header(line)?;
                args.extend(["--header".into(), format!("{name}: {value}")]);
            }
            let field_name = settings.custom_file_form_name.trim();
            let field_name = if field_name.is_empty() {
                "file"
            } else {
                field_name
            };
            args.extend([
                "-F".into(),
                curl_file_form(field_name, file_path),
                upload_url.into(),
            ]);
        }
        UploadDestination::WebDav => {
            let base_url = settings.web_dav_url.trim();
            if base_url.is_empty() {
                return Err(missing_upload_setting("WebDAV URL"));
            }
            if !is_https_url(base_url) {
                return Err("WebDAV uploads require an HTTPS URL.".into());
            }
            let username = settings.web_dav_username.trim();
            if username.is_empty() {
                return Err(missing_upload_setting("WebDAV username"));
            }
            let file_name = upload_file_name(file_path)?;
            let upload_url = append_url_path(base_url, &file_name);
            success_url = Some(if settings.web_dav_public_url.trim().is_empty() {
                upload_url.clone()
            } else {
                append_url_path(settings.web_dav_public_url.trim(), &file_name)
            });
            args.extend([
                "--request".into(),
                "PUT".into(),
                "--user".into(),
                format!("{username}:{}", settings.web_dav_password),
                "--upload-file".into(),
                file_path.display().to_string(),
                upload_url,
            ]);
        }
        UploadDestination::AzureBlob => {
            let file_name = upload_file_name(file_path)?;
            let (upload_url, public_url) =
                azure_blob_urls(&settings.azure_blob_sas_url, &file_name)?;
            success_url = Some(public_url);
            args.extend([
                "--request".into(),
                "PUT".into(),
                "--header".into(),
                "x-ms-blob-type: BlockBlob".into(),
                "--upload-file".into(),
                file_path.display().to_string(),
                upload_url,
            ]);
        }
        UploadDestination::Ftp => {
            let file_name = upload_file_name(file_path)?;
            let upload_url = ftp_upload_url(&settings.ftp_url, &file_name)?;
            let username = settings.ftp_username.trim();
            if username.is_empty() {
                return Err(missing_upload_setting("FTP username"));
            }
            success_url = Some(if settings.ftp_public_url.trim().is_empty() {
                upload_url.clone()
            } else {
                append_url_path(settings.ftp_public_url.trim(), &file_name)
            });
            args.extend([
                "--user".into(),
                format!("{username}:{}", settings.ftp_password),
                "--upload-file".into(),
                file_path.display().to_string(),
                upload_url,
            ]);
        }
        UploadDestination::S3Compatible => {
            let endpoint = s3_endpoint_url(&settings.s3_endpoint)?;
            let bucket = settings.s3_bucket.trim();
            if bucket.is_empty() {
                return Err(missing_upload_setting("S3 bucket"));
            }
            let access_key = settings.s3_access_key.trim();
            if access_key.is_empty() {
                return Err(missing_upload_setting("S3 access key"));
            }
            if settings.s3_secret_key.trim().is_empty() {
                return Err(missing_upload_setting("S3 secret key"));
            }

            let key = s3_object_key(file_path, settings)?;
            let object_url = append_url_path_segments(&endpoint, [bucket, &key]);
            success_url = Some(if settings.s3_public_url.trim().is_empty() {
                object_url.clone()
            } else {
                append_url_path_segments(settings.s3_public_url.trim(), [key.as_str()])
            });
            let region = if settings.s3_region.trim().is_empty() {
                "auto"
            } else {
                settings.s3_region.trim()
            };
            args.extend([
                "--request".into(),
                "PUT".into(),
                "--aws-sigv4".into(),
                format!("aws:amz:{region}:s3"),
                "--user".into(),
                format!("{access_key}:{}", settings.s3_secret_key),
                "--upload-file".into(),
                file_path.display().to_string(),
                object_url,
            ]);
        }
        UploadDestination::Sftp => {
            let file_name = upload_file_name(file_path)?;
            let upload_url = sftp_upload_url(settings, &file_name)?;
            let username = settings.sftp_username.trim();
            if username.is_empty() {
                return Err(missing_upload_setting("SFTP username"));
            }
            let host_key = hex_sha256_fingerprint_to_base64(&settings.sftp_host_key_fingerprint)?;
            success_url = Some(if settings.sftp_public_url.trim().is_empty() {
                upload_url.clone()
            } else {
                append_url_path(settings.sftp_public_url.trim(), &file_name)
            });
            args.extend([
                "--hostpubsha256".into(),
                host_key,
                "--user".into(),
                format!("{username}:{}", settings.sftp_password),
                "--upload-file".into(),
                file_path.display().to_string(),
                upload_url,
            ]);
        }
        UploadDestination::GitHub => {
            let repo = github_repo(&settings.github_repo)?;
            let token = settings.github_token.trim();
            if token.is_empty() {
                return Err(missing_upload_setting("GitHub token"));
            }
            let branch = github_branch(&settings.github_branch);
            let path = github_upload_path(file_path, settings)?;
            let body = std::fs::read(file_path)
                .map_err(|error| format!("GitHub upload could not read file: {error}"))?;
            let payload = serde_json::json!({
                "message": format!("Add OddSnap upload {}", upload_file_name(file_path)?),
                "content": base64_encode(&body),
                "branch": branch,
            });
            stdin_body = Some(payload.to_string().into_bytes());
            success_url = Some(github_raw_url(&repo, branch, &path));
            args.extend([
                "--request".into(),
                "PUT".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                "Accept: application/vnd.github+json".into(),
                "--header".into(),
                "X-GitHub-Api-Version: 2022-11-28".into(),
                "--header".into(),
                "Content-Type: application/json".into(),
                "--data-binary".into(),
                "@-".into(),
                github_contents_url(&repo, &path),
            ]);
        }
        UploadDestination::Immich => {
            let endpoint = immich_assets_url(&settings.immich_base_url)?;
            let api_key = settings.immich_api_key.trim();
            if api_key.is_empty() {
                return Err(missing_upload_setting("Immich API key"));
            }
            let metadata = std::fs::metadata(file_path)
                .map_err(|error| format!("Immich upload could not read file metadata: {error}"))?;
            let modified = metadata
                .modified()
                .map_err(|error| format!("Immich upload could not read modified time: {error}"))?;
            let created = metadata.created().unwrap_or(modified);
            let file_name = upload_file_name(file_path)?;
            args.extend([
                "--header".into(),
                format!("x-api-key: {api_key}"),
                "--form-string".into(),
                format!(
                    "deviceAssetId={}-{}-{}-{}",
                    host_device_name(),
                    file_path.display(),
                    metadata.len(),
                    system_time_unix_seconds(modified)
                ),
                "--form-string".into(),
                "deviceId=OddSnap".into(),
                "--form-string".into(),
                format!("fileCreatedAt={}", system_time_to_rfc3339(created)),
                "--form-string".into(),
                format!("fileModifiedAt={}", system_time_to_rfc3339(modified)),
                "--form-string".into(),
                format!("filename={file_name}"),
                "--form".into(),
                format!("assetData=@{}", file_path.display()),
                endpoint,
            ]);
        }
        UploadDestination::Dropbox => {
            return Err("Dropbox uploads use the multi-step Rust curl backend.".into());
        }
        UploadDestination::OneDrive => {
            return Err("OneDrive uploads use the multi-step Rust curl backend.".into());
        }
        UploadDestination::GoogleDrive => {
            return Err("Google Drive uploads use the multi-step Rust curl backend.".into());
        }
        _ => unreachable!("unsupported destinations returned early"),
    }

    Ok(CurlUploadRequest {
        provider_name: destination.display_name(),
        program: "curl".into(),
        args,
        stdin_body,
        destination,
        success_url,
    })
}

pub fn parse_curl_upload_output(
    destination: UploadDestination,
    output: &str,
) -> Result<UploadSuccess, String> {
    parse_curl_upload_output_with_settings(destination, output, &UploadSettings::default())
}

pub fn parse_curl_upload_output_with_settings(
    destination: UploadDestination,
    output: &str,
    settings: &UploadSettings,
) -> Result<UploadSuccess, String> {
    parse_curl_upload_output_with_success_url(destination, output, settings, None)
}

pub fn parse_curl_upload_output_with_success_url(
    destination: UploadDestination,
    output: &str,
    settings: &UploadSettings,
    success_url: Option<&str>,
) -> Result<UploadSuccess, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error(
            &destination.display_name(),
            status_code,
            body,
        ));
    }

    match parse_upload_response_with_settings(destination.clone(), body, settings) {
        Ok(success) => Ok(success),
        Err(error) if body.trim().is_empty() => {
            if let Some(url) = success_url {
                parse_plain_url(&destination.display_name(), url).map(|url| UploadSuccess {
                    url,
                    provider_name: destination.display_name(),
                })
            } else {
                Err(error)
            }
        }
        Err(error) => Err(error),
    }
}

pub fn parse_dropbox_upload_ack(output: &str) -> Result<(), String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if (200..300).contains(&status_code) {
        Ok(())
    } else {
        Err(build_upload_error("Dropbox", status_code, body))
    }
}

pub fn parse_dropbox_shared_link_output(output: &str) -> Result<UploadSuccess, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("Dropbox", status_code, body));
    }
    parse_dropbox_shared_link_body(body)
}

pub fn parse_dropbox_list_shared_links_output(output: &str) -> Result<UploadSuccess, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("Dropbox", status_code, body));
    }
    let node: Value = serde_json::from_str(body)
        .map_err(|error| format!("Dropbox returned invalid JSON: {error}"))?;
    let url = node
        .get("links")
        .and_then(Value::as_array)
        .and_then(|links| links.first())
        .and_then(|link| link.get("url"))
        .and_then(Value::as_str)
        .ok_or_else(|| "Dropbox returned no shared link.".to_string())?;
    dropbox_upload_success(url)
}

pub fn dropbox_shared_link_already_exists(output: &str) -> bool {
    let Ok((body, status_code)) = split_curl_body_and_status(output) else {
        return false;
    };
    if (200..300).contains(&status_code) {
        return false;
    }
    body.contains("shared_link_already_exists")
}

pub fn parse_onedrive_upload_item_id(output: &str) -> Result<String, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("OneDrive", status_code, body));
    }
    let node: Value = serde_json::from_str(body)
        .map_err(|error| format!("OneDrive returned invalid JSON: {error}"))?;
    node.get("id")
        .and_then(Value::as_str)
        .filter(|id| !id.trim().is_empty())
        .map(str::to_string)
        .ok_or_else(|| "OneDrive returned no item ID.".into())
}

pub fn parse_onedrive_create_link_output(output: &str) -> Result<UploadSuccess, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("OneDrive", status_code, body));
    }
    let node: Value = serde_json::from_str(body)
        .map_err(|error| format!("OneDrive returned invalid JSON: {error}"))?;
    let url = node
        .get("link")
        .and_then(|link| link.get("webUrl"))
        .and_then(Value::as_str)
        .ok_or_else(|| "OneDrive returned no sharing link.".to_string())?;
    parse_plain_url("OneDrive", url).map(|url| UploadSuccess {
        url,
        provider_name: UploadDestination::OneDrive.display_name(),
    })
}

pub fn parse_google_drive_upload_file_id(output: &str) -> Result<String, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("Google Drive", status_code, body));
    }
    let node: Value = serde_json::from_str(body)
        .map_err(|error| format!("Google Drive returned invalid JSON: {error}"))?;
    node.get("id")
        .and_then(Value::as_str)
        .filter(|id| !id.trim().is_empty())
        .map(str::to_string)
        .ok_or_else(|| "Google Drive returned no file ID.".into())
}

pub fn parse_google_drive_resumable_session_output(output: &str) -> Result<String, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error(
            "Google Drive upload session",
            status_code,
            body,
        ));
    }
    google_drive_location_header(body)
        .ok_or_else(|| "Google Drive did not return an upload session URL.".into())
}

pub fn parse_google_drive_permission_output(
    output: &str,
    file_id: &str,
) -> Result<UploadSuccess, String> {
    let (body, status_code) = split_curl_body_and_status(output)?;
    if !(200..300).contains(&status_code) {
        return Err(build_upload_error("Google Drive", status_code, body));
    }
    parse_plain_url("Google Drive", &google_drive_file_url(file_id)).map(|url| UploadSuccess {
        url,
        provider_name: UploadDestination::GoogleDrive.display_name(),
    })
}

pub fn parse_upload_response(
    destination: UploadDestination,
    body: &str,
) -> Result<UploadSuccess, String> {
    parse_upload_response_with_settings(destination, body, &UploadSettings::default())
}

pub fn parse_upload_response_with_settings(
    destination: UploadDestination,
    body: &str,
    settings: &UploadSettings,
) -> Result<UploadSuccess, String> {
    let provider_name = destination.display_name();
    match destination {
        UploadDestination::Imgur => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("Imgur returned invalid JSON: {error}"))?;
            if node
                .get("success")
                .and_then(Value::as_bool)
                .unwrap_or(false)
            {
                let url = node
                    .get("data")
                    .and_then(|data| data.get("link"))
                    .and_then(Value::as_str)
                    .ok_or_else(|| "Imgur did not return a usable link.".to_string())?;
                return parse_plain_url("Imgur", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }
            Err(json_error_message("Imgur", &node))
        }
        UploadDestination::ImgBb => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("ImgBB returned invalid JSON: {error}"))?;
            if node
                .get("success")
                .and_then(Value::as_bool)
                .unwrap_or(false)
            {
                let url = node
                    .get("data")
                    .and_then(|data| data.get("url"))
                    .and_then(Value::as_str)
                    .or_else(|| {
                        node.get("data")
                            .and_then(|data| data.get("display_url"))
                            .and_then(Value::as_str)
                    })
                    .ok_or_else(|| "ImgBB did not return a usable link.".to_string())?;
                return parse_plain_url("ImgBB", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }
            Err(json_error_message("ImgBB", &node))
        }
        UploadDestination::ImgPile => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("imgpile returned invalid JSON: {error}"))?;
            let url = node
                .get("media")
                .and_then(|media| media.get("urls"))
                .and_then(|urls| urls.get("original"))
                .and_then(Value::as_str);
            if let Some(url) = url {
                return parse_plain_url("imgpile", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }
            Err(json_error_message("imgpile", &node))
        }
        UploadDestination::Gyazo => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("Gyazo returned invalid JSON: {error}"))?;
            let url = node.get("permalink_url").and_then(Value::as_str);
            if let Some(url) = url {
                return parse_plain_url("Gyazo", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }
            Err(json_error_message("Gyazo", &node))
        }
        UploadDestination::Catbox | UploadDestination::Litterbox | UploadDestination::Uguu => {
            parse_plain_url(&provider_name, body).map(|url| UploadSuccess { url, provider_name })
        }
        UploadDestination::FileIo => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("file.io returned invalid JSON: {error}"))?;
            if node
                .get("success")
                .and_then(Value::as_bool)
                .unwrap_or(false)
            {
                let url = node
                    .get("link")
                    .and_then(Value::as_str)
                    .ok_or_else(|| "file.io did not return a usable link.".to_string())?;
                return parse_plain_url("file.io", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }
            Err(json_error_message("file.io", &node))
        }
        UploadDestination::TmpFiles => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("tmpfiles.org returned invalid JSON: {error}"))?;
            let page_url = node
                .get("data")
                .and_then(|data| data.get("url"))
                .and_then(Value::as_str)
                .ok_or_else(|| json_error_message("tmpfiles.org", &node))?;
            let url = tmpfiles_download_url(page_url)
                .ok_or_else(|| "tmpfiles.org did not return a usable link.".to_string())?;
            Ok(UploadSuccess { url, provider_name })
        }
        UploadDestination::Gofile => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("Gofile returned invalid JSON: {error}"))?;
            let status = node.get("status").and_then(Value::as_str).unwrap_or("");
            let data = node.get("data");
            let url = data
                .and_then(|data| data.get("downloadPage"))
                .and_then(Value::as_str)
                .or_else(|| {
                    data.and_then(|data| data.get("directLink"))
                        .and_then(Value::as_str)
                })
                .or_else(|| {
                    data.and_then(|data| data.get("link"))
                        .and_then(Value::as_str)
                });
            if status.eq_ignore_ascii_case("ok") {
                if let Some(url) = url {
                    return parse_plain_url("Gofile", url)
                        .map(|url| UploadSuccess { url, provider_name });
                }
            }
            Err(json_error_message("Gofile", &node))
        }
        UploadDestination::GitHub => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("GitHub returned invalid JSON: {error}"))?;
            let url = node
                .get("content")
                .and_then(|content| content.get("download_url"))
                .and_then(Value::as_str);
            if let Some(url) = url {
                return parse_plain_url("GitHub", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }

            let path = node
                .get("content")
                .and_then(|content| content.get("path"))
                .and_then(Value::as_str);
            if let Some(path) = path {
                let repo = github_repo(&settings.github_repo)?;
                let branch = github_branch(&settings.github_branch);
                return parse_plain_url("GitHub", &github_raw_url(&repo, branch, path))
                    .map(|url| UploadSuccess { url, provider_name });
            }

            Err(json_error_message("GitHub", &node))
        }
        UploadDestination::Immich => {
            let node: Value = serde_json::from_str(body)
                .map_err(|error| format!("Immich returned invalid JSON: {error}"))?;
            let id = node
                .get("id")
                .and_then(Value::as_str)
                .filter(|id| !id.trim().is_empty())
                .ok_or_else(|| "Immich returned no asset ID.".to_string())?;
            let base_url = immich_base_url(&settings.immich_base_url)?;
            let url = append_url_path(&base_url, &format!("photos/{id}"));
            parse_plain_url("Immich", &url).map(|url| UploadSuccess { url, provider_name })
        }
        UploadDestination::CustomHttp => {
            let response_path = settings.custom_response_url_path.trim();
            if !response_path.is_empty() {
                if let Some(url) = json_path_string(body, response_path) {
                    return parse_plain_url("Custom", &url)
                        .map(|url| UploadSuccess { url, provider_name });
                }
            }

            let trimmed = body.trim();
            if trimmed.starts_with("https://") || trimmed.starts_with("http://") {
                return parse_plain_url("Custom", trimmed)
                    .map(|url| UploadSuccess { url, provider_name });
            }

            if let Some(url) = first_http_url(trimmed) {
                return parse_plain_url("Custom", url)
                    .map(|url| UploadSuccess { url, provider_name });
            }

            Err(format!(
                "Upload returned: {}",
                trimmed.chars().take(180).collect::<String>()
            ))
        }
        _ => Err(format!(
            "{} upload is not implemented in the Rust curl backend yet.",
            provider_name
        )),
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

fn curl_file_form(field_name: &str, file_path: &Path) -> String {
    format!("{field_name}=@{}", file_path.display())
}

fn curl_base_args() -> Vec<String> {
    vec![
        "--silent".into(),
        "--show-error".into(),
        "--location".into(),
        "--max-time".into(),
        "120".into(),
        "--write-out".into(),
        "\n%{http_code}".into(),
    ]
}

fn upload_file_name(file_path: &Path) -> Result<String, String> {
    file_path
        .file_name()
        .and_then(|name| name.to_str())
        .filter(|name| !name.trim().is_empty())
        .map(str::to_string)
        .ok_or_else(|| "upload file path must include a file name.".into())
}

fn append_url_path(base_url: &str, file_name: &str) -> String {
    append_url_path_segments(base_url, [file_name])
}

fn dropbox_json_request(token: &str, url: &str, body: String) -> CurlUploadRequest {
    CurlUploadRequest {
        destination: UploadDestination::Dropbox,
        provider_name: UploadDestination::Dropbox.display_name(),
        program: "curl".into(),
        args: curl_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "--header".into(),
                format!("Authorization: Bearer {token}"),
                "--header".into(),
                "Content-Type: application/json".into(),
                "--data-binary".into(),
                "@-".into(),
                url.into(),
            ])
            .collect(),
        stdin_body: Some(body.into_bytes()),
        success_url: None,
    }
}

fn dropbox_upload_path(file_path: &Path, settings: &UploadSettings) -> Result<String, String> {
    let file_name = upload_file_name(file_path)?;
    let prefix = settings.dropbox_path_prefix.trim().trim_matches('/');
    Ok(if prefix.is_empty() {
        format!("/{file_name}")
    } else {
        format!("/{prefix}/{file_name}")
    })
}

fn onedrive_upload_url(file_path: &Path, settings: &UploadSettings) -> Result<String, String> {
    let file_name = upload_file_name(file_path)?;
    let folder = settings.one_drive_folder.trim().trim_matches('/');
    let path = if folder.is_empty() {
        percent_encode_path_component(&file_name)
    } else {
        folder
            .split('/')
            .chain([file_name.as_str()])
            .filter(|segment| !segment.is_empty())
            .map(percent_encode_path_component)
            .collect::<Vec<_>>()
            .join("/")
    };
    Ok(format!(
        "https://graph.microsoft.com/v1.0/me/drive/root:/{path}:/content"
    ))
}

fn google_drive_multipart_body(
    file_path: &Path,
    settings: &UploadSettings,
    boundary: &str,
) -> Result<Vec<u8>, String> {
    let file_body = std::fs::read(file_path)
        .map_err(|error| format!("Google Drive upload could not read file: {error}"))?;
    let metadata = google_drive_metadata(file_path, settings)?;

    let mut body = Vec::new();
    body.extend_from_slice(format!("--{boundary}\r\n").as_bytes());
    body.extend_from_slice(b"Content-Type: application/json; charset=UTF-8\r\n\r\n");
    body.extend_from_slice(metadata.to_string().as_bytes());
    body.extend_from_slice(b"\r\n");
    body.extend_from_slice(format!("--{boundary}\r\n").as_bytes());
    body.extend_from_slice(
        format!("Content-Type: {}\r\n\r\n", upload_content_type(file_path)).as_bytes(),
    );
    body.extend_from_slice(&file_body);
    body.extend_from_slice(b"\r\n");
    body.extend_from_slice(format!("--{boundary}--\r\n").as_bytes());
    Ok(body)
}

fn google_drive_metadata(file_path: &Path, settings: &UploadSettings) -> Result<Value, String> {
    let file_name = upload_file_name(file_path)?;
    let mut metadata = serde_json::json!({ "name": file_name });
    let folder = settings.google_drive_folder_id.trim();
    if !folder.is_empty() {
        metadata["parents"] = serde_json::json!([folder]);
    }
    Ok(metadata)
}

fn google_drive_file_url(file_id: &str) -> String {
    format!(
        "https://drive.google.com/file/d/{}/view",
        percent_encode_path_component(file_id.trim())
    )
}

fn google_drive_location_header(output_with_headers: &str) -> Option<String> {
    output_with_headers.lines().find_map(|line| {
        let (name, value) = line.split_once(':')?;
        if name.trim().eq_ignore_ascii_case("location") {
            let value = value.trim();
            if value.starts_with("https://") || value.starts_with("http://") {
                Some(value.into())
            } else {
                None
            }
        } else {
            None
        }
    })
}

fn upload_content_type(file_path: &Path) -> &'static str {
    match file_path
        .extension()
        .and_then(|extension| extension.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase()
        .as_str()
    {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        "mp4" => "video/mp4",
        "webm" => "video/webm",
        "mkv" => "video/x-matroska",
        _ => "application/octet-stream",
    }
}

fn append_url_path_segments<'a>(
    base_url: &str,
    segments: impl IntoIterator<Item = &'a str>,
) -> String {
    let encoded_path = segments
        .into_iter()
        .flat_map(|segment| segment.split('/'))
        .filter(|segment| !segment.is_empty())
        .map(percent_encode_path_component)
        .collect::<Vec<_>>()
        .join("/");
    format!("{}/{}", base_url.trim_end_matches('/'), encoded_path)
}

fn azure_blob_urls(sas_base_url: &str, file_name: &str) -> Result<(String, String), String> {
    let trimmed = sas_base_url.trim();
    if trimmed.is_empty() {
        return Err(missing_upload_setting("Azure Blob SAS URL"));
    }
    if !is_https_url(trimmed) {
        return Err("Azure Blob uploads require an HTTPS SAS URL.".into());
    }

    let (base, query) = trimmed
        .split_once('?')
        .map_or((trimmed, ""), |(base, query)| (base, query));
    let public_url = append_url_path(base, file_name);
    let upload_url = if query.is_empty() {
        public_url.clone()
    } else {
        format!("{public_url}?{query}")
    };
    Ok((upload_url, public_url))
}

fn ftp_upload_url(base_url: &str, file_name: &str) -> Result<String, String> {
    let mut normalized = base_url.trim().to_string();
    if normalized.is_empty() {
        return Err(missing_upload_setting("FTP URL"));
    }
    if !normalized.contains("://") {
        normalized = format!("ftp://{normalized}");
    }
    let lower = normalized.to_ascii_lowercase();
    if !(lower.starts_with("ftp://") || lower.starts_with("ftps://")) {
        return Err("FTP URL must be a valid ftp:// or ftps:// address.".into());
    }
    Ok(append_url_path(&normalized, file_name))
}

fn sftp_upload_url(settings: &UploadSettings, file_name: &str) -> Result<String, String> {
    let host = settings
        .sftp_host
        .trim()
        .trim_start_matches("sftp://")
        .trim_matches('/');
    if host.is_empty() {
        return Err(missing_upload_setting("SFTP host"));
    }

    let port = if settings.sftp_port == 0 {
        22
    } else {
        settings.sftp_port
    };
    let base = format!("sftp://{host}:{port}");
    let remote_path = settings.sftp_remote_path.trim().trim_matches('/');
    if remote_path.is_empty() {
        Ok(append_url_path(&base, file_name))
    } else {
        Ok(append_url_path_segments(&base, [remote_path, file_name]))
    }
}

fn s3_endpoint_url(endpoint: &str) -> Result<String, String> {
    let mut normalized = endpoint.trim().trim_end_matches('/').to_string();
    if normalized.is_empty() {
        return Err(missing_upload_setting("S3 endpoint"));
    }
    if !normalized.contains("://") {
        normalized = format!("https://{normalized}");
    }
    if !is_https_url(&normalized) {
        return Err("S3 uploads require an HTTPS endpoint.".into());
    }
    Ok(normalized)
}

fn s3_object_key(file_path: &Path, settings: &UploadSettings) -> Result<String, String> {
    let file_name = upload_file_name(file_path)?;
    let prefix = settings.s3_path_prefix.trim().trim_matches('/');
    Ok(if prefix.is_empty() {
        format!("oddsnap/{file_name}")
    } else {
        format!("{prefix}/oddsnap/{file_name}")
    })
}

fn github_repo(value: &str) -> Result<String, String> {
    let repo = value.trim().trim_matches('/');
    if repo.is_empty() {
        return Err(missing_upload_setting("GitHub repo"));
    }
    if repo.split('/').count() != 2 {
        return Err("GitHub repo must use owner/name format.".into());
    }
    Ok(repo.into())
}

fn github_branch(value: &str) -> &str {
    let branch = value.trim();
    if branch.is_empty() {
        "main"
    } else {
        branch
    }
}

fn github_upload_path(file_path: &Path, settings: &UploadSettings) -> Result<String, String> {
    let file_name = upload_file_name(file_path)?;
    let prefix = settings.github_path_prefix.trim().trim_matches('/');
    Ok(if prefix.is_empty() {
        file_name
    } else {
        format!("{prefix}/{file_name}")
    })
}

fn github_contents_url(repo: &str, path: &str) -> String {
    append_url_path_segments(
        &format!("https://api.github.com/repos/{repo}/contents"),
        [path],
    )
}

fn github_raw_url(repo: &str, branch: &str, path: &str) -> String {
    append_url_path_segments(
        &format!("https://raw.githubusercontent.com/{repo}/{branch}"),
        [path],
    )
}

fn immich_base_url(value: &str) -> Result<String, String> {
    let base_url = value.trim().trim_end_matches('/');
    if base_url.is_empty() {
        return Err(missing_upload_setting("Immich base URL"));
    }
    if !(base_url.starts_with("https://") || base_url.starts_with("http://")) {
        return Err("Immich base URL must start with http:// or https://.".into());
    }
    Ok(base_url.into())
}

fn immich_assets_url(value: &str) -> Result<String, String> {
    Ok(format!("{}/api/assets", immich_base_url(value)?))
}

fn host_device_name() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .map(|name| sanitize_device_asset_id_part(&name))
        .ok()
        .filter(|name| !name.is_empty())
        .unwrap_or_else(|| "oddsnap-rust".into())
}

fn sanitize_device_asset_id_part(value: &str) -> String {
    value
        .chars()
        .map(|character| {
            if character.is_ascii_alphanumeric() || matches!(character, '-' | '_' | '.') {
                character
            } else {
                '_'
            }
        })
        .collect()
}

fn system_time_unix_seconds(value: SystemTime) -> u64 {
    value
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs())
        .unwrap_or(0)
}

fn system_time_to_rfc3339(value: SystemTime) -> String {
    let total_seconds = system_time_unix_seconds(value) as i64;
    let days = total_seconds.div_euclid(86_400);
    let seconds_of_day = total_seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let hour = seconds_of_day / 3_600;
    let minute = (seconds_of_day % 3_600) / 60;
    let second = seconds_of_day % 60;
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

fn civil_from_days(days_since_unix_epoch: i64) -> (i64, u32, u32) {
    let z = days_since_unix_epoch + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let day_of_era = z - era * 146_097;
    let year_of_era =
        (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let mut year = year_of_era + era * 400;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_prime = (5 * day_of_year + 2) / 153;
    let day = day_of_year - (153 * month_prime + 2) / 5 + 1;
    let month = month_prime + if month_prime < 10 { 3 } else { -9 };
    year += if month <= 2 { 1 } else { 0 };
    (year, month as u32, day as u32)
}

fn percent_encode_path_component(value: &str) -> String {
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

fn hex_sha256_fingerprint_to_base64(value: &str) -> Result<String, String> {
    let hex = value
        .chars()
        .filter(|character| character.is_ascii_hexdigit())
        .collect::<String>();
    if hex.len() != 64 {
        return Err(
            "SFTP host key fingerprint not configured or invalid (expected 64 hex chars.).".into(),
        );
    }

    let mut bytes = Vec::with_capacity(32);
    for index in (0..hex.len()).step_by(2) {
        let byte = u8::from_str_radix(&hex[index..index + 2], 16).map_err(|_| {
            "SFTP host key fingerprint not configured or invalid (expected 64 hex chars.)."
                .to_string()
        })?;
        bytes.push(byte);
    }
    Ok(base64_encode(&bytes))
}

fn base64_encode(bytes: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut encoded = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let first = chunk[0];
        let second = *chunk.get(1).unwrap_or(&0);
        let third = *chunk.get(2).unwrap_or(&0);
        let combined = ((first as u32) << 16) | ((second as u32) << 8) | third as u32;

        encoded.push(ALPHABET[((combined >> 18) & 0x3f) as usize] as char);
        encoded.push(ALPHABET[((combined >> 12) & 0x3f) as usize] as char);
        encoded.push(if chunk.len() >= 2 {
            ALPHABET[((combined >> 6) & 0x3f) as usize] as char
        } else {
            '='
        });
        encoded.push(if chunk.len() == 3 {
            ALPHABET[(combined & 0x3f) as usize] as char
        } else {
            '='
        });
    }
    encoded
}

fn split_curl_body_and_status(output: &str) -> Result<(&str, u16), String> {
    let trimmed = output.trim_end();
    let Some((body, status)) = trimmed.rsplit_once('\n') else {
        return Err("curl upload output did not include an HTTP status code.".into());
    };
    if status.len() != 3 || !status.chars().all(|character| character.is_ascii_digit()) {
        return Err("curl upload output ended without a valid HTTP status code.".into());
    }
    let status_code = status
        .parse::<u16>()
        .map_err(|error| format!("curl upload status was not numeric: {error}"))?;
    Ok((body.trim(), status_code))
}

fn parse_plain_url(provider_name: &str, value: &str) -> Result<String, String> {
    let url = value.trim();
    if url.starts_with("https://") || url.starts_with("http://") {
        Ok(url.into())
    } else if url.is_empty() {
        Err(format!("{provider_name} did not return a link."))
    } else {
        Err(format!("{provider_name} error: {url}"))
    }
}

fn parse_dropbox_shared_link_body(body: &str) -> Result<UploadSuccess, String> {
    let node: Value = serde_json::from_str(body)
        .map_err(|error| format!("Dropbox returned invalid JSON: {error}"))?;
    let url = node
        .get("url")
        .and_then(Value::as_str)
        .ok_or_else(|| "Dropbox returned no shared link.".to_string())?;
    dropbox_upload_success(url)
}

fn dropbox_upload_success(url: &str) -> Result<UploadSuccess, String> {
    parse_plain_url("Dropbox", &dropbox_raw_url(url)).map(|url| UploadSuccess {
        url,
        provider_name: UploadDestination::Dropbox.display_name(),
    })
}

fn dropbox_raw_url(url: &str) -> String {
    let trimmed = url.trim();
    if let Some(prefix) = trimmed.strip_suffix("?dl=0") {
        format!("{prefix}?raw=1")
    } else if let Some(prefix) = trimmed.strip_suffix("&dl=0") {
        format!("{prefix}&raw=1")
    } else {
        trimmed.into()
    }
}

fn tmpfiles_download_url(value: &str) -> Option<String> {
    let url = value.trim();
    let prefix = "https://tmpfiles.org/";
    let path = url.strip_prefix(prefix)?;
    if path.is_empty() {
        None
    } else if path.starts_with("dl/") {
        Some(format!("{prefix}{path}"))
    } else {
        Some(format!("{prefix}dl/{path}"))
    }
}

fn parse_custom_upload_header(line: &str) -> Result<(&str, &str), String> {
    if line
        .chars()
        .any(|character| character == '\r' || character == '\n' || character.is_control())
    {
        return Err("Custom upload headers cannot contain control characters.".into());
    }

    let Some((name, value)) = line.split_once(':') else {
        return Err("Custom upload headers must use Name: Value format.".into());
    };
    let name = name.trim();
    let value = value.trim();
    if name.is_empty() {
        return Err("Custom upload headers must use Name: Value format.".into());
    }
    if !name
        .chars()
        .all(|character| character.is_ascii_alphanumeric() || "!#$%&'*+-.^_`|~".contains(character))
    {
        return Err(format!(
            "Custom upload header '{name}' has an invalid name."
        ));
    }
    if forbidden_custom_upload_header(name) {
        return Err(format!(
            "Custom upload header '{name}' is managed by OddSnap and cannot be overridden."
        ));
    }
    Ok((name, value))
}

fn forbidden_custom_upload_header(name: &str) -> bool {
    matches!(
        normalized_key(name).as_str(),
        "connection"
            | "contentlength"
            | "contenttype"
            | "expect"
            | "host"
            | "keepalive"
            | "proxyconnection"
            | "te"
            | "trailer"
            | "transferencoding"
            | "upgrade"
    )
}

fn json_path_string(body: &str, path: &str) -> Option<String> {
    let node: Value = serde_json::from_str(body).ok()?;
    let mut current = &node;
    for part in path.split('.').filter(|part| !part.is_empty()) {
        current = current.get(part)?;
    }
    current.as_str().map(str::to_string)
}

fn first_http_url(value: &str) -> Option<&str> {
    let start = value.find("https://").or_else(|| value.find("http://"))?;
    let candidate = &value[start..];
    let end = candidate
        .char_indices()
        .find_map(|(index, character)| {
            if character.is_whitespace() || matches!(character, '"' | '\'' | '}' | ']') {
                Some(index)
            } else {
                None
            }
        })
        .unwrap_or(candidate.len());
    Some(candidate[..end].trim_end_matches([',', '.', ';', ':']))
}

fn json_error_message(provider_name: &str, node: &Value) -> String {
    node.get("error")
        .and_then(|error| {
            error
                .get("message")
                .and_then(Value::as_str)
                .or_else(|| error.as_str())
        })
        .or_else(|| node.get("message").and_then(Value::as_str))
        .or_else(|| {
            node.get("data")
                .and_then(|data| data.get("error"))
                .and_then(Value::as_str)
        })
        .map(str::to_string)
        .unwrap_or_else(|| format!("{provider_name} did not return a successful upload response."))
}

fn build_upload_error(provider_name: &str, status_code: u16, body: &str) -> String {
    if status_code == 429 {
        return format!("{provider_name} rate limit reached");
    }

    let trimmed = body.trim();
    if trimmed.is_empty() {
        format!("{provider_name} upload failed with HTTP {status_code}")
    } else if trimmed.starts_with('<') {
        format!("{provider_name} returned an HTML error page (HTTP {status_code})")
    } else {
        let shortened: String = trimmed.chars().take(180).collect();
        if shortened.len() == trimmed.len() {
            trimmed.into()
        } else {
            format!("{shortened}...")
        }
    }
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
        assert_eq!(
            UploadDestination::TransferSh.configuration_error(&settings),
            Some("The public transfer.sh service is unavailable. Choose Temp Hosts, Catbox, Litterbox, Uguu, or file.io.".into())
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
    fn explicit_upload_preflight_ignores_auto_upload_toggles() {
        let file = PathBuf::from("capture.png");
        let settings = AppSettings {
            image_upload_destination: "Catbox".into(),
            auto_upload_screenshots: false,
            ..AppSettings::default()
        };

        assert_eq!(
            upload_preflight_for_media(&settings, HistoryKind::Image, &file, false),
            UploadPreflight::Disabled
        );
        assert!(matches!(
            upload_preflight_for_explicit_media(&settings, HistoryKind::Image, &file, false),
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

    #[test]
    fn builds_curl_upload_requests_for_public_hosts() {
        let path = PathBuf::from("capture.png");
        let catbox = build_curl_upload_request(UploadDestination::Catbox, &path)
            .expect("build catbox request");

        assert_eq!(catbox.program, "curl");
        assert!(catbox.args.contains(&"reqtype=fileupload".into()));
        assert!(catbox.args.contains(&"fileToUpload=@capture.png".into()));
        assert!(catbox
            .args
            .contains(&"https://catbox.moe/user/api.php".into()));

        let file_io =
            build_curl_upload_request(UploadDestination::FileIo, &path).expect("build file.io");
        assert!(file_io.args.contains(&"file=@capture.png".into()));
        assert!(file_io.args.contains(&"https://file.io".into()));

        let imgur = build_curl_upload_request_with_settings(
            UploadDestination::Imgur,
            &path,
            &UploadSettings {
                imgur_client_id: "client".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build imgur");
        assert!(imgur
            .args
            .contains(&"Authorization: Client-ID client".into()));
        assert!(imgur.args.contains(&"https://api.imgur.com/3/image".into()));

        let imgbb = build_curl_upload_request_with_settings(
            UploadDestination::ImgBb,
            &path,
            &UploadSettings {
                imgbb_api_key: "key".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build imgbb");
        assert!(imgbb.args.contains(&"image=@capture.png".into()));
        assert!(imgbb
            .args
            .contains(&"https://api.imgbb.com/1/upload?key=key".into()));

        let gyazo = build_curl_upload_request_with_settings(
            UploadDestination::Gyazo,
            &path,
            &UploadSettings {
                gyazo_access_token: "token".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build gyazo");
        assert!(gyazo.args.contains(&"access_token=token".into()));
        assert!(gyazo.args.contains(&"imagedata=@capture.png".into()));
        assert!(gyazo
            .args
            .contains(&"https://upload.gyazo.com/api/upload".into()));

        let imgpile = build_curl_upload_request_with_settings(
            UploadDestination::ImgPile,
            &path,
            &UploadSettings {
                imgpile_api_token: "pile-token".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build imgpile");
        assert!(imgpile
            .args
            .contains(&"Authorization: Bearer pile-token".into()));
        assert!(imgpile.args.contains(&"file=@capture.png".into()));
        assert!(imgpile
            .args
            .contains(&"https://cdn.imgpile.com/api/v1/media".into()));

        let custom = build_curl_upload_request_with_settings(
            UploadDestination::CustomHttp,
            &path,
            &UploadSettings {
                custom_upload_url: "https://upload.example.test".into(),
                custom_file_form_name: "media".into(),
                custom_headers: "X-Api-Key: secret\nX-Mode: test".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build custom upload");
        assert!(custom.args.contains(&"X-Api-Key: secret".into()));
        assert!(custom.args.contains(&"X-Mode: test".into()));
        assert!(custom.args.contains(&"media=@capture.png".into()));
        assert!(custom.args.contains(&"https://upload.example.test".into()));

        assert!(build_curl_upload_request(UploadDestination::Imgur, &path).is_err());
        assert!(build_curl_upload_request(UploadDestination::Gyazo, &path).is_err());
        assert!(build_curl_upload_request(UploadDestination::ImgPile, &path).is_err());
        assert!(build_curl_upload_request_with_settings(
            UploadDestination::CustomHttp,
            &path,
            &UploadSettings {
                custom_upload_url: "https://upload.example.test".into(),
                custom_headers: "Content-Type: image/png".into(),
                ..UploadSettings::default()
            },
        )
        .is_err());

        let webdav = build_curl_upload_request_with_settings(
            UploadDestination::WebDav,
            &path,
            &UploadSettings {
                web_dav_url: "https://dav.example.test/uploads".into(),
                web_dav_username: "user".into(),
                web_dav_password: "pass".into(),
                web_dav_public_url: "https://cdn.example.test/uploads".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build webdav");
        assert!(webdav.args.contains(&"PUT".into()));
        assert!(webdav.args.contains(&"user:pass".into()));
        assert!(webdav.args.contains(&"capture.png".into()));
        assert!(webdav
            .args
            .contains(&"https://dav.example.test/uploads/capture.png".into()));
        assert_eq!(
            webdav.success_url.as_deref(),
            Some("https://cdn.example.test/uploads/capture.png")
        );

        let azure = build_curl_upload_request_with_settings(
            UploadDestination::AzureBlob,
            &path,
            &UploadSettings {
                azure_blob_sas_url: "https://blob.example.test/container?sig=secret".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build azure");
        assert!(azure.args.contains(&"x-ms-blob-type: BlockBlob".into()));
        assert!(azure
            .args
            .contains(&"https://blob.example.test/container/capture.png?sig=secret".into()));
        assert_eq!(
            azure.success_url.as_deref(),
            Some("https://blob.example.test/container/capture.png")
        );

        let ftp = build_curl_upload_request_with_settings(
            UploadDestination::Ftp,
            &path,
            &UploadSettings {
                ftp_url: "ftps://ftp.example.test/uploads".into(),
                ftp_username: "user".into(),
                ftp_password: "pass".into(),
                ftp_public_url: "https://cdn.example.test/uploads".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build ftp");
        assert!(ftp.args.contains(&"user:pass".into()));
        assert!(ftp.args.contains(&"capture.png".into()));
        assert!(ftp
            .args
            .contains(&"ftps://ftp.example.test/uploads/capture.png".into()));
        assert_eq!(
            ftp.success_url.as_deref(),
            Some("https://cdn.example.test/uploads/capture.png")
        );

        let s3 = build_curl_upload_request_with_settings(
            UploadDestination::S3Compatible,
            &path,
            &UploadSettings {
                s3_endpoint: "https://s3.example.test".into(),
                s3_bucket: "bucket".into(),
                s3_region: "auto".into(),
                s3_access_key: "access".into(),
                s3_secret_key: "secret".into(),
                s3_path_prefix: "captures".into(),
                s3_public_url: "https://cdn.example.test".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build s3");
        assert!(s3.args.contains(&"aws:amz:auto:s3".into()));
        assert!(s3.args.contains(&"access:secret".into()));
        assert!(s3
            .args
            .contains(&"https://s3.example.test/bucket/captures/oddsnap/capture.png".into()));
        assert_eq!(
            s3.success_url.as_deref(),
            Some("https://cdn.example.test/captures/oddsnap/capture.png")
        );

        let sftp = build_curl_upload_request_with_settings(
            UploadDestination::Sftp,
            &path,
            &UploadSettings {
                sftp_host: "sftp.example.test".into(),
                sftp_port: 2222,
                sftp_username: "user".into(),
                sftp_password: "pass".into(),
                sftp_remote_path: "/uploads".into(),
                sftp_public_url: "https://cdn.example.test/uploads".into(),
                sftp_host_key_fingerprint:
                    "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build sftp");
        assert!(sftp
            .args
            .contains(&"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=".into()));
        assert!(sftp.args.contains(&"user:pass".into()));
        assert!(sftp
            .args
            .contains(&"sftp://sftp.example.test:2222/uploads/capture.png".into()));
        assert_eq!(
            sftp.success_url.as_deref(),
            Some("https://cdn.example.test/uploads/capture.png")
        );
    }

    #[test]
    fn builds_github_curl_upload_request_with_json_stdin() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-github-upload-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("capture.png");
        fs::write(&path, b"png").expect("write upload file");

        let request = build_curl_upload_request_with_settings(
            UploadDestination::GitHub,
            &path,
            &UploadSettings {
                github_token: "ghp_secret".into(),
                github_repo: "owner/repo".into(),
                github_branch: "trunk".into(),
                github_path_prefix: "captures".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build github");

        assert!(request.args.contains(&"PUT".into()));
        assert!(request
            .args
            .contains(&"Authorization: Bearer ghp_secret".into()));
        assert!(request.args.contains(&"--data-binary".into()));
        assert!(request.args.contains(&"@-".into()));
        assert!(request.args.contains(
            &"https://api.github.com/repos/owner/repo/contents/captures/capture.png".into()
        ));
        assert_eq!(
            request.success_url.as_deref(),
            Some("https://raw.githubusercontent.com/owner/repo/trunk/captures/capture.png")
        );

        let body = String::from_utf8(request.stdin_body.expect("json body")).expect("utf8 body");
        assert!(body.contains("\"branch\":\"trunk\""));
        assert!(body.contains("\"content\":\"cG5n\""));
        assert!(body.contains("\"message\":\"Add OddSnap upload capture.png\""));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_immich_curl_upload_request_with_metadata_fields() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-immich-upload-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("capture.png");
        fs::write(&path, b"png").expect("write upload file");

        let request = build_curl_upload_request_with_settings(
            UploadDestination::Immich,
            &path,
            &UploadSettings {
                immich_base_url: "https://immich.example.test/".into(),
                immich_api_key: "immich-key".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build immich");

        assert!(request.args.contains(&"x-api-key: immich-key".into()));
        assert!(request.args.contains(&"deviceId=OddSnap".into()));
        assert!(request.args.contains(&"filename=capture.png".into()));
        assert!(request
            .args
            .contains(&format!("assetData=@{}", path.display())));
        assert!(request
            .args
            .contains(&"https://immich.example.test/api/assets".into()));
        assert_eq!(request.stdin_body, None);

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_dropbox_curl_upload_plan_with_link_fallback_request() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-dropbox-upload-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("capture.png");
        fs::write(&path, b"png").expect("write upload file");

        let plan = build_dropbox_curl_upload_plan(
            &path,
            &UploadSettings {
                dropbox_access_token: "dropbox-token".into(),
                dropbox_path_prefix: "OddSnap/Captures".into(),
                ..UploadSettings::default()
            },
        )
        .expect("build dropbox plan");

        assert!(plan
            .upload
            .args
            .contains(&"Authorization: Bearer dropbox-token".into()));
        assert!(plan
            .upload
            .args
            .iter()
            .any(|arg| arg.contains("\"path\":\"/OddSnap/Captures/capture.png\"")));
        assert_eq!(plan.upload.stdin_body.as_deref(), Some(b"png".as_slice()));
        assert!(plan.create_shared_link.args.contains(
            &"https://api.dropboxapi.com/2/sharing/create_shared_link_with_settings".into()
        ));
        assert!(plan
            .list_shared_links
            .args
            .contains(&"https://api.dropboxapi.com/2/sharing/list_shared_links".into()));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_onedrive_curl_upload_plan_and_create_link_request() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-onedrive-upload-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("capture file.png");
        fs::write(&path, b"png").expect("write upload file");

        let settings = UploadSettings {
            one_drive_access_token: "onedrive-token".into(),
            one_drive_folder: "OddSnap/Captures".into(),
            ..UploadSettings::default()
        };
        let plan = build_onedrive_curl_upload_plan(&path, &settings).expect("build onedrive plan");

        assert!(plan
            .upload
            .args
            .contains(&"Authorization: Bearer onedrive-token".into()));
        assert!(plan.upload.args.contains(
            &"https://graph.microsoft.com/v1.0/me/drive/root:/OddSnap/Captures/capture%20file.png:/content".into()
        ));
        assert_eq!(plan.upload.stdin_body.as_deref(), Some(b"png".as_slice()));

        let create_link =
            build_onedrive_create_link_request("item id", &settings).expect("create link request");
        assert!(create_link.args.contains(
            &"https://graph.microsoft.com/v1.0/me/drive/items/item%20id/createLink".into()
        ));
        let body = String::from_utf8(create_link.stdin_body.expect("link body")).expect("utf8");
        assert!(body.contains("\"type\":\"view\""));
        assert!(body.contains("\"scope\":\"anonymous\""));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_google_drive_multipart_upload_plan_and_permission_request() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-drive-upload-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("capture.png");
        fs::write(&path, b"png").expect("write upload file");

        let settings = UploadSettings {
            google_drive_access_token: "drive-token".into(),
            google_drive_folder_id: "folder-id".into(),
            ..UploadSettings::default()
        };
        let plan = build_google_drive_curl_upload_plan(&path, &settings).expect("build drive plan");

        assert_eq!(plan.kind, GoogleDriveUploadPlanKind::Multipart);
        assert!(plan
            .upload
            .args
            .contains(&"Authorization: Bearer drive-token".into()));
        assert!(plan.upload.args.iter().any(|arg| {
            arg.starts_with("Content-Type: multipart/related; boundary=oddsnap-rust-drive-boundary")
        }));
        assert!(plan.upload.args.contains(&"https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink,webContentLink".into()));
        let body = String::from_utf8(plan.upload.stdin_body.expect("multipart body"))
            .expect("utf8 multipart body");
        assert!(body.contains(r#""name":"capture.png""#));
        assert!(body.contains(r#""parents":["folder-id"]"#));
        assert!(body.contains("Content-Type: image/png"));

        let permission =
            build_google_drive_permission_request("file id", &settings).expect("permission");
        assert!(permission
            .args
            .contains(&"https://www.googleapis.com/drive/v3/files/file%20id/permissions".into()));
        let body = String::from_utf8(permission.stdin_body.expect("permission body"))
            .expect("utf8 permission body");
        assert!(body.contains("\"role\":\"reader\""));
        assert!(body.contains("\"type\":\"anyone\""));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_google_drive_resumable_upload_plan_for_large_files() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-drive-resumable-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path = root.join("large.mp4");
        fs::write(&path, vec![0_u8; (5 * MIB + 1) as usize]).expect("write upload file");

        let settings = UploadSettings {
            google_drive_access_token: "drive-token".into(),
            ..UploadSettings::default()
        };
        let plan = build_google_drive_curl_upload_plan(&path, &settings)
            .expect("build resumable drive plan");

        assert_eq!(plan.kind, GoogleDriveUploadPlanKind::Resumable);
        assert!(plan.upload.args.contains(&"--dump-header".into()));
        assert!(plan
            .upload
            .args
            .contains(&"X-Upload-Content-Type: video/mp4".into()));
        assert!(plan
            .upload
            .args
            .contains(&format!("X-Upload-Content-Length: {}", 5 * MIB + 1)));
        assert!(plan.upload.args.contains(
            &"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id"
                .into()
        ));

        let resumable = build_google_drive_resumable_upload_request(
            "https://upload.example.test/session",
            &path,
        )
        .expect("build upload request");
        assert!(resumable.args.contains(&"PUT".into()));
        assert!(resumable.args.contains(&"Content-Type: video/mp4".into()));
        assert!(resumable
            .args
            .contains(&"https://upload.example.test/session".into()));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn parses_upload_responses_for_public_hosts() {
        assert_eq!(
            parse_upload_response(
                UploadDestination::Imgur,
                r#"{"success":true,"data":{"link":"https://i.imgur.com/a.png"}}"#,
            )
            .expect("parse imgur")
            .url,
            "https://i.imgur.com/a.png"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::ImgBb,
                r#"{"success":true,"data":{"url":"https://i.ibb.co/a.png"}}"#,
            )
            .expect("parse imgbb")
            .url,
            "https://i.ibb.co/a.png"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::Gyazo,
                r#"{"permalink_url":"https://gyazo.com/abc"}"#,
            )
            .expect("parse gyazo")
            .url,
            "https://gyazo.com/abc"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::ImgPile,
                r#"{"media":{"urls":{"original":"https://cdn.imgpile.com/a.png"}}}"#,
            )
            .expect("parse imgpile")
            .url,
            "https://cdn.imgpile.com/a.png"
        );

        assert_eq!(
            parse_upload_response_with_settings(
                UploadDestination::CustomHttp,
                r#"{"data":{"url":"https://cdn.example.test/a.png"}}"#,
                &UploadSettings {
                    custom_response_url_path: "data.url".into(),
                    ..UploadSettings::default()
                },
            )
            .expect("parse custom json response")
            .url,
            "https://cdn.example.test/a.png"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::CustomHttp,
                r#"{"ok":true,"url":"https://cdn.example.test/fallback.png"}"#,
            )
            .expect("parse custom fallback response")
            .url,
            "https://cdn.example.test/fallback.png"
        );

        assert_eq!(
            parse_upload_response_with_settings(
                UploadDestination::GitHub,
                r#"{"content":{"path":"captures/capture.png"}}"#,
                &UploadSettings {
                    github_repo: "owner/repo".into(),
                    github_branch: "trunk".into(),
                    ..UploadSettings::default()
                },
            )
            .expect("parse github fallback response")
            .url,
            "https://raw.githubusercontent.com/owner/repo/trunk/captures/capture.png"
        );

        assert_eq!(
            parse_upload_response_with_settings(
                UploadDestination::Immich,
                r#"{"id":"asset-id"}"#,
                &UploadSettings {
                    immich_base_url: "https://immich.example.test/".into(),
                    ..UploadSettings::default()
                },
            )
            .expect("parse immich response")
            .url,
            "https://immich.example.test/photos/asset-id"
        );

        assert_eq!(
            parse_dropbox_shared_link_output(
                r#"{"url":"https://www.dropbox.com/s/abc/capture.png?dl=0"}
200"#,
            )
            .expect("parse dropbox link")
            .url,
            "https://www.dropbox.com/s/abc/capture.png?raw=1"
        );
        assert_eq!(
            parse_dropbox_list_shared_links_output(
                r#"{"links":[{"url":"https://www.dropbox.com/s/abc/capture.png?dl=0"}]}
200"#,
            )
            .expect("parse dropbox existing link")
            .url,
            "https://www.dropbox.com/s/abc/capture.png?raw=1"
        );
        assert!(dropbox_shared_link_already_exists(
            r#"{"error":{".tag":"shared_link_already_exists"}}
409"#
        ));

        assert_eq!(
            parse_onedrive_upload_item_id(
                r#"{"id":"item-id"}
201"#,
            )
            .expect("parse onedrive item id"),
            "item-id"
        );
        assert_eq!(
            parse_onedrive_create_link_output(
                r#"{"link":{"webUrl":"https://1drv.ms/u/s!abc"}}
200"#,
            )
            .expect("parse onedrive link")
            .url,
            "https://1drv.ms/u/s!abc"
        );

        assert_eq!(
            parse_google_drive_upload_file_id(
                r#"{"id":"drive-file-id"}
200"#,
            )
            .expect("parse drive file id"),
            "drive-file-id"
        );
        assert_eq!(
            parse_google_drive_resumable_session_output(
                "HTTP/2 200\r\nLocation: https://upload.example.test/session\r\n\r\n\n200",
            )
            .expect("parse drive session location"),
            "https://upload.example.test/session"
        );
        assert_eq!(
            parse_google_drive_permission_output(
                r#"{"id":"permission-id"}
200"#,
                "drive-file-id",
            )
            .expect("parse drive permission")
            .url,
            "https://drive.google.com/file/d/drive-file-id/view"
        );

        assert_eq!(
            parse_upload_response(UploadDestination::Catbox, "https://files.catbox.moe/a.png")
                .expect("parse catbox")
                .url,
            "https://files.catbox.moe/a.png"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::FileIo,
                r#"{"success":true,"link":"https://file.io/abc"}"#,
            )
            .expect("parse file.io")
            .url,
            "https://file.io/abc"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::TmpFiles,
                r#"{"data":{"url":"https://tmpfiles.org/123/capture.png"}}"#,
            )
            .expect("parse tmpfiles")
            .url,
            "https://tmpfiles.org/dl/123/capture.png"
        );

        assert_eq!(
            parse_upload_response(
                UploadDestination::Gofile,
                r#"{"status":"ok","data":{"downloadPage":"https://gofile.io/d/abc"}}"#,
            )
            .expect("parse gofile")
            .url,
            "https://gofile.io/d/abc"
        );
    }

    #[test]
    fn parses_curl_upload_output_status_suffix() {
        let success = parse_curl_upload_output(
            UploadDestination::Uguu,
            "https://a.uguu.se/capture.png\n200",
        )
        .expect("parse curl output");
        assert_eq!(success.url, "https://a.uguu.se/capture.png");

        assert_eq!(
            parse_curl_upload_output(UploadDestination::Catbox, "rate limited\n429")
                .expect_err("status should fail"),
            "Catbox rate limit reached"
        );

        let success = parse_curl_upload_output_with_success_url(
            UploadDestination::WebDav,
            "\n201",
            &UploadSettings::default(),
            Some("https://cdn.example.test/capture.png"),
        )
        .expect("parse empty success response");
        assert_eq!(success.url, "https://cdn.example.test/capture.png");
    }
}
