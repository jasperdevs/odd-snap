use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    process::{Command, Stdio},
};

use oddsnap_core::{
    build_deepai_upscale_curl_request, build_image_download_curl_request,
    build_sticker_api_curl_request, parse_deepai_upscale_output, parse_image_output_curl_status,
    AppSettings, CurlUploadRequest, StickerProvider, StickerSettings, UpscaleProvider,
    UpscaleSettings,
};

const RUNTIME_LAYOUT_VERSION: &str = "4";
const PIP_PACKAGE: &str = "pip==26.1";
const SETUPTOOLS_PACKAGE: &str = "setuptools==82.0.1";
const WHEEL_PACKAGE: &str = "wheel==0.47.0";
const REMBG_PACKAGE: &str = "rembg==2.0.75";
const NUMPY_PACKAGE: &str = "numpy==2.4.4";
const PILLOW_PACKAGE: &str = "pillow==12.2.0";
const ONNXRUNTIME_PACKAGE: &str = "onnxruntime==1.25.1";
const ONNXRUNTIME_GPU_PACKAGE: &str = "onnxruntime-gpu==1.25.1";

pub(crate) fn process_sticker_capture(
    settings: &AppSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    let provider_name = sticker_settings.provider.label();
    if sticker_settings.provider == StickerProvider::LocalCpu {
        return process_local_sticker_capture(&sticker_settings, input_path, output_path);
    }

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
    if upscale_settings.provider == UpscaleProvider::Local {
        return process_local_upscale_capture(&upscale_settings, input_path, output_path);
    }

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

pub(crate) fn sticker_upscale_runtime_status_summary(settings: &AppSettings) -> String {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    let upscale_settings =
        UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref());
    let sticker_runtime = local_runtime_status(
        RuntimeKind::Sticker,
        sticker_execution_provider(&sticker_settings),
    );
    let upscale_runtime = local_runtime_status(
        RuntimeKind::Upscale,
        upscale_execution_provider(&upscale_settings),
    );
    let sticker_model =
        if local_sticker_model_path(active_sticker_engine(&sticker_settings)).is_file() {
            "model ready"
        } else {
            "model missing"
        };
    let upscale_model =
        if local_upscale_model_path(active_upscale_engine(&upscale_settings)).is_file() {
            "model ready"
        } else {
            "model missing"
        };
    format!(
        "Sticker/upscale local: sticker {sticker_runtime}, {sticker_model}; upscale {upscale_runtime}, {upscale_model}"
    )
}

pub(crate) fn install_active_sticker_runtime(settings: &AppSettings) -> Result<String, String> {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    let execution = sticker_execution_provider(&sticker_settings);
    let engine = active_sticker_engine(&sticker_settings);
    ensure_sticker_runtime(execution)?;
    ensure_sticker_model(engine, execution)?;
    Ok(format!(
        "Sticker local runtime installed for {}.",
        local_sticker_engine_label(engine)
    ))
}

pub(crate) fn install_active_upscale_runtime(settings: &AppSettings) -> Result<String, String> {
    let upscale_settings =
        UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref());
    let execution = upscale_execution_provider(&upscale_settings);
    let engine = active_upscale_engine(&upscale_settings);
    ensure_upscale_runtime(execution)?;
    ensure_upscale_model(engine)?;
    Ok(format!(
        "Upscale local runtime installed for {}.",
        local_upscale_engine_label(engine)
    ))
}

pub(crate) fn remove_active_sticker_runtime(settings: &AppSettings) -> Result<String, String> {
    let sticker_settings =
        StickerSettings::from_json_value(settings.sticker_upload_settings.as_ref());
    remove_runtime(
        RuntimeKind::Sticker,
        sticker_execution_provider(&sticker_settings),
    )?;
    Ok("Sticker local runtime removed.".into())
}

pub(crate) fn remove_active_upscale_runtime(settings: &AppSettings) -> Result<String, String> {
    let upscale_settings =
        UpscaleSettings::from_json_value(settings.upscale_upload_settings.as_ref());
    remove_runtime(
        RuntimeKind::Upscale,
        upscale_execution_provider(&upscale_settings),
    )?;
    Ok("Upscale local runtime removed.".into())
}

