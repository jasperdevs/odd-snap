use std::{
    io::Write,
    path::Path,
    process::{Command, Stdio},
};

use oddsnap_core::{
    build_deepai_upscale_curl_request, build_image_download_curl_request,
    build_sticker_api_curl_request, parse_deepai_upscale_output, parse_image_output_curl_status,
    AppSettings, CurlUploadRequest, StickerSettings, UpscaleSettings,
};

pub(crate) fn process_sticker_capture(
    settings: &AppSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    let provider_name = sticker_settings.provider.label();
    let request = build_sticker_api_curl_request(&sticker_settings, input_path, output_path)?;
    let (stdout, stderr) = run_curl_request(&request)?;
    parse_image_output_curl_status(&provider_name, &stdout, output_path)
        .map_err(|error| append_curl_stderr(error, &stderr))?;
    Ok(provider_name)
}

pub(crate) fn process_upscale_capture(
    settings: &AppSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    let upscale_settings =
        UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref());
    let provider_name = upscale_settings.provider.label();
    let request = build_deepai_upscale_curl_request(&upscale_settings, input_path)?;
    let (stdout, stderr) = run_curl_request(&request)?;
    let output_url = parse_deepai_upscale_output(&provider_name, &stdout)
        .map_err(|error| append_curl_stderr(error, &stderr))?;
    let download = build_image_download_curl_request(&provider_name, &output_url, output_path)?;
    let (download_stdout, download_stderr) = run_curl_request(&download)?;
    parse_image_output_curl_status(&provider_name, &download_stdout, output_path)
        .map_err(|error| append_curl_stderr(error, &download_stderr))?;
    Ok(provider_name)
}

fn run_curl_request(request: &CurlUploadRequest) -> Result<(String, String), String> {
    let mut command = Command::new(&request.program);
    command.args(&request.args);
    if request.stdin_body.is_some() {
        command.stdin(Stdio::piped());
    } else {
        command.stdin(Stdio::null());
    }
    let mut child = command
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| {
            format!(
                "{} processing could not start curl: {error}",
                request.provider_name
            )
        })?;

    if let Some(body) = &request.stdin_body {
        let mut stdin = child.stdin.take().ok_or_else(|| {
            format!(
                "{} processing could not open curl stdin.",
                request.provider_name
            )
        })?;
        stdin.write_all(body).map_err(|error| {
            format!(
                "{} processing could not write curl body: {error}",
                request.provider_name
            )
        })?;
    }

    let output = child.wait_with_output().map_err(|error| {
        format!(
            "{} processing failed while waiting for curl: {error}",
            request.provider_name
        )
    })?;

    let stdout = String::from_utf8_lossy(&output.stdout);
    let stderr = String::from_utf8_lossy(&output.stderr);
    if !output.status.success() && stdout.trim().is_empty() {
        let error = stderr.trim();
        return Err(if error.is_empty() {
            format!(
                "{} processing failed before a response was returned.",
                request.provider_name
            )
        } else {
            error.into()
        });
    }

    Ok((stdout.into(), stderr.into()))
}

fn append_curl_stderr(error: String, stderr: &str) -> String {
    let stderr = stderr.trim();
    if stderr.is_empty() {
        error
    } else {
        format!("{error}; curl: {stderr}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use oddsnap_core::{StickerProvider, UpscaleProvider};

    #[test]
    fn sticker_processor_reports_local_runtime_pending() {
        let settings = AppSettings::default();

        let error =
            process_sticker_capture(&settings, Path::new("input.png"), Path::new("output.png"))
                .expect_err("local pending");

        assert!(error.contains("Local sticker runtime is pending"));
    }

    #[test]
    fn upscale_processor_reports_local_runtime_pending() {
        let settings = AppSettings::default();

        let error =
            process_upscale_capture(&settings, Path::new("input.png"), Path::new("output.png"))
                .expect_err("local pending");

        assert!(error.contains("Local upscale runtime is pending"));
    }

    #[test]
    fn remote_labels_match_legacy_names() {
        assert_eq!(StickerProvider::RemoveBg.label(), "Remove.bg");
        assert_eq!(
            UpscaleProvider::DeepAiSuperResolution.label(),
            "DeepAI Super Resolution"
        );
    }
}
