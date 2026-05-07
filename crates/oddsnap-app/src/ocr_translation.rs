use std::{
    fs,
    path::PathBuf,
    process::{Command, Stdio},
};

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
    let argos_ready = argos_runtime_is_installed();
    let local_runtime_ready = open_source_local_runtime_is_installed();

    if let Some(error) = translation_configuration_error(
        &source,
        model,
        google_key.is_some(),
        argos_ready,
        local_runtime_ready,
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
            run_open_source_local_translate(text, &source, &target)?
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

pub(crate) fn translation_runtime_status_summary(settings: &AppSettings) -> String {
    let argos = if argos_runtime_is_installed() {
        "Argos installed"
    } else {
        "Argos not installed"
    };
    let local = if open_source_local_runtime_is_installed() {
        "local installed"
    } else if settings.translation_runtime_installed {
        "local marker stale"
    } else {
        "local not installed"
    };
    format!("Translation runtimes: {argos}; {local}")
}

pub(crate) fn argos_runtime_is_installed() -> bool {
    fs::read_to_string(argos_runtime_marker_path())
        .map(|marker| marker.trim() == ARGOS_TRANSLATE_PACKAGE)
        .unwrap_or(false)
}

pub(crate) fn open_source_local_runtime_is_installed() -> bool {
    has_open_source_local_runtime_files(&open_source_local_runtime_paths())
}

pub(crate) fn install_argos_runtime() -> Result<(), String> {
    run_python_command(
        &argos_install_command(),
        "Couldn't install Argos Translate.",
    )?;
    fs::create_dir_all(argos_runtime_state_dir())
        .map_err(|error| format!("Couldn't create Argos runtime marker directory: {error}"))?;
    fs::write(argos_runtime_marker_path(), ARGOS_TRANSLATE_PACKAGE)
        .map_err(|error| format!("Couldn't write Argos runtime marker: {error}"))?;
    Ok(())
}

pub(crate) fn remove_argos_runtime() -> Result<(), String> {
    run_python_command(
        &argos_remove_command(),
        "Couldn't uninstall Argos Translate.",
    )?;
    match fs::remove_file(argos_runtime_marker_path()) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(format!("Couldn't delete Argos runtime marker: {error}")),
    }
}

pub(crate) fn install_open_source_local_runtime() -> Result<(), String> {
    run_python_command(
        &open_source_local_package_install_command(),
        "Couldn't install the local translation runtime packages.",
    )?;
    let paths = open_source_local_runtime_paths();
    run_python_command(
        &open_source_local_prepare_command(&paths),
        "Couldn't prepare the local translation model.",
    )
    .or_else(|error| {
        if !error.contains("incomplete") && !error.contains("output directory") {
            return Err(error);
        }
        remove_open_source_local_runtime()?;
        run_python_command(
            &open_source_local_prepare_command(&paths),
            "Couldn't prepare the local translation model.",
        )
    })?;
    Ok(())
}

pub(crate) fn remove_open_source_local_runtime() -> Result<(), String> {
    let paths = open_source_local_runtime_paths();
    match fs::remove_dir_all(&paths.root) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(format!(
            "Couldn't remove the local translation runtime: {error}"
        )),
    }
}