fn process_local_sticker_capture(
    settings: &StickerSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    let execution = sticker_execution_provider(settings);
    let engine = active_sticker_engine(settings);
    if execution == ExecutionProvider::Gpu {
        match process_local_sticker_with_engine(
            engine,
            execution,
            settings.add_stroke,
            settings.add_shadow,
            input_path,
            output_path,
        ) {
            Ok(label) => return Ok(label),
            Err(gpu_error) => {
                let cpu_engine = parse_sticker_engine(&settings.local_cpu_engine);
                return process_local_sticker_with_engine(
                    cpu_engine,
                    ExecutionProvider::Cpu,
                    settings.add_stroke,
                    settings.add_shadow,
                    input_path,
                    output_path,
                )
                .map(|label| format!("{label} (CPU fallback)"))
                .map_err(|cpu_error| format!("{gpu_error} CPU fallback failed: {cpu_error}"));
            }
        }
    }
    process_local_sticker_with_engine(
        engine,
        execution,
        settings.add_stroke,
        settings.add_shadow,
        input_path,
        output_path,
    )
}

fn process_local_sticker_with_engine(
    engine: LocalStickerEngine,
    execution: ExecutionProvider,
    add_stroke: bool,
    add_shadow: bool,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    ensure_sticker_runtime(execution)?;
    ensure_sticker_model(engine, execution)?;
    let script = rembg_script(add_stroke, add_shadow);
    let result = run_runtime_python(
        RuntimeKind::Sticker,
        execution,
        &[
            "-c".into(),
            script,
            input_path.to_string_lossy().into_owned(),
            output_path.to_string_lossy().into_owned(),
            local_sticker_model_id(engine).into(),
        ],
        Some((&local_sticker_model_cache_dir(), "U2NET_HOME")),
    )?;
    if result.status_success && output_path.is_file() {
        return Ok(local_sticker_engine_label(engine).into());
    }
    Err(runtime_failure_message(
        result,
        "rembg failed to process the image.",
    ))
}

fn process_local_upscale_capture(
    settings: &UpscaleSettings,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    let execution = upscale_execution_provider(settings);
    let engine = active_upscale_engine(settings);
    if execution == ExecutionProvider::Gpu {
        match process_local_upscale_with_engine(
            engine,
            execution,
            settings.scale_factor,
            input_path,
            output_path,
        ) {
            Ok(label) => return Ok(label),
            Err(gpu_error) => {
                let cpu_engine = parse_upscale_engine(&settings.local_cpu_engine);
                return process_local_upscale_with_engine(
                    cpu_engine,
                    ExecutionProvider::Cpu,
                    settings.scale_factor,
                    input_path,
                    output_path,
                )
                .map(|label| format!("{label} (CPU fallback)"))
                .map_err(|cpu_error| format!("{gpu_error} CPU fallback failed: {cpu_error}"));
            }
        }
    }
    process_local_upscale_with_engine(
        engine,
        execution,
        settings.scale_factor,
        input_path,
        output_path,
    )
}

