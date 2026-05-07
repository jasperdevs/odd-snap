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
}
