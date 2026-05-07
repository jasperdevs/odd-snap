use std::path::Path;

use serde_json::Value;

use crate::upload::{CurlUploadRequest, UploadDestination};

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum StickerProvider {
    None,
    RemoveBg,
    Photoroom,
    LocalCpu,
    Unknown(String),
}

impl StickerProvider {
    pub fn label(&self) -> String {
        match self {
            Self::None => "None".into(),
            Self::RemoveBg => "Remove.bg".into(),
            Self::Photoroom => "Photoroom".into(),
            Self::LocalCpu => "Local".into(),
            Self::Unknown(value) => value.clone(),
        }
    }

    fn setting_name(&self) -> String {
        match self {
            Self::None => "None".into(),
            Self::RemoveBg => "RemoveBg".into(),
            Self::Photoroom => "Photoroom".into(),
            Self::LocalCpu => "LocalCpu".into(),
            Self::Unknown(value) => value.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StickerSettings {
    pub provider: StickerProvider,
    pub remove_bg_api_key: String,
    pub photoroom_api_key: String,
    pub local_engine: String,
    pub local_cpu_engine: String,
    pub local_gpu_engine: String,
    pub local_execution_provider: String,
    pub add_shadow: bool,
    pub add_stroke: bool,
}

impl Default for StickerSettings {
    fn default() -> Self {
        Self {
            provider: StickerProvider::LocalCpu,
            remove_bg_api_key: String::new(),
            photoroom_api_key: String::new(),
            local_engine: "U2Netp".into(),
            local_cpu_engine: "U2Netp".into(),
            local_gpu_engine: "BiRefNetLite".into(),
            local_execution_provider: "Cpu".into(),
            add_shadow: false,
            add_stroke: false,
        }
    }
}

impl StickerSettings {
    pub fn from_json_value(value: Option<&Value>) -> Self {
        let Some(value) = value else {
            return Self::default();
        };
        Self {
            provider: parse_sticker_provider(json_field(value, "Provider")),
            remove_bg_api_key: json_string(value, "RemoveBgApiKey"),
            photoroom_api_key: json_string(value, "PhotoroomApiKey"),
            local_engine: json_string(value, "LocalEngine").or_default_value("U2Netp"),
            local_cpu_engine: json_string(value, "LocalCpuEngine").or_default_value("U2Netp"),
            local_gpu_engine: json_string(value, "LocalGpuEngine").or_default_value("BiRefNetLite"),
            local_execution_provider: json_string(value, "LocalExecutionProvider")
                .or_default_value("Cpu"),
            add_shadow: json_bool(value, "AddShadow"),
            add_stroke: json_bool(value, "AddStroke"),
        }
    }

    pub fn to_json_value(&self) -> Value {
        serde_json::json!({
            "Provider": self.provider.setting_name(),
            "RemoveBgApiKey": self.remove_bg_api_key,
            "PhotoroomApiKey": self.photoroom_api_key,
            "LocalEngine": self.local_engine,
            "LocalCpuEngine": self.local_cpu_engine,
            "LocalGpuEngine": self.local_gpu_engine,
            "LocalExecutionProvider": self.local_execution_provider,
            "AddShadow": self.add_shadow,
            "AddStroke": self.add_stroke,
        })
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum UpscaleProvider {
    None,
    Local,
    DeepAiSuperResolution,
    DeepAiWaifu2x,
    Unknown(String),
}

impl UpscaleProvider {
    pub fn label(&self) -> String {
        match self {
            Self::None => "None".into(),
            Self::Local => "Local".into(),
            Self::DeepAiSuperResolution => "DeepAI Super Resolution".into(),
            Self::DeepAiWaifu2x => "DeepAI Waifu2x".into(),
            Self::Unknown(value) => value.clone(),
        }
    }

    fn setting_name(&self) -> String {
        match self {
            Self::None => "None".into(),
            Self::Local => "Local".into(),
            Self::DeepAiSuperResolution => "DeepAiSuperResolution".into(),
            Self::DeepAiWaifu2x => "DeepAiWaifu2x".into(),
            Self::Unknown(value) => value.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UpscaleSettings {
    pub provider: UpscaleProvider,
    pub deep_ai_api_key: String,
    pub local_engine: String,
    pub local_cpu_engine: String,
    pub local_gpu_engine: String,
    pub local_execution_provider: String,
    pub scale_factor: u32,
    pub show_preview_window: bool,
}

impl Default for UpscaleSettings {
    fn default() -> Self {
        Self {
            provider: UpscaleProvider::Local,
            deep_ai_api_key: String::new(),
            local_engine: "SwinIrRealWorld".into(),
            local_cpu_engine: "SwinIrRealWorld".into(),
            local_gpu_engine: "RealEsrganX4Plus".into(),
            local_execution_provider: "Cpu".into(),
            scale_factor: 4,
            show_preview_window: true,
        }
    }
}

impl UpscaleSettings {
    pub fn from_json_value(value: Option<&Value>) -> Self {
        let Some(value) = value else {
            return Self::default();
        };
        Self {
            provider: parse_upscale_provider(json_field(value, "Provider")),
            deep_ai_api_key: json_string(value, "DeepAiApiKey"),
            local_engine: json_string(value, "LocalEngine").or_default_value("SwinIrRealWorld"),
            local_cpu_engine: json_string(value, "LocalCpuEngine")
                .or_default_value("SwinIrRealWorld"),
            local_gpu_engine: json_string(value, "LocalGpuEngine")
                .or_default_value("RealEsrganX4Plus"),
            local_execution_provider: json_string(value, "LocalExecutionProvider")
                .or_default_value("Cpu"),
            scale_factor: json_u32(value, "ScaleFactor").unwrap_or(4),
            show_preview_window: json_bool_or(value, "ShowPreviewWindow", true),
        }
    }

    pub fn to_json_value(&self) -> Value {
        serde_json::json!({
            "Provider": self.provider.setting_name(),
            "DeepAiApiKey": self.deep_ai_api_key,
            "LocalEngine": self.local_engine,
            "LocalCpuEngine": self.local_cpu_engine,
            "LocalGpuEngine": self.local_gpu_engine,
            "LocalExecutionProvider": self.local_execution_provider,
            "ScaleFactor": self.scale_factor,
            "ShowPreviewWindow": self.show_preview_window,
        })
    }
}

trait StringDefault {
    fn or_default_value(self, value: &str) -> String;
}

impl StringDefault for String {
    fn or_default_value(self, value: &str) -> String {
        if self.trim().is_empty() {
            value.into()
        } else {
            self
        }
    }
}

pub fn build_sticker_api_curl_request(
    settings: &StickerSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<CurlUploadRequest, String> {
    match settings.provider {
        StickerProvider::RemoveBg => {
            if settings.remove_bg_api_key.trim().is_empty() {
                return Err("remove.bg API key not configured".into());
            }
            Ok(image_output_request(
                "Remove.bg",
                "https://api.remove.bg/v1.0/removebg",
                vec![
                    "--request".into(),
                    "POST".into(),
                    "-H".into(),
                    format!("X-Api-Key: {}", settings.remove_bg_api_key.trim()),
                    "-F".into(),
                    "size=auto".into(),
                    "-F".into(),
                    curl_file_form("image_file", input_path),
                ],
                output_path,
            ))
        }
        StickerProvider::Photoroom => {
            if settings.photoroom_api_key.trim().is_empty() {
                return Err("Photoroom API key not configured".into());
            }
            Ok(image_output_request(
                "Photoroom",
                "https://sdk.photoroom.com/v1/segment",
                vec![
                    "--request".into(),
                    "POST".into(),
                    "-H".into(),
                    format!("x-api-key: {}", settings.photoroom_api_key.trim()),
                    "-F".into(),
                    curl_file_form("image_file", input_path),
                ],
                output_path,
            ))
        }
        StickerProvider::LocalCpu => Err(format!(
            "Local sticker runtime is pending in the Rust port (engine {}, {}).",
            settings.local_engine, settings.local_execution_provider
        )),
        StickerProvider::None => Err("No sticker provider configured".into()),
        StickerProvider::Unknown(ref value) => Err(format!("Unknown sticker provider '{value}'.")),
    }
}

pub fn build_deepai_upscale_curl_request(
    settings: &UpscaleSettings,
    input_path: &Path,
) -> Result<CurlUploadRequest, String> {
    let (provider_name, endpoint) = match settings.provider {
        UpscaleProvider::DeepAiSuperResolution => (
            "DeepAI Super Resolution",
            "https://api.deepai.org/api/torch-srgan",
        ),
        UpscaleProvider::DeepAiWaifu2x => ("DeepAI Waifu2x", "https://api.deepai.org/api/waifu2x"),
        UpscaleProvider::Local => {
            return Err(format!(
                "Local upscale runtime is pending in the Rust port (engine {}, {}).",
                settings.local_engine, settings.local_execution_provider
            ));
        }
        UpscaleProvider::None => return Err("No upscale provider configured".into()),
        UpscaleProvider::Unknown(ref value) => {
            return Err(format!("Unknown upscale provider '{value}'."));
        }
    };
    if settings.deep_ai_api_key.trim().is_empty() {
        return Err(format!("{provider_name} API key not configured"));
    }

    Ok(CurlUploadRequest {
        destination: UploadDestination::CustomHttp,
        provider_name: provider_name.into(),
        program: "curl".into(),
        args: curl_process_base_args()
            .into_iter()
            .chain([
                "--request".into(),
                "POST".into(),
                "-H".into(),
                format!("api-key: {}", settings.deep_ai_api_key.trim()),
                "-F".into(),
                curl_file_form("image", input_path),
                endpoint.into(),
            ])
            .collect(),
        stdin_body: None,
        success_url: None,
    })
}

pub fn build_image_download_curl_request(
    provider_name: &str,
    url: &str,
    output_path: &Path,
) -> Result<CurlUploadRequest, String> {
    if !url.starts_with("https://") {
        return Err(format!("{provider_name} returned a non-HTTPS output URL"));
    }
    Ok(image_output_request(
        provider_name,
        url,
        Vec::new(),
        output_path,
    ))
}

pub fn parse_image_output_curl_status(
    provider_name: &str,
    stdout: &str,
    output_path: &Path,
) -> Result<(), String> {
    let status = curl_status_code(stdout)?;
    if (200..300).contains(&status) {
        let metadata = std::fs::metadata(output_path)
            .map_err(|error| format!("{provider_name} did not write an output image: {error}"))?;
        if metadata.len() == 0 {
            return Err(format!("{provider_name} returned an empty image"));
        }
        return Ok(());
    }

    let body = std::fs::read_to_string(output_path).unwrap_or_default();
    if status == 429 {
        return Err(format!("{provider_name} rate limit reached"));
    }
    if body.trim().is_empty() {
        Err(format!("{provider_name} error: HTTP {status}"))
    } else {
        Err(truncate_error(body.trim(), 180))
    }
}

pub fn parse_deepai_upscale_output(provider_name: &str, stdout: &str) -> Result<String, String> {
    let status = curl_status_code(stdout)?;
    let body = curl_body_without_status(stdout);
    if !(200..300).contains(&status) {
        return Err(if body.trim().is_empty() {
            format!("{provider_name} error: HTTP {status}")
        } else {
            truncate_error(body.trim(), 180)
        });
    }
    let value: Value = serde_json::from_str(body.trim())
        .map_err(|error| format!("{provider_name} returned invalid JSON: {error}"))?;
    value
        .get("output_url")
        .and_then(Value::as_str)
        .filter(|url| !url.trim().is_empty())
        .map(str::to_string)
        .ok_or_else(|| format!("{provider_name} did not return an output image URL"))
}

fn image_output_request(
    provider_name: &str,
    url: &str,
    provider_args: Vec<String>,
    output_path: &Path,
) -> CurlUploadRequest {
    CurlUploadRequest {
        destination: UploadDestination::CustomHttp,
        provider_name: provider_name.into(),
        program: "curl".into(),
        args: curl_process_base_args()
            .into_iter()
            .chain(provider_args)
            .chain([
                "--output".into(),
                output_path.to_string_lossy().into_owned(),
                "--write-out".into(),
                "\n%{http_code}".into(),
                url.into(),
            ])
            .collect(),
        stdin_body: None,
        success_url: None,
    }
}

fn curl_process_base_args() -> Vec<String> {
    vec![
        "--silent".into(),
        "--show-error".into(),
        "--location".into(),
    ]
}

fn curl_file_form(field_name: &str, path: &Path) -> String {
    format!("{field_name}=@{}", path.to_string_lossy())
}

fn curl_status_code(stdout: &str) -> Result<u16, String> {
    stdout
        .lines()
        .next_back()
        .unwrap_or_default()
        .trim()
        .parse::<u16>()
        .map_err(|_| "curl response did not include an HTTP status code".into())
}

fn curl_body_without_status(stdout: &str) -> &str {
    stdout.rsplit_once('\n').map_or(stdout, |(body, _)| body)
}

fn truncate_error(value: &str, max_chars: usize) -> String {
    value.chars().take(max_chars).collect()
}

fn parse_sticker_provider(value: Option<&Value>) -> StickerProvider {
    match legacy_enum_key(value).as_deref() {
        Some("0") | Some("none") => StickerProvider::None,
        Some("1") | Some("removebg") | Some("remove.bg") => StickerProvider::RemoveBg,
        Some("2") | Some("photoroom") => StickerProvider::Photoroom,
        Some("3") | Some("localcpu") | Some("local") => StickerProvider::LocalCpu,
        Some(value) => StickerProvider::Unknown(value.into()),
        None => StickerProvider::LocalCpu,
    }
}

fn parse_upscale_provider(value: Option<&Value>) -> UpscaleProvider {
    match legacy_enum_key(value).as_deref() {
        Some("0") | Some("none") => UpscaleProvider::None,
        Some("1") | Some("local") => UpscaleProvider::Local,
        Some("2") | Some("deepaisuperresolution") | Some("deepaisrgan") => {
            UpscaleProvider::DeepAiSuperResolution
        }
        Some("3") | Some("deepaiwaifu2x") | Some("waifu2x") => UpscaleProvider::DeepAiWaifu2x,
        Some(value) => UpscaleProvider::Unknown(value.into()),
        None => UpscaleProvider::Local,
    }
}

fn legacy_enum_key(value: Option<&Value>) -> Option<String> {
    match value? {
        Value::Number(number) => Some(number.to_string()),
        Value::String(text) => Some(
            text.chars()
                .filter(|ch| !matches!(ch, '_' | '-' | ' '))
                .collect::<String>()
                .to_ascii_lowercase(),
        ),
        _ => None,
    }
}

fn json_field<'a>(value: &'a Value, pascal_name: &str) -> Option<&'a Value> {
    let object = value.as_object()?;
    object.get(pascal_name).or_else(|| {
        let mut chars = pascal_name.chars();
        let camel = match chars.next() {
            Some(first) => first.to_ascii_lowercase().to_string() + chars.as_str(),
            None => return None,
        };
        object.get(&camel)
    })
}

fn json_string(value: &Value, key: &str) -> String {
    match json_field(value, key) {
        Some(Value::String(text)) => text.clone(),
        Some(Value::Number(number)) => number.to_string(),
        Some(Value::Bool(flag)) => flag.to_string(),
        _ => String::new(),
    }
}

fn json_bool(value: &Value, key: &str) -> bool {
    json_bool_or(value, key, false)
}

fn json_bool_or(value: &Value, key: &str, fallback: bool) -> bool {
    match json_field(value, key) {
        Some(Value::Bool(flag)) => *flag,
        Some(Value::Number(number)) => number.as_i64().is_some_and(|value| value != 0),
        Some(Value::String(text)) => text.eq_ignore_ascii_case("true") || text == "1",
        _ => fallback,
    }
}

fn json_u32(value: &Value, key: &str) -> Option<u32> {
    match json_field(value, key)? {
        Value::Number(number) => number.as_u64().and_then(|value| u32::try_from(value).ok()),
        Value::String(text) => text.trim().parse().ok(),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sticker_settings_parse_legacy_values() {
        let value = serde_json::json!({
            "Provider": 1,
            "RemoveBgApiKey": "remove-key",
            "LocalCpuEngine": "U2Net",
            "AddStroke": true
        });

        let settings = StickerSettings::from_json_value(Some(&value));

        assert_eq!(settings.provider, StickerProvider::RemoveBg);
        assert_eq!(settings.remove_bg_api_key, "remove-key");
        assert_eq!(settings.local_cpu_engine, "U2Net");
        assert!(settings.add_stroke);
        assert_eq!(settings.local_gpu_engine, "BiRefNetLite");
    }

    #[test]
    fn upscale_settings_parse_legacy_values() {
        let value = serde_json::json!({
            "Provider": "DeepAiWaifu2x",
            "DeepAiApiKey": "deep-key",
            "ScaleFactor": 2,
            "ShowPreviewWindow": false
        });

        let settings = UpscaleSettings::from_json_value(Some(&value));

        assert_eq!(settings.provider, UpscaleProvider::DeepAiWaifu2x);
        assert_eq!(settings.deep_ai_api_key, "deep-key");
        assert_eq!(settings.scale_factor, 2);
        assert!(!settings.show_preview_window);
    }

    #[test]
    fn sticker_settings_round_trip_rust_json_values() {
        let settings = StickerSettings {
            provider: StickerProvider::Photoroom,
            photoroom_api_key: "photo-key".into(),
            local_engine: "BiRefNetLite".into(),
            local_cpu_engine: "U2Net".into(),
            local_gpu_engine: "BiRefNetLite".into(),
            local_execution_provider: "Gpu".into(),
            add_shadow: true,
            add_stroke: true,
            ..StickerSettings::default()
        };

        let parsed = StickerSettings::from_json_value(Some(&settings.to_json_value()));

        assert_eq!(parsed, settings);
    }

    #[test]
    fn upscale_settings_round_trip_rust_json_values() {
        let settings = UpscaleSettings {
            provider: UpscaleProvider::DeepAiSuperResolution,
            deep_ai_api_key: "deep-key".into(),
            local_engine: "RealEsrganX4Plus".into(),
            local_cpu_engine: "SwinIrRealWorld".into(),
            local_gpu_engine: "RealEsrganX4Plus".into(),
            local_execution_provider: "Gpu".into(),
            scale_factor: 2,
            show_preview_window: false,
        };

        let parsed = UpscaleSettings::from_json_value(Some(&settings.to_json_value()));

        assert_eq!(parsed, settings);
    }

    #[test]
    fn sticker_requests_preserve_legacy_removebg_form() {
        let settings = StickerSettings {
            provider: StickerProvider::RemoveBg,
            remove_bg_api_key: "secret".into(),
            ..StickerSettings::default()
        };

        let request = build_sticker_api_curl_request(
            &settings,
            Path::new("capture.png"),
            Path::new("sticker.png"),
        )
        .expect("request");

        assert!(request.args.contains(&"X-Api-Key: secret".into()));
        assert!(request.args.contains(&"size=auto".into()));
        assert!(request.args.contains(&"image_file=@capture.png".into()));
        assert!(request.args.contains(&"sticker.png".into()));
    }

    #[test]
    fn deepai_response_extracts_output_url() {
        let output = r#"{"output_url":"https://cdn.example.test/up.png"}
200"#;

        assert_eq!(
            parse_deepai_upscale_output("DeepAI", output).expect("url"),
            "https://cdn.example.test/up.png"
        );
    }

    #[test]
    fn image_output_status_reports_remote_error_body() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-sticker-upscale-test-{}",
            std::process::id()
        ));
        std::fs::create_dir_all(&root).expect("root");
        let output = root.join("error.txt");
        std::fs::write(&output, "bad request").expect("write body");

        let error = parse_image_output_curl_status("Remove.bg", "\n400", &output)
            .expect_err("remote error");

        assert_eq!(error, "bad request");
        let _ = std::fs::remove_dir_all(root);
    }
}