fn process_local_upscale_with_engine(
    engine: LocalUpscaleEngine,
    execution: ExecutionProvider,
    scale_factor: u32,
    input_path: &Path,
    output_path: &Path,
) -> Result<String, String> {
    ensure_upscale_runtime(execution)?;
    ensure_upscale_model(engine)?;
    let max_scale = local_upscale_engine_max_scale(engine);
    let requested_scale = scale_factor.clamp(2, max_scale);
    let result = run_runtime_python(
        RuntimeKind::Upscale,
        execution,
        &[
            "-c".into(),
            upscale_script(),
            input_path.to_string_lossy().into_owned(),
            output_path.to_string_lossy().into_owned(),
            local_upscale_model_path(engine)
                .to_string_lossy()
                .into_owned(),
            if execution == ExecutionProvider::Gpu {
                "gpu".into()
            } else {
                "cpu".into()
            },
            max_scale.to_string(),
            requested_scale.to_string(),
        ],
        None,
    )?;
    if result.status_success && output_path.is_file() {
        return Ok(local_upscale_engine_label(engine).into());
    }
    Err(runtime_failure_message(
        result,
        "Upscale processing failed.",
    ))
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum RuntimeKind {
    Sticker,
    Upscale,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum ExecutionProvider {
    Cpu,
    Gpu,
}

#[derive(Debug)]
struct ProcessOutput {
    status_success: bool,
    stdout: String,
    stderr: String,
}

#[derive(Debug, Clone)]
struct PythonLauncher {
    program: String,
    args: Vec<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum LocalStickerEngine {
    BriaRmbg,
    U2Netp,
    U2Net,
    BiRefNetLite,
    IsNetGeneralUse,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum LocalUpscaleEngine {
    SwinIrRealWorld,
    RealEsrganX4Plus,
}

fn ensure_sticker_runtime(execution: ExecutionProvider) -> Result<(), String> {
    ensure_python_runtime(
        RuntimeKind::Sticker,
        execution,
        &[
            REMBG_PACKAGE,
            NUMPY_PACKAGE,
            PILLOW_PACKAGE,
            if execution == ExecutionProvider::Gpu {
                ONNXRUNTIME_GPU_PACKAGE
            } else {
                ONNXRUNTIME_PACKAGE
            },
        ],
        Some(("U2NET_HOME", local_sticker_model_cache_dir())),
    )
    .or_else(|error| {
        if execution != ExecutionProvider::Gpu {
            return Err(error);
        }
        ensure_python_runtime(
            RuntimeKind::Sticker,
            execution,
            &[
                REMBG_PACKAGE,
                NUMPY_PACKAGE,
                PILLOW_PACKAGE,
                ONNXRUNTIME_PACKAGE,
            ],
            Some(("U2NET_HOME", local_sticker_model_cache_dir())),
        )
    })
}

fn ensure_upscale_runtime(execution: ExecutionProvider) -> Result<(), String> {
    ensure_python_runtime(
        RuntimeKind::Upscale,
        execution,
        &[
            if execution == ExecutionProvider::Gpu {
                ONNXRUNTIME_GPU_PACKAGE
            } else {
                ONNXRUNTIME_PACKAGE
            },
            NUMPY_PACKAGE,
            PILLOW_PACKAGE,
        ],
        None,
    )
    .or_else(|error| {
        if execution != ExecutionProvider::Gpu {
            return Err(error);
        }
        ensure_python_runtime(
            RuntimeKind::Upscale,
            execution,
            &[ONNXRUNTIME_PACKAGE, NUMPY_PACKAGE, PILLOW_PACKAGE],
            None,
        )
    })
}

fn ensure_python_runtime(
    kind: RuntimeKind,
    execution: ExecutionProvider,
    packages: &[&str],
    env: Option<(&str, PathBuf)>,
) -> Result<(), String> {
    if runtime_is_ready(kind, execution) {
        return Ok(());
    }

    let runtime_dir = runtime_environment_dir(kind, execution);
    if runtime_dir.exists() {
        fs::remove_dir_all(&runtime_dir).map_err(|error| {
            format!(
                "Couldn't recreate {} runtime directory: {error}",
                runtime_label(kind)
            )
        })?;
    }
    if let Some(parent) = runtime_dir.parent() {
        fs::create_dir_all(parent).map_err(|error| {
            format!(
                "Couldn't create {} runtime root: {error}",
                runtime_label(kind)
            )
        })?;
    }

    let launcher = resolve_python_launcher()
        .ok_or_else(|| "Python 3.10-3.12 was not found for local ML runtimes.".to_string())?;
    let mut create_args = launcher.args.clone();
    create_args.extend([
        "-m".into(),
        "venv".into(),
        runtime_dir.to_string_lossy().into_owned(),
    ]);
    let create = run_process(&launcher.program, &create_args, None)?;
    if !create.status_success {
        return Err(runtime_failure_message(
            create,
            "Couldn't create the isolated Python environment.",
        ));
    }

    let tools = run_runtime_python(
        kind,
        execution,
        &[
            "-m".into(),
            "pip".into(),
            "install".into(),
            "--disable-pip-version-check".into(),
            PIP_PACKAGE.into(),
            SETUPTOOLS_PACKAGE.into(),
            WHEEL_PACKAGE.into(),
        ],
        env.as_ref().map(|(key, value)| (value.as_path(), *key)),
    )?;
    if !tools.status_success {
        return Err(runtime_failure_message(
            tools,
            "Couldn't prepare pip inside the isolated runtime.",
        ));
    }

    let mut install_args = vec![
        "-m".into(),
        "pip".into(),
        "install".into(),
        "--disable-pip-version-check".into(),
        "--prefer-binary".into(),
    ];
    install_args.extend(packages.iter().map(|package| (*package).to_string()));
    let install = run_runtime_python(
        kind,
        execution,
        &install_args,
        env.as_ref().map(|(key, value)| (value.as_path(), *key)),
    )?;
    if !install.status_success {
        return Err(runtime_failure_message(
            install,
            &format!("Couldn't install the {} runtime.", runtime_label(kind)),
        ));
    }

    fs::write(runtime_marker_path(kind, execution), RUNTIME_LAYOUT_VERSION).map_err(|error| {
        format!(
            "Couldn't write {} runtime marker: {error}",
            runtime_label(kind)
        )
    })?;
    Ok(())
}

fn runtime_is_ready(kind: RuntimeKind, execution: ExecutionProvider) -> bool {
    let python = runtime_python_path(kind, execution);
    python.is_file()
        && fs::read_to_string(runtime_marker_path(kind, execution))
            .map(|marker| marker.trim() == RUNTIME_LAYOUT_VERSION)
            .unwrap_or(false)
}

fn local_runtime_status(kind: RuntimeKind, execution: ExecutionProvider) -> &'static str {
    if runtime_is_ready(kind, execution) {
        "runtime ready"
    } else {
        "runtime missing"
    }
}

fn remove_runtime(kind: RuntimeKind, execution: ExecutionProvider) -> Result<(), String> {
    let dir = runtime_environment_dir(kind, execution);
    match fs::remove_dir_all(&dir) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(format!(
            "Couldn't remove {} runtime: {error}",
            runtime_label(kind)
        )),
    }
}

fn ensure_sticker_model(
    engine: LocalStickerEngine,
    execution: ExecutionProvider,
) -> Result<(), String> {
    let model_path = local_sticker_model_path(engine);
    if model_path.is_file() {
        return Ok(());
    }
    fs::create_dir_all(local_sticker_model_cache_dir())
        .map_err(|error| format!("Couldn't create sticker model cache: {error}"))?;
    let result = run_runtime_python(
        RuntimeKind::Sticker,
        execution,
        &[
            "-c".into(),
            "import sys\nfrom rembg import new_session\nnew_session(sys.argv[1])\nprint('ok')\n"
                .into(),
            local_sticker_model_id(engine).into(),
        ],
        Some((&local_sticker_model_cache_dir(), "U2NET_HOME")),
    )?;
    if result.status_success && model_path.is_file() {
        Ok(())
    } else if result.status_success {
        Err(format!(
            "The sticker model did not finish downloading: {}",
            local_sticker_model_path(engine).display()
        ))
    } else {
        Err(runtime_failure_message(
            result,
            "Couldn't prepare the sticker model.",
        ))
    }
}

fn ensure_upscale_model(engine: LocalUpscaleEngine) -> Result<(), String> {
    let model_path = local_upscale_model_path(engine);
    if model_path.is_file() {
        return Ok(());
    }
    fs::create_dir_all(local_upscale_model_cache_dir())
        .map_err(|error| format!("Couldn't create upscale model cache: {error}"))?;
    let temp_path = model_path.with_extension("onnx.download");
    let download = build_image_download_curl_request(
        local_upscale_engine_label(engine),
        local_upscale_model_url(engine),
        &temp_path,
    )?;
    let (stdout, stderr) = run_curl_request(&download)?;
    parse_image_output_curl_status(local_upscale_engine_label(engine), &stdout, &temp_path)
        .map_err(|error| append_curl_stderr(error, &stderr))?;
    fs::rename(&temp_path, &model_path)
        .or_else(|_| {
            fs::copy(&temp_path, &model_path)?;
            fs::remove_file(&temp_path)
        })
        .map_err(|error| format!("Couldn't store upscale model: {error}"))
}

fn run_runtime_python(
    kind: RuntimeKind,
    execution: ExecutionProvider,
    args: &[String],
    env_path: Option<(&Path, &str)>,
) -> Result<ProcessOutput, String> {
    run_process(
        runtime_python_path(kind, execution)
            .to_string_lossy()
            .as_ref(),
        args,
        env_path,
    )
}

fn run_process(
    program: &str,
    args: &[String],
    env_path: Option<(&Path, &str)>,
) -> Result<ProcessOutput, String> {
    let mut command = Command::new(program);
    command
        .args(args)
        .env("PYTHONUTF8", "1")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some((path, key)) = env_path {
        command.env(key, path);
    }
    let output = command
        .output()
        .map_err(|error| format!("failed to start {program}: {error}"))?;
    Ok(ProcessOutput {
        status_success: output.status.success(),
        stdout: String::from_utf8_lossy(&output.stdout).to_string(),
        stderr: String::from_utf8_lossy(&output.stderr).to_string(),
    })
}

fn runtime_failure_message(output: ProcessOutput, fallback: &str) -> String {
    let stderr = output.stderr.trim();
    if !stderr.is_empty() {
        return stderr.into();
    }
    let stdout = output.stdout.trim();
    if !stdout.is_empty() {
        return stdout.into();
    }
    fallback.into()
}

fn resolve_python_launcher() -> Option<PythonLauncher> {
    #[cfg(target_os = "windows")]
    {
        for version in ["-3.12", "-3.11", "-3.10"] {
            if python_launcher_works("py", &[version.into()]) {
                return Some(PythonLauncher {
                    program: "py".into(),
                    args: vec![version.into()],
                });
            }
        }
    }

    for program in [
        "python3.12",
        "python3.11",
        "python3.10",
        "python3",
        "python",
    ] {
        if python_launcher_works(program, &[]) {
            return Some(PythonLauncher {
                program: program.into(),
                args: Vec::new(),
            });
        }
    }
    None
}

fn python_launcher_works(program: &str, prefix_args: &[String]) -> bool {
    let mut args = prefix_args.to_vec();
    args.extend([
        "-c".into(),
        "import sys; raise SystemExit(0 if (3, 10) <= sys.version_info[:2] <= (3, 12) else 1)"
            .into(),
    ]);
    Command::new(program)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .map(|status| status.success())
        .unwrap_or(false)
}

fn runtime_root(kind: RuntimeKind) -> PathBuf {
    app_data_root().join(match kind {
        RuntimeKind::Sticker => "rembg",
        RuntimeKind::Upscale => "upscale",
    })
}

fn runtime_environment_dir(kind: RuntimeKind, execution: ExecutionProvider) -> PathBuf {
    runtime_root(kind)
        .join("runtime")
        .join(if execution == ExecutionProvider::Gpu {
            "gpu"
        } else {
            "cpu"
        })
}

fn runtime_python_path(kind: RuntimeKind, execution: ExecutionProvider) -> PathBuf {
    let dir = runtime_environment_dir(kind, execution);
    #[cfg(target_os = "windows")]
    {
        dir.join("Scripts").join("python.exe")
    }
    #[cfg(not(target_os = "windows"))]
    {
        dir.join("bin").join("python")
    }
}

fn runtime_marker_path(kind: RuntimeKind, execution: ExecutionProvider) -> PathBuf {
    runtime_environment_dir(kind, execution).join(".oddsnap-runtime-version")
}

fn local_sticker_model_cache_dir() -> PathBuf {
    runtime_root(RuntimeKind::Sticker).join("models")
}

fn local_upscale_model_cache_dir() -> PathBuf {
    runtime_root(RuntimeKind::Upscale).join("models")
}

fn app_data_root() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Some(appdata) = std::env::var_os("APPDATA") {
            return PathBuf::from(appdata).join("OddSnap");
        }
    }
    #[cfg(target_os = "macos")]
    {
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("OddSnap");
        }
    }
    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        if let Some(data_home) = std::env::var_os("XDG_DATA_HOME") {
            return PathBuf::from(data_home).join("oddsnap");
        }
        if let Some(home) = std::env::var_os("HOME") {
            return PathBuf::from(home)
                .join(".local")
                .join("share")
                .join("oddsnap");
        }
    }
    std::env::temp_dir().join("OddSnap")
}

