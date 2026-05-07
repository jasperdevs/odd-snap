#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CurlTranslationRequest {
    pub program: String,
    pub args: Vec<String>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum TranslationModel {
    Argos = 0,
    Google = 1,
    #[default]
    OpenSourceLocal = 2,
}

impl TranslationModel {
    pub fn from_legacy_value(value: u32) -> Self {
        match value {
            0 => Self::Argos,
            1 => Self::Google,
            _ => Self::OpenSourceLocal,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Argos => "Argos Translate",
            Self::Google => "Google Translate",
            Self::OpenSourceLocal => "Open-source Local",
        }
    }

    pub fn supports_auto_detect(self) -> bool {
        matches!(self, Self::Google | Self::OpenSourceLocal)
    }
}

pub const SUPPORTED_TRANSLATION_LANGUAGES: &[(&str, &str)] = &[
    ("auto", "Auto-detect"),
    ("ar", "Arabic"),
    ("az", "Azerbaijani"),
    ("bg", "Bulgarian"),
    ("bn", "Bengali"),
    ("ca", "Catalan"),
    ("cs", "Czech"),
    ("da", "Danish"),
    ("de", "German"),
    ("el", "Greek"),
    ("en", "English"),
    ("eo", "Esperanto"),
    ("es", "Spanish"),
    ("et", "Estonian"),
    ("fa", "Persian"),
    ("fi", "Finnish"),
    ("fr", "French"),
    ("ga", "Irish"),
    ("he", "Hebrew"),
    ("hi", "Hindi"),
    ("hu", "Hungarian"),
    ("id", "Indonesian"),
    ("it", "Italian"),
    ("ja", "Japanese"),
    ("ko", "Korean"),
    ("lt", "Lithuanian"),
    ("lv", "Latvian"),
    ("ms", "Malay"),
    ("nb", "Norwegian"),
    ("nl", "Dutch"),
    ("pl", "Polish"),
    ("pt", "Portuguese"),
    ("ro", "Romanian"),
    ("ru", "Russian"),
    ("sk", "Slovak"),
    ("sl", "Slovenian"),
    ("sq", "Albanian"),
    ("sr", "Serbian"),
    ("sv", "Swedish"),
    ("th", "Thai"),
    ("tl", "Tagalog"),
    ("tr", "Turkish"),
    ("uk", "Ukrainian"),
    ("ur", "Urdu"),
    ("vi", "Vietnamese"),
    ("zh", "Chinese"),
];

pub fn translation_language_name(code: &str) -> &str {
    SUPPORTED_TRANSLATION_LANGUAGES
        .iter()
        .find_map(|(language_code, name)| language_code.eq_ignore_ascii_case(code).then_some(*name))
        .unwrap_or(code)
}

pub fn resolve_translation_source_language(from_code: Option<&str>) -> String {
    let requested = from_code.unwrap_or_default().trim();
    if requested.is_empty() || requested.eq_ignore_ascii_case("auto") {
        return "auto".to_string();
    }

    normalize_supported_translation_language(requested).unwrap_or_else(|| "auto".to_string())
}

pub fn resolve_translation_target_language(
    to_code: Option<&str>,
    interface_language: Option<&str>,
    system_language: Option<&str>,
) -> String {
    let requested = to_code.unwrap_or_default().trim();
    if !requested.is_empty() && !requested.eq_ignore_ascii_case("auto") {
        return normalize_supported_translation_language(requested)
            .unwrap_or_else(|| "en".to_string());
    }

    interface_language
        .and_then(normalize_supported_translation_language)
        .or_else(|| system_language.and_then(normalize_supported_translation_language))
        .unwrap_or_else(|| "en".to_string())
}

pub fn normalize_supported_translation_language(language_code: &str) -> Option<String> {
    let normalized = language_code.trim().replace('_', "-");
    if normalized.is_empty() {
        return None;
    }

    if let Some((code, _)) = SUPPORTED_TRANSLATION_LANGUAGES
        .iter()
        .find(|(code, _)| code.eq_ignore_ascii_case(&normalized))
    {
        return Some((*code).to_string());
    }

    let neutral = normalized
        .split_once('-')
        .map_or(normalized.as_str(), |(code, _)| code);
    SUPPORTED_TRANSLATION_LANGUAGES
        .iter()
        .find(|(code, _)| *code != "auto" && code.eq_ignore_ascii_case(neutral))
        .map(|(code, _)| (*code).to_string())
}