fn run_open_source_local_translate(
    text: &str,
    source: &str,
    target: &str,
) -> Result<String, String> {
    let paths = open_source_local_runtime_paths();
    if !has_open_source_local_runtime_files(&paths) {
        return Err("Open-source local translation runtime files are missing or incomplete. Install it in Settings -> OCR.".into());
    }

    let command = build_open_source_local_translate_command(text, source, target, &paths);
    let output = Command::new(&command.program)
        .args(&command.args)
        .env("PYTHONUTF8", "1")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .map_err(|error| format!("failed to start open-source local translation: {error}"))?;

    if output.status.success() {
        return Ok(String::from_utf8_lossy(&output.stdout)
            .trim_end()
            .to_string());
    }

    Err(normalize_python_translation_error(
        &String::from_utf8_lossy(&output.stderr),
        &String::from_utf8_lossy(&output.stdout),
        "Local translation failed.",
    ))
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct OpenSourceLocalRuntimePaths {
    root: PathBuf,
    model_dir: PathBuf,
    tokenizer_dir: PathBuf,
    runtime_version_path: PathBuf,
}

fn open_source_local_runtime_paths() -> OpenSourceLocalRuntimePaths {
    open_source_local_runtime_paths_from_root(default_open_source_local_runtime_root())
}

fn open_source_local_runtime_paths_from_root(root: PathBuf) -> OpenSourceLocalRuntimePaths {
    OpenSourceLocalRuntimePaths {
        root: root.clone(),
        model_dir: root.join("m2m100_ct2"),
        tokenizer_dir: root.join("tokenizer"),
        runtime_version_path: root.join("runtime.version"),
    }
}

fn default_open_source_local_runtime_root() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(appdata) = std::env::var_os("APPDATA") {
            return PathBuf::from(appdata)
                .join("OddSnap")
                .join("translate-local");
        }
    }

    #[cfg(target_os = "macos")]
    {
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("OddSnap")
                .join("translate-local");
        }
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        if let Some(data_home) = std::env::var_os("XDG_DATA_HOME") {
            return PathBuf::from(data_home)
                .join("oddsnap")
                .join("translate-local");
        }

        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join(".local")
                .join("share")
                .join("oddsnap")
                .join("translate-local");
        }
    }

    std::env::temp_dir().join("OddSnap").join("translate-local")
}

fn has_open_source_local_runtime_files(paths: &OpenSourceLocalRuntimePaths) -> bool {
    paths.model_dir.join("model.bin").is_file()
        && paths.tokenizer_dir.join("tokenizer_config.json").is_file()
        && fs::read_to_string(&paths.runtime_version_path)
            .map(|version| version.trim() == OPEN_SOURCE_LOCAL_RUNTIME_VERSION)
            .unwrap_or(false)
}

fn argos_runtime_marker_path() -> PathBuf {
    argos_runtime_state_dir().join("runtime.marker")
}