fn runtime_label(kind: RuntimeKind) -> &'static str {
    match kind {
        RuntimeKind::Sticker => "sticker",
        RuntimeKind::Upscale => "upscale",
    }
}

fn sticker_execution_provider(settings: &StickerSettings) -> ExecutionProvider {
    parse_execution_provider(&settings.local_execution_provider)
}

fn upscale_execution_provider(settings: &UpscaleSettings) -> ExecutionProvider {
    parse_execution_provider(&settings.local_execution_provider)
}

fn parse_execution_provider(value: &str) -> ExecutionProvider {
    if value.trim().eq_ignore_ascii_case("gpu") {
        ExecutionProvider::Gpu
    } else {
        ExecutionProvider::Cpu
    }
}

fn active_sticker_engine(settings: &StickerSettings) -> LocalStickerEngine {
    if sticker_execution_provider(settings) == ExecutionProvider::Gpu {
        parse_sticker_engine(&settings.local_gpu_engine)
    } else {
        parse_sticker_engine(&settings.local_cpu_engine)
    }
}

fn parse_sticker_engine(value: &str) -> LocalStickerEngine {
    match normalize_key(value).as_str() {
        "briarmbg" | "bria" | "rmbg" => LocalStickerEngine::BriaRmbg,
        "u2net" => LocalStickerEngine::U2Net,
        "birefnetlite" | "birefnetgenerallite" => LocalStickerEngine::BiRefNetLite,
        "isnetgeneraluse" | "isnet" => LocalStickerEngine::IsNetGeneralUse,
        _ => LocalStickerEngine::U2Netp,
    }
}