pub fn translation_configuration_error(
    from_code: &str,
    model: TranslationModel,
    google_api_key_present: bool,
    argos_ready: bool,
    local_runtime_ready: bool,
) -> Option<&'static str> {
    match model {
        TranslationModel::Google if !google_api_key_present => {
            Some("Google Translate API key not set. Add it in Settings -> OCR.")
        }
        TranslationModel::OpenSourceLocal if !local_runtime_ready => {
            Some("Open-source local translation is not installed. Install it in Settings -> OCR.")
        }
        TranslationModel::Argos if from_code.eq_ignore_ascii_case("auto") => {
            Some("Argos Translate does not support auto-detect. Pick a source language or use Google Translate.")
        }
        TranslationModel::Argos if !argos_ready => {
            Some("Argos Translate is not installed. Install it in Settings -> OCR.")
        }
        _ => None,
    }
}

pub fn build_google_translate_curl_request(
    text: &str,
    from_code: &str,
    to_code: &str,
    api_key: &str,
) -> Result<CurlTranslationRequest, String> {
    let api_key = api_key.trim();
    if api_key.is_empty() {
        return Err("Google Translate API key not set. Add it in Settings -> OCR.".into());
    }
    let target = resolve_translation_target_language(Some(to_code), None, None);
    let source = resolve_translation_source_language(Some(from_code));
    let mut args = vec![
        "--silent".into(),
        "--show-error".into(),
        "--location".into(),
        "--request".into(),
        "POST".into(),
        "--data-urlencode".into(),
        format!("q={text}"),
        "--data-urlencode".into(),
        format!("target={target}"),
    ];
    if !source.eq_ignore_ascii_case("auto") {
        args.extend(["--data-urlencode".into(), format!("source={source}")]);
    }
    args.extend([
        "--data-urlencode".into(),
        "format=text".into(),
        "--write-out".into(),
        "\n%{http_code}".into(),
        format!(
            "https://translation.googleapis.com/language/translate/v2?key={}",
            percent_encode_query(api_key)
        ),
    ]);

    Ok(CurlTranslationRequest {
        program: "curl".into(),
        args,
    })
}

pub fn parse_google_translate_response(output: &str) -> Result<String, String> {
    let (body, status_code) = split_google_translate_body_and_status(output)?;
    let json: serde_json::Value = serde_json::from_str(body)
        .map_err(|error| format!("Google Translate returned invalid JSON: {error}"))?;

    if !(200..300).contains(&status_code) {
        return Err(extract_google_translate_error(&json)
            .unwrap_or_else(|| format!("Google Translate request failed ({status_code}).")));
    }

    json.pointer("/data/translations/0/translatedText")
        .and_then(serde_json::Value::as_str)
        .map(str::to_string)
        .ok_or_else(|| "Google Translate response did not include translated text.".into())
}

fn extract_google_translate_error(json: &serde_json::Value) -> Option<String> {
    json.pointer("/error/message")
        .and_then(serde_json::Value::as_str)
        .map(str::to_string)
        .or_else(|| json.get("error").map(ToString::to_string))
}

fn split_google_translate_body_and_status(output: &str) -> Result<(&str, u16), String> {
    let (body, status) = output.rsplit_once('\n').ok_or_else(|| {
        "Google Translate output did not include an HTTP status code.".to_string()
    })?;
    let status = status
        .trim()
        .parse::<u16>()
        .map_err(|error| format!("Google Translate status was not numeric: {error}"))?;
    Ok((body, status))
}

