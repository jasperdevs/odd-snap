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

    let translated = match model {
        TranslationModel::Google => {
            let api_key = google_key.ok_or_else(|| {
                "Google Translate API key not set. Add it in Settings -> OCR.".to_string()
            })?;
            let request = build_google_translate_curl_request(text, &source, &target, api_key)?;
            run_translation_curl_request(&request)
                .and_then(|output| parse_google_translate_response(&output))?
        }
        TranslationModel::Argos => run_argos_translate(text, &source, &target)?,
        TranslationModel::OpenSourceLocal => {
            return Err(
                "Open-source local translation runtime execution is pending in Rust.".into(),
            );
        }
    };

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

fn run_argos_translate(text: &str, source: &str, target: &str) -> Result<String, String> {
    let command = build_argos_translate_command(text, source, target);
    let output = Command::new(&command.program)
        .args(&command.args)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .map_err(|error| format!("failed to start Argos Translate: {error}"))?;

    if output.status.success() {
        return Ok(String::from_utf8_lossy(&output.stdout)
            .trim_end()
            .to_string());
    }

    let stderr = String::from_utf8_lossy(&output.stderr);
    let stdout = String::from_utf8_lossy(&output.stdout);
    let message = if stderr.trim().is_empty() {
        stdout.trim()
    } else {
        stderr.trim()
    };
    Err(if message.is_empty() {
        "Argos translation failed.".into()
    } else {
        message.into()
    })
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct ArgosTranslateCommand {
    program: &'static str,
    args: Vec<String>,
}

fn build_argos_translate_command(text: &str, source: &str, target: &str) -> ArgosTranslateCommand {
    let mut args = Vec::new();
    if let Some(arg) = python_launcher_arg() {
        args.push(arg.into());
    }
    args.extend([
        "-c".into(),
        ARGOS_TRANSLATE_SCRIPT.into(),
        text.into(),
        source.into(),
        target.into(),
    ]);

    ArgosTranslateCommand {
        program: python_launcher_program(),
        args,
    }
}

fn python_launcher_program() -> &'static str {
    if cfg!(target_os = "windows") {
        "py"
    } else {
        "python3"
    }
}

fn python_launcher_arg() -> Option<&'static str> {
    if cfg!(target_os = "windows") {
        Some("-3")
    } else {
        None
    }
}

const ARGOS_TRANSLATE_SCRIPT: &str = r#"
import sys
import argostranslate.translate as tr

text = sys.argv[1]
from_code = sys.argv[2]
to_code = sys.argv[3]

installed = tr.get_installed_languages()
from_lang = next((l for l in installed if l.code == from_code), None)
to_lang = next((l for l in installed if l.code == to_code), None)

if not from_lang or not to_lang or not from_lang.get_translation(to_lang):
    import argostranslate.package as pkg
    pkg.update_package_index()
    available = pkg.get_available_packages()
    match = next((p for p in available if p.from_code == from_code and p.to_code == to_code), None)
    if not match:
        raise RuntimeError("No Argos language pack is available for this language pair.")
    download_path = match.download()
    pkg.install_from_path(download_path)
    installed = tr.get_installed_languages()
    from_lang = next((l for l in installed if l.code == from_code), None)
    to_lang = next((l for l in installed if l.code == to_code), None)
    if not from_lang or not to_lang or not from_lang.get_translation(to_lang):
        raise RuntimeError("Argos language pack install completed, but the translation pair is still unavailable.")

translated = tr.translate(text, from_code, to_code)
print(translated)
"#;

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

    #[test]
    fn argos_translate_command_preserves_legacy_script_arguments() {
        let command = build_argos_translate_command("hello", "en", "es");

        assert!(command.args.contains(&"-c".to_string()));
        assert!(command
            .args
            .iter()
            .any(|arg| arg.contains("argostranslate.translate")));
        assert_eq!(command.args[command.args.len() - 3], "hello");
        assert_eq!(command.args[command.args.len() - 2], "en");
        assert_eq!(command.args[command.args.len() - 1], "es");
    }
}