fn active_upscale_engine(settings: &UpscaleSettings) -> LocalUpscaleEngine {
    if upscale_execution_provider(settings) == ExecutionProvider::Gpu {
        parse_upscale_engine(&settings.local_gpu_engine)
    } else {
        parse_upscale_engine(&settings.local_cpu_engine)
    }
}

fn parse_upscale_engine(value: &str) -> LocalUpscaleEngine {
    match normalize_key(value).as_str() {
        "realesrganx4plus" | "realesrgan" => LocalUpscaleEngine::RealEsrganX4Plus,
        _ => LocalUpscaleEngine::SwinIrRealWorld,
    }
}

fn normalize_key(value: &str) -> String {
    value
        .chars()
        .filter(|ch| !matches!(ch, '_' | '-' | ' '))
        .collect::<String>()
        .to_ascii_lowercase()
}

fn local_sticker_engine_label(engine: LocalStickerEngine) -> &'static str {
    match engine {
        LocalStickerEngine::BriaRmbg => "BRIA RMBG",
        LocalStickerEngine::U2Netp => "U2Netp",
        LocalStickerEngine::U2Net => "U2Net",
        LocalStickerEngine::BiRefNetLite => "BiRefNet Lite",
        LocalStickerEngine::IsNetGeneralUse => "ISNet General Use",
    }
}