fn percent_encode_query(value: &str) -> String {
    let mut encoded = String::new();
    for byte in value.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                encoded.push(byte as char)
            }
            _ => encoded.push_str(&format!("%{byte:02X}")),
        }
    }
    encoded
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn translation_models_match_legacy_labels_and_auto_detect_support() {
        assert_eq!(
            TranslationModel::from_legacy_value(0),
            TranslationModel::Argos
        );
        assert_eq!(
            TranslationModel::from_legacy_value(1),
            TranslationModel::Google
        );
        assert_eq!(
            TranslationModel::from_legacy_value(2),
            TranslationModel::OpenSourceLocal
        );
        assert_eq!(
            TranslationModel::from_legacy_value(99),
            TranslationModel::OpenSourceLocal
        );
        assert_eq!(TranslationModel::Argos.label(), "Argos Translate");
        assert!(!TranslationModel::Argos.supports_auto_detect());
        assert!(TranslationModel::Google.supports_auto_detect());
        assert!(TranslationModel::OpenSourceLocal.supports_auto_detect());
    }

    #[test]
    fn translation_language_resolution_accepts_specific_and_neutral_codes() {
        assert_eq!(
            normalize_supported_translation_language("pt-BR").as_deref(),
            Some("pt")
        );
        assert_eq!(
            normalize_supported_translation_language("zh_Hans").as_deref(),
            Some("zh")
        );
        assert_eq!(
            normalize_supported_translation_language("klingon").as_deref(),
            None
        );
        assert_eq!(translation_language_name("es"), "Spanish");
        assert_eq!(translation_language_name("xx"), "xx");
    }

    #[test]
    fn translation_source_language_falls_back_to_auto() {
        assert_eq!(resolve_translation_source_language(None), "auto");
        assert_eq!(resolve_translation_source_language(Some("auto")), "auto");
        assert_eq!(resolve_translation_source_language(Some("en-US")), "en");
        assert_eq!(resolve_translation_source_language(Some("unknown")), "auto");
    }

    #[test]
    fn translation_target_language_falls_back_to_interface_then_system_then_english() {
        assert_eq!(
            resolve_translation_target_language(Some("de-DE"), None, None),
            "de"
        );
        assert_eq!(
            resolve_translation_target_language(Some("auto"), Some("fr-CA"), Some("es-MX")),
            "fr"
        );
        assert_eq!(
            resolve_translation_target_language(Some("auto"), Some("unknown"), Some("es-MX")),
            "es"
        );
        assert_eq!(
            resolve_translation_target_language(Some("auto"), Some("unknown"), Some("unknown")),
            "en"
        );
    }

    #[test]
    fn translation_configuration_errors_match_legacy_runtime_rules() {
        assert_eq!(
            translation_configuration_error("en", TranslationModel::Google, false, true, true),
            Some("Google Translate API key not set. Add it in Settings -> OCR.")
        );
        assert_eq!(
            translation_configuration_error(
                "en",
                TranslationModel::OpenSourceLocal,
                true,
                true,
                false
            ),
            Some("Open-source local translation is not installed. Install it in Settings -> OCR.")
        );
        assert_eq!(
            translation_configuration_error("auto", TranslationModel::Argos, true, true, true),
            Some("Argos Translate does not support auto-detect. Pick a source language or use Google Translate.")
        );
        assert_eq!(
            translation_configuration_error("en", TranslationModel::Argos, true, false, true),
            Some("Argos Translate is not installed. Install it in Settings -> OCR.")
        );
        assert_eq!(
            translation_configuration_error("en", TranslationModel::Google, true, false, false),
            None
        );
    }

    #[test]
    fn google_translate_curl_request_matches_legacy_form_fields() {
        let request = build_google_translate_curl_request("hello world", "auto", "es-MX", "a b")
            .expect("build google translate request");

        assert_eq!(request.program, "curl");
        assert!(request.args.contains(&"q=hello world".to_string()));
        assert!(request.args.contains(&"target=es".to_string()));
        assert!(request.args.contains(&"format=text".to_string()));
        assert!(!request.args.iter().any(|arg| arg == "source=auto"));
        assert!(request.args.iter().any(|arg| arg.ends_with("key=a%20b")));
    }

    #[test]
    fn google_translate_parser_reports_success_and_api_errors() {
        let success = r#"{"data":{"translations":[{"translatedText":"hola"}]}}"#;
        assert_eq!(
            parse_google_translate_response(&format!("{success}\n200")).as_deref(),
            Ok("hola")
        );

        let failure = r#"{"error":{"message":"bad key"}}"#;
        assert_eq!(
            parse_google_translate_response(&format!("{failure}\n403"))
                .expect_err("403 response should report the Google Translate API error"),
            "bad key"
        );
    }
}
