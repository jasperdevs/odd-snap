use std::process::{Command, Stdio};

use oddsnap_core::{
    build_google_translate_curl_request, parse_google_translate_response,
    resolve_translation_source_language, resolve_translation_target_language,
    translation_configuration_error, AppSettings, CurlTranslationRequest, TranslationModel,
};

#[derive(Debug)]
pub(crate) struct OcrTranslationResult {
    pub(crate) text: String,
    pub(crate) source: String,
    pub(crate) target: String,
    pub(crate) model: TranslationModel,
}

pub(crate) fn translate_ocr_text(
    text: &str,
    settings: &AppSettings,
) -> Result<OcrTranslationResult, String> {
    let source = resolve_translation_source_language(Some(&settings.ocr_default_translate_from));
    let target = resolve_translation_target_language(
        Some(&settings.ocr_default_translate_to),
        Some(&settings.interface_language),
        None,
    );
    let model = TranslationModel::from_legacy_value(settings.translation_model);
    let google_key = settings
        .google_translate_api_key
        .as_deref()
        .filter(|key| !key.trim().is_empty());

    if let Some(error) = translation_configuration_error(
        &source,
        model,
        google_key.is_some(),
        settings.translation_runtime_installed,
        settings.translation_runtime_installed,
    ) {
        return Err(error.into());
    }

    if model != TranslationModel::Google {
        return Err(format!(
            "{} translation runtime is preserved in settings, but Rust runtime execution is pending.",
            model.label()
        ));
    }

    let api_key = google_key.ok_or_else(|| {
        "Google Translate API key not set. Add it in Settings -> OCR.".to_string()
    })?;
    let request = build_google_translate_curl_request(text, &source, &target, api_key)?;
    let translated = run_translation_curl_request(&request)
        .and_then(|output| parse_google_translate_response(&output))?;

    Ok(OcrTranslationResult {
        text: translated,
        source,
        target,
        model,
    })
}

fn run_translation_curl_request(request: &CurlTranslationRequest) -> Result<String, String> {
    let output = Command::new(&request.program)
        .args(&request.args)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .map_err(|error| format!("failed to start translation curl: {error}"))?;

    let stdout = String::from_utf8_lossy(&output.stdout).to_string();
    if output.status.success() {
        return Ok(stdout);
    }

    let stderr = String::from_utf8_lossy(&output.stderr);
    if stdout.trim().is_empty() {
        let error = stderr.trim();
        Err(if error.is_empty() {
            format!("translation curl exited with status {}", output.status)
        } else {
            error.into()
        })
    } else {
        Ok(stdout)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn translate_ocr_text_reports_missing_google_key_before_curl() {
        let settings = AppSettings {
            translation_model: TranslationModel::Google as u32,
            ..AppSettings::default()
        };

        assert_eq!(
            translate_ocr_text("hello", &settings).unwrap_err(),
            "Google Translate API key not set. Add it in Settings -> OCR."
        );
    }
}