fn local_sticker_model_id(engine: LocalStickerEngine) -> &'static str {
    match engine {
        LocalStickerEngine::BriaRmbg => "bria-rmbg",
        LocalStickerEngine::U2Netp => "u2netp",
        LocalStickerEngine::U2Net => "u2net",
        LocalStickerEngine::BiRefNetLite => "birefnet-general-lite",
        LocalStickerEngine::IsNetGeneralUse => "isnet-general-use",
    }
}

fn local_sticker_model_file_name(engine: LocalStickerEngine) -> &'static str {
    match engine {
        LocalStickerEngine::BriaRmbg => "bria-rmbg.onnx",
        LocalStickerEngine::U2Netp => "u2netp.onnx",
        LocalStickerEngine::U2Net => "u2net.onnx",
        LocalStickerEngine::BiRefNetLite => "birefnet-general-lite.onnx",
        LocalStickerEngine::IsNetGeneralUse => "isnet-general-use.onnx",
    }
}

fn local_sticker_model_path(engine: LocalStickerEngine) -> PathBuf {
    let file_name = local_sticker_model_file_name(engine);
    let preferred = local_sticker_model_cache_dir().join(file_name);
    if preferred.is_file() {
        return preferred;
    }
    if let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) {
        let legacy = PathBuf::from(home).join(".u2net").join(file_name);
        if legacy.is_file() {
            return legacy;
        }
    }
    preferred
}