fn argos_runtime_state_dir() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(appdata) = std::env::var_os("APPDATA") {
            return PathBuf::from(appdata).join("OddSnap").join("argos");
        }
    }

    #[cfg(target_os = "macos")]
    {
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("OddSnap")
                .join("argos");
        }
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        if let Some(data_home) = std::env::var_os("XDG_DATA_HOME") {
            return PathBuf::from(data_home).join("oddsnap").join("argos");
        }

        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join(".local")
                .join("share")
                .join("oddsnap")
                .join("argos");
        }
    }

    std::env::temp_dir().join("OddSnap").join("argos")
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct OpenSourceLocalTranslateCommand {
    program: &'static str,
    args: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct PythonCommand {
    program: &'static str,
    args: Vec<String>,
}

fn run_python_command(command: &PythonCommand, fallback: &str) -> Result<String, String> {
    let output = Command::new(command.program)
        .args(&command.args)
        .env("PYTHONUTF8", "1")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .map_err(|error| format!("{fallback} Python could not start: {error}"))?;

    if output.status.success() {
        return Ok(String::from_utf8_lossy(&output.stdout)
            .trim_end()
            .to_string());
    }

    Err(normalize_python_translation_error(
        &String::from_utf8_lossy(&output.stderr),
        &String::from_utf8_lossy(&output.stdout),
        fallback,
    ))
}

fn python_command(args: Vec<String>) -> PythonCommand {
    let mut command_args = Vec::new();
    if let Some(arg) = python_launcher_arg() {
        command_args.push(arg.into());
    }
    command_args.extend(args);

    PythonCommand {
        program: python_launcher_program(),
        args: command_args,
    }
}

fn argos_install_command() -> PythonCommand {
    python_command(vec![
        "-m".into(),
        "pip".into(),
        "install".into(),
        "--user".into(),
        "--disable-pip-version-check".into(),
        ARGOS_TRANSLATE_PACKAGE.into(),
    ])
}

fn argos_remove_command() -> PythonCommand {
    python_command(vec![
        "-m".into(),
        "pip".into(),
        "uninstall".into(),
        "-y".into(),
        "argostranslate".into(),
    ])
}

fn open_source_local_package_install_command() -> PythonCommand {
    let mut args = vec![
        "-m".into(),
        "pip".into(),
        "install".into(),
        "--user".into(),
        "--disable-pip-version-check".into(),
    ];
    args.extend(
        OPEN_SOURCE_LOCAL_RUNTIME_PACKAGES
            .iter()
            .map(|package| (*package).to_string()),
    );
    python_command(args)
}

fn open_source_local_prepare_command(paths: &OpenSourceLocalRuntimePaths) -> PythonCommand {
    python_command(vec![
        "-c".into(),
        OPEN_SOURCE_LOCAL_INSTALL_SCRIPT.into(),
        paths.model_dir.to_string_lossy().into_owned(),
        paths.tokenizer_dir.to_string_lossy().into_owned(),
        paths.runtime_version_path.to_string_lossy().into_owned(),
        OPEN_SOURCE_LOCAL_RUNTIME_VERSION.into(),
    ])
}

fn build_open_source_local_translate_command(
    text: &str,
    source: &str,
    target: &str,
    paths: &OpenSourceLocalRuntimePaths,
) -> OpenSourceLocalTranslateCommand {
    let mut args = Vec::new();
    if let Some(arg) = python_launcher_arg() {
        args.push(arg.into());
    }
    args.extend([
        "-c".into(),
        OPEN_SOURCE_LOCAL_TRANSLATE_SCRIPT.into(),
        text.into(),
        source.into(),
        target.into(),
        paths.model_dir.to_string_lossy().into_owned(),
        paths.tokenizer_dir.to_string_lossy().into_owned(),
    ]);

    OpenSourceLocalTranslateCommand {
        program: python_launcher_program(),
        args,
    }
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

fn normalize_python_translation_error(stderr: &str, stdout: &str, fallback: &str) -> String {
    let mut text = if stderr.trim().is_empty() {
        stdout.trim().to_string()
    } else {
        stderr.trim().to_string()
    };
    if text.is_empty() {
        return fallback.into();
    }

    text = text.replace('\r', "\n");
    let lower_text = text.to_lowercase();
    if lower_text.contains("no module named") {
        return "The local translation Python packages are missing or incomplete.".into();
    }
    if lower_text.contains("output directory") && lower_text.contains("exists") {
        return "Existing local translation files were incomplete. Retry install and OddSnap will rebuild them.".into();
    }
    if text.contains("Traceback") {
        let last_meaningful = text.lines().rev().map(str::trim).find(|line| {
            !line.is_empty()
                && !line.starts_with("File ")
                && !line.starts_with("Traceback")
                && !line.starts_with('^')
        });
        if let Some(line) = last_meaningful {
            return line.to_string();
        }
    }

    while text.contains('\n') {
        text = text.replace('\n', " ");
    }
    while text.contains("  ") {
        text = text.replace("  ", " ");
    }
    if text.len() <= 180 {
        text
    } else {
        format!("{}...", text.chars().take(177).collect::<String>())
    }
}

const OPEN_SOURCE_LOCAL_RUNTIME_VERSION: &str = "m2m100-418m-ct2-v2";
const ARGOS_TRANSLATE_PACKAGE: &str = "argostranslate==1.11.0";
const OPEN_SOURCE_LOCAL_RUNTIME_PACKAGES: &[&str] = &[
    "ctranslate2==4.7.1",
    "transformers==5.5.4",
    "sentencepiece==0.2.1",
    "langid==1.1.6",
    "huggingface_hub==1.10.2",
    "numpy==2.4.4",
];

const OPEN_SOURCE_LOCAL_INSTALL_SCRIPT: &str = r#"
import os
import shutil
import sys
from pathlib import Path

from ctranslate2.converters import TransformersConverter
from transformers import AutoTokenizer

model_dir = sys.argv[1]
tokenizer_dir = sys.argv[2]
runtime_version_path = sys.argv[3]
runtime_version = sys.argv[4]

if not os.path.exists(os.path.join(model_dir, "model.bin")):
    if os.path.isdir(model_dir):
        shutil.rmtree(model_dir)
    Path(model_dir).parent.mkdir(parents=True, exist_ok=True)
    converter = TransformersConverter("facebook/m2m100_418M")
    converter.convert(model_dir, quantization="int8")
else:
    Path(model_dir).mkdir(parents=True, exist_ok=True)

if os.path.isdir(tokenizer_dir):
    shutil.rmtree(tokenizer_dir)
Path(tokenizer_dir).mkdir(parents=True, exist_ok=True)
tokenizer = AutoTokenizer.from_pretrained("facebook/m2m100_418M", use_fast=False)
tokenizer.save_pretrained(tokenizer_dir)

with open(runtime_version_path, "w", encoding="utf-8") as version_file:
    version_file.write(runtime_version)

print("ok")
"#;

const OPEN_SOURCE_LOCAL_TRANSLATE_SCRIPT: &str = r#"
import sys

import ctranslate2
import langid
from transformers import AutoTokenizer

text = sys.argv[1]
from_code = sys.argv[2].strip().lower()
to_code = sys.argv[3].strip().lower()
model_dir = sys.argv[4]
tokenizer_dir = sys.argv[5]

aliases = {
    "nb": "no",
    "he": "he",
    "zh": "zh",
}

tokenizer = AutoTokenizer.from_pretrained(tokenizer_dir, use_fast=False)
available = set(tokenizer.lang_code_to_token.keys())

def resolve_language(code):
    if code == "auto":
        code = langid.classify(text)[0].lower()
    code = aliases.get(code, code)
    if code not in available:
        raise SystemExit(f"Language '{code}' is not supported by the open-source local model.")
    return code

source_language = resolve_language(from_code)
target_language = resolve_language(to_code)

tokenizer.src_lang = source_language
source_tokens = tokenizer.convert_ids_to_tokens(tokenizer.encode(text))
target_prefix = [tokenizer.lang_code_to_token[target_language]]

translator = ctranslate2.Translator(model_dir, device="cpu", compute_type="int8")
result = translator.translate_batch([source_tokens], target_prefix=[target_prefix], beam_size=4, max_batch_size=1)
target_tokens = result[0].hypotheses[0]
if target_tokens and target_tokens[0] == tokenizer.lang_code_to_token[target_language]:
    target_tokens = target_tokens[1:]

translated = tokenizer.decode(tokenizer.convert_tokens_to_ids(target_tokens), skip_special_tokens=True)
print(translated)
"#;

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

    #[test]
    fn argos_runtime_commands_preserve_legacy_package_actions() {
        let install = argos_install_command();
        assert!(install.args.contains(&"pip".to_string()));
        assert!(install.args.contains(&"install".to_string()));
        assert!(install.args.contains(&"--user".to_string()));
        assert!(install.args.contains(&ARGOS_TRANSLATE_PACKAGE.to_string()));

        let remove = argos_remove_command();
        assert!(remove.args.contains(&"pip".to_string()));
        assert!(remove.args.contains(&"uninstall".to_string()));
        assert!(remove.args.contains(&"-y".to_string()));
        assert!(remove.args.contains(&"argostranslate".to_string()));
    }

    #[test]
    fn open_source_local_paths_match_legacy_runtime_layout() {
        let paths = open_source_local_runtime_paths_from_root(PathBuf::from(
            "C:/Users/me/AppData/Roaming/OddSnap/translate-local",
        ));

        assert_eq!(
            paths.model_dir,
            PathBuf::from("C:/Users/me/AppData/Roaming/OddSnap/translate-local/m2m100_ct2")
        );
        assert_eq!(
            paths.tokenizer_dir,
            PathBuf::from("C:/Users/me/AppData/Roaming/OddSnap/translate-local/tokenizer")
        );
        assert_eq!(
            paths.runtime_version_path,
            PathBuf::from("C:/Users/me/AppData/Roaming/OddSnap/translate-local/runtime.version")
        );
    }

    #[test]
    fn open_source_local_translate_command_preserves_legacy_script_arguments() {
        let paths =
            open_source_local_runtime_paths_from_root(PathBuf::from("C:/OddSnap/translate-local"));
        let command = build_open_source_local_translate_command("hello", "auto", "es", &paths);

        assert!(command.args.contains(&"-c".to_string()));
        assert!(command
            .args
            .iter()
            .any(|arg| arg.contains("ctranslate2.Translator")));
        assert_eq!(command.args[command.args.len() - 5], "hello");
        assert_eq!(command.args[command.args.len() - 4], "auto");
        assert_eq!(command.args[command.args.len() - 3], "es");
        assert_eq!(
            command.args[command.args.len() - 2],
            paths.model_dir.to_string_lossy().to_string()
        );
        assert_eq!(
            command.args[command.args.len() - 1],
            paths.tokenizer_dir.to_string_lossy().to_string()
        );
    }

    #[test]
    fn open_source_local_install_commands_preserve_legacy_runtime_layout() {
        let package_install = open_source_local_package_install_command();
        assert!(package_install.args.contains(&"pip".to_string()));
        assert!(package_install.args.contains(&"install".to_string()));
        for package in OPEN_SOURCE_LOCAL_RUNTIME_PACKAGES {
            assert!(package_install.args.contains(&package.to_string()));
        }

        let paths =
            open_source_local_runtime_paths_from_root(PathBuf::from("C:/OddSnap/translate-local"));
        let prepare = open_source_local_prepare_command(&paths);
        assert!(prepare.args.contains(&"-c".to_string()));
        assert!(prepare
            .args
            .iter()
            .any(|arg| arg.contains("TransformersConverter")));
        assert_eq!(
            prepare.args[prepare.args.len() - 4],
            paths.model_dir.to_string_lossy().to_string()
        );
        assert_eq!(
            prepare.args[prepare.args.len() - 3],
            paths.tokenizer_dir.to_string_lossy().to_string()
        );
        assert_eq!(
            prepare.args[prepare.args.len() - 2],
            paths.runtime_version_path.to_string_lossy().to_string()
        );
        assert_eq!(
            prepare.args[prepare.args.len() - 1],
            OPEN_SOURCE_LOCAL_RUNTIME_VERSION
        );
    }

    #[test]
    fn open_source_local_runtime_files_require_matching_version() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-local-runtime-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        let paths = open_source_local_runtime_paths_from_root(root.clone());

        fs::create_dir_all(&paths.model_dir).unwrap();
        fs::create_dir_all(&paths.tokenizer_dir).unwrap();
        fs::write(paths.model_dir.join("model.bin"), b"model").unwrap();
        fs::write(paths.tokenizer_dir.join("tokenizer_config.json"), b"{}").unwrap();
        fs::write(&paths.runtime_version_path, b"old").unwrap();
        assert!(!has_open_source_local_runtime_files(&paths));

        fs::write(
            &paths.runtime_version_path,
            OPEN_SOURCE_LOCAL_RUNTIME_VERSION,
        )
        .unwrap();
        assert!(has_open_source_local_runtime_files(&paths));

        fs::remove_dir_all(root).unwrap();
    }
}