fn local_upscale_engine_label(engine: LocalUpscaleEngine) -> &'static str {
    match engine {
        LocalUpscaleEngine::SwinIrRealWorld => "SwinIR x4",
        LocalUpscaleEngine::RealEsrganX4Plus => "Real-ESRGAN x4plus",
    }
}

fn local_upscale_engine_max_scale(engine: LocalUpscaleEngine) -> u32 {
    match engine {
        LocalUpscaleEngine::SwinIrRealWorld | LocalUpscaleEngine::RealEsrganX4Plus => 4,
    }
}

fn local_upscale_model_file_name(engine: LocalUpscaleEngine) -> &'static str {
    match engine {
        LocalUpscaleEngine::SwinIrRealWorld => "swinir-realworld-x4.onnx",
        LocalUpscaleEngine::RealEsrganX4Plus => "real-esrgan-x4plus.onnx",
    }
}

fn local_upscale_model_path(engine: LocalUpscaleEngine) -> PathBuf {
    local_upscale_model_cache_dir().join(local_upscale_model_file_name(engine))
}

fn local_upscale_model_url(engine: LocalUpscaleEngine) -> &'static str {
    match engine {
        LocalUpscaleEngine::SwinIrRealWorld => "https://huggingface.co/rocca/swin-ir-onnx/resolve/main/003_realSR_BSRGAN_DFO_s64w8_SwinIR-M_x4_GAN.onnx?download=1",
        LocalUpscaleEngine::RealEsrganX4Plus => "https://huggingface.co/bukuroo/RealESRGAN-ONNX/resolve/main/real-esrgan-x4plus-128.onnx?download=1",
    }
}

fn rembg_script(add_stroke: bool, add_shadow: bool) -> String {
    format!(
        r#"
import sys
from rembg import remove, new_session
from PIL import Image, ImageFilter

input_path = sys.argv[1]
output_path = sys.argv[2]
model_name = sys.argv[3]

with open(input_path, "rb") as f:
    input_data = f.read()

session = new_session(model_name)
output_data = remove(input_data, session=session)
tmp_path = output_path + ".raw.png"
with open(tmp_path, "wb") as f:
    f.write(output_data)

image = Image.open(tmp_path).convert("RGBA")
add_stroke = {add_stroke}
add_shadow = {add_shadow}
if add_stroke or add_shadow:
    padding = (18 if add_shadow else 0) + (4 if add_stroke else 0)
    canvas = Image.new("RGBA", (image.width + padding * 2, image.height + padding * 2), (0, 0, 0, 0))
    alpha = image.getchannel("A")
    if add_shadow:
        for dx, dy, opacity in [(7, 8, 31), (5, 6, 23), (3, 4, 15)]:
            shadow = Image.new("RGBA", image.size, (0, 0, 0, opacity))
            shadow.putalpha(alpha.filter(ImageFilter.GaussianBlur(2)))
            canvas.alpha_composite(shadow, (padding + dx, padding + dy))
    if add_stroke:
        white = Image.new("RGBA", image.size, (255, 255, 255, 242))
        white.putalpha(alpha)
        for y in range(-3, 4):
            for x in range(-3, 4):
                if x == 0 and y == 0:
                    continue
                if x * x + y * y <= 9:
                    canvas.alpha_composite(white, (padding + x, padding + y))
    canvas.alpha_composite(image, (padding, padding))
    image = canvas
image.save(output_path)
"#
    )
}

fn upscale_script() -> String {
    r#"
import sys
import numpy as np
from PIL import Image
import onnxruntime as ort

input_path = sys.argv[1]
output_path = sys.argv[2]
model_path = sys.argv[3]
device = sys.argv[4]
native_scale = int(sys.argv[5])
requested_scale = int(sys.argv[6])

providers = ["CPUExecutionProvider"]
if device == "gpu" and "CUDAExecutionProvider" in ort.get_available_providers():
    providers = ["CUDAExecutionProvider", "CPUExecutionProvider"]

session = ort.InferenceSession(model_path, providers=providers)
input_name = session.get_inputs()[0].name
output_name = session.get_outputs()[0].name
input_shape = session.get_inputs()[0].shape
output_shape = session.get_outputs()[0].shape
if (
    len(input_shape) >= 4 and len(output_shape) >= 4 and
    isinstance(input_shape[2], int) and isinstance(input_shape[3], int) and
    isinstance(output_shape[2], int) and isinstance(output_shape[3], int) and
    input_shape[2] > 0 and input_shape[3] > 0
):
    native_scale = max(output_shape[2] // input_shape[2], output_shape[3] // input_shape[3], 1)

img = Image.open(input_path).convert("RGB")
arr = np.asarray(img).astype(np.float32) / 255.0
arr = np.transpose(arr, (2, 0, 1))[None, :, :, :]

window = 8
h = arr.shape[2]
w = arr.shape[3]
pad_h = (window - h % window) % window
pad_w = (window - w % window) % window
if pad_h or pad_w:
    arr = np.pad(arr, ((0, 0), (0, 0), (0, pad_h), (0, pad_w)), mode="reflect")

tile = 256
_, _, padded_h, padded_w = arr.shape
output = np.zeros((1, 3, padded_h * native_scale, padded_w * native_scale), dtype=np.float32)
weight = np.zeros_like(output)

expected_h = input_shape[2] if len(input_shape) >= 4 and isinstance(input_shape[2], int) and input_shape[2] > 0 else None
expected_w = input_shape[3] if len(input_shape) >= 4 and isinstance(input_shape[3], int) and input_shape[3] > 0 else None

for y in range(0, padded_h, tile):
    for x in range(0, padded_w, tile):
        input_tile = arr[:, :, y:min(y + tile, padded_h), x:min(x + tile, padded_w)]
        tile_h = input_tile.shape[2]
        tile_w = input_tile.shape[3]
        if expected_h is not None and expected_w is not None and (tile_h != expected_h or tile_w != expected_w):
            pad_h = max(0, expected_h - tile_h)
            pad_w = max(0, expected_w - tile_w)
            if pad_h or pad_w:
                input_tile = np.pad(input_tile, ((0, 0), (0, 0), (0, pad_h), (0, pad_w)), mode="reflect")
        output_tile = session.run([output_name], {input_name: input_tile})[0]
        crop_h = tile_h * native_scale
        crop_w = tile_w * native_scale
        output_tile = output_tile[:, :, :crop_h, :crop_w]
        out_y = y * native_scale
        out_x = x * native_scale
        out_h = output_tile.shape[2]
        out_w = output_tile.shape[3]
        output[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += output_tile
        weight[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += 1.0

output = output / np.maximum(weight, 1e-8)
output = output[:, :, :h * native_scale, :w * native_scale]
output = np.clip(output[0], 0.0, 1.0)
output = np.transpose(output, (1, 2, 0))
output = (output * 255.0).round().astype(np.uint8)
result = Image.fromarray(output)
if requested_scale != native_scale:
    result = result.resize((img.width * requested_scale, img.height * requested_scale), Image.Resampling.LANCZOS)
result.save(output_path)
"#
    .into()
}

#[cfg(test)]
mod tests {
    use super::*;
    use oddsnap_core::{StickerProvider, UpscaleProvider};

    #[test]
    fn local_runtime_summary_reports_status_without_installing() {
        let settings = AppSettings::default();

        let summary = sticker_upscale_runtime_status_summary(&settings);

        assert!(summary.contains("Sticker/upscale local"));
        assert!(summary.contains("sticker"));
        assert!(summary.contains("upscale"));
    }

    #[test]
    fn active_local_engines_follow_execution_provider() {
        let sticker = StickerSettings {
            local_execution_provider: "Gpu".into(),
            local_gpu_engine: "BiRefNetLite".into(),
            ..StickerSettings::default()
        };
        let upscale = UpscaleSettings {
            local_execution_provider: "Gpu".into(),
            local_gpu_engine: "RealEsrganX4Plus".into(),
            ..UpscaleSettings::default()
        };

        assert_eq!(
            active_sticker_engine(&sticker),
            LocalStickerEngine::BiRefNetLite
        );
        assert_eq!(
            active_upscale_engine(&upscale),
            LocalUpscaleEngine::RealEsrganX4Plus
        );
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
