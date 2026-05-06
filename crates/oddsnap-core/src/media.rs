use std::path::{Path, PathBuf};

use crate::{RecordingFormat, RecordingQuality};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FfmpegTools {
    pub ffmpeg: PathBuf,
    pub ffprobe: Option<PathBuf>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FfmpegRecordingRequest {
    pub input_args: Vec<String>,
    pub output_path: PathBuf,
    pub format: RecordingFormat,
    pub quality: RecordingQuality,
    pub fps: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FfmpegThumbnailRequest {
    pub input_path: PathBuf,
    pub output_path: PathBuf,
    pub seek_seconds: Option<String>,
}

pub fn build_recording_output_args(request: &FfmpegRecordingRequest) -> Vec<String> {
    let fps = request.fps.clamp(1, 240).to_string();
    let mut args = vec!["-y".to_string()];
    args.extend(request.input_args.iter().cloned());
    args.extend(["-r".to_string(), fps]);

    if let Some(height) = request.quality.max_height() {
        args.extend(["-vf".to_string(), format!("scale=-2:{height}")]);
    }

    match request.format {
        RecordingFormat::Gif => {
            args.extend(["-loop".to_string(), "0".to_string()]);
        }
        RecordingFormat::Mp4 => {
            args.extend([
                "-c:v".to_string(),
                "libx264".to_string(),
                "-pix_fmt".to_string(),
                "yuv420p".to_string(),
                "-movflags".to_string(),
                "+faststart".to_string(),
            ]);
        }
        RecordingFormat::WebM => {
            args.extend([
                "-c:v".to_string(),
                "libvpx-vp9".to_string(),
                "-pix_fmt".to_string(),
                "yuv420p".to_string(),
            ]);
        }
        RecordingFormat::Mkv => {
            args.extend([
                "-c:v".to_string(),
                "libx264".to_string(),
                "-pix_fmt".to_string(),
                "yuv420p".to_string(),
            ]);
        }
    }

    args.push(request.output_path.display().to_string());
    args
}

pub fn build_video_thumbnail_args(request: &FfmpegThumbnailRequest) -> Vec<String> {
    let mut args = vec!["-y".to_string()];
    if let Some(seek_seconds) = &request.seek_seconds {
        args.extend(["-ss".to_string(), seek_seconds.clone()]);
    }
    args.extend([
        "-i".to_string(),
        request.input_path.display().to_string(),
        "-vf".to_string(),
        "scale=480:-1".to_string(),
        "-vframes".to_string(),
        "1".to_string(),
        "-q:v".to_string(),
        "3".to_string(),
        request.output_path.display().to_string(),
    ]);
    args
}

pub fn build_video_thumbnail_fallback_args(request: &FfmpegThumbnailRequest) -> Vec<String> {
    vec![
        "-y".to_string(),
        "-i".to_string(),
        request.input_path.display().to_string(),
        "-vf".to_string(),
        "thumbnail=24,scale=480:-1".to_string(),
        "-frames:v".to_string(),
        "1".to_string(),
        "-q:v".to_string(),
        "3".to_string(),
        request.output_path.display().to_string(),
    ]
}

pub fn discover_ffmpeg_tools() -> Option<FfmpegTools> {
    let path_var = std::env::var_os("PATH").unwrap_or_default();
    discover_ffmpeg_tools_in_locations(
        &path_var.to_string_lossy(),
        std::env::consts::EXE_SUFFIX,
        default_ffmpeg_candidate_directories(),
    )
}

pub fn discover_ffmpeg_tools_in_path(path_var: &str, exe_suffix: &str) -> Option<FfmpegTools> {
    discover_ffmpeg_tools_in_locations(path_var, exe_suffix, [])
}

pub fn discover_ffmpeg_tools_in_locations(
    path_var: &str,
    exe_suffix: &str,
    candidate_directories: impl IntoIterator<Item = PathBuf>,
) -> Option<FfmpegTools> {
    let ffmpeg_name = executable_name("ffmpeg", exe_suffix);
    let ffprobe_name = executable_name("ffprobe", exe_suffix);
    let candidate_directories: Vec<PathBuf> = candidate_directories.into_iter().collect();
    let ffmpeg = find_executable_in_directories(
        candidate_directories.iter().map(PathBuf::as_path),
        &ffmpeg_name,
    )
    .or_else(|| find_executable_in_path(path_var, &ffmpeg_name))?;
    let ffprobe = ffmpeg
        .parent()
        .and_then(|directory| find_executable_in_directories([directory], &ffprobe_name))
        .or_else(|| {
            find_executable_in_directories(
                candidate_directories.iter().map(PathBuf::as_path),
                &ffprobe_name,
            )
        })
        .or_else(|| find_executable_in_path(path_var, &ffprobe_name));

    Some(FfmpegTools { ffmpeg, ffprobe })
}

fn default_ffmpeg_candidate_directories() -> Vec<PathBuf> {
    let mut directories = Vec::new();

    if let Ok(current_exe) = std::env::current_exe() {
        if let Some(directory) = current_exe.parent() {
            directories.push(directory.to_path_buf());
        }
    }

    #[cfg(target_os = "windows")]
    {
        if let Some(appdata) = std::env::var_os("APPDATA") {
            directories.push(PathBuf::from(appdata).join("OddSnap"));
        }
    }

    directories
}

fn executable_name(name: &'static str, exe_suffix: &str) -> String {
    if exe_suffix.is_empty() || name.ends_with(exe_suffix) {
        name.to_string()
    } else {
        format!("{name}{exe_suffix}")
    }
}

fn find_executable_in_path(path_var: &str, executable: &str) -> Option<PathBuf> {
    std::env::split_paths(path_var)
        .map(|directory| directory.join(executable))
        .find(|candidate| is_file(candidate))
}

fn find_executable_in_directories<'a>(
    directories: impl IntoIterator<Item = &'a Path>,
    executable: &str,
) -> Option<PathBuf> {
    directories
        .into_iter()
        .map(|directory| directory.join(executable))
        .find(|candidate| is_file(candidate))
}

fn is_file(path: &Path) -> bool {
    path.metadata()
        .map(|metadata| metadata.is_file())
        .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;

    use super::{
        build_recording_output_args, build_video_thumbnail_args,
        build_video_thumbnail_fallback_args, discover_ffmpeg_tools_in_locations,
        discover_ffmpeg_tools_in_path, FfmpegRecordingRequest, FfmpegThumbnailRequest,
    };
    use crate::{RecordingFormat, RecordingQuality};

    #[test]
    fn discovers_ffmpeg_and_ffprobe_from_path() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-ffmpeg-discovery-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let bin = root.join("bin");
        fs::create_dir_all(&bin).expect("create bin dir");
        let suffix = std::env::consts::EXE_SUFFIX;
        let ffmpeg = bin.join(format!("ffmpeg{suffix}"));
        let ffprobe = bin.join(format!("ffprobe{suffix}"));
        fs::write(&ffmpeg, b"ffmpeg").expect("write ffmpeg");
        fs::write(&ffprobe, b"ffprobe").expect("write ffprobe");
        let path_var = std::env::join_paths([bin.as_path()])
            .expect("join PATH")
            .to_string_lossy()
            .to_string();

        let tools = discover_ffmpeg_tools_in_path(&path_var, suffix).expect("discover ffmpeg");

        assert_eq!(tools.ffmpeg, ffmpeg);
        assert_eq!(tools.ffprobe, Some(ffprobe));
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn discovery_requires_ffmpeg_but_not_ffprobe() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-ffmpeg-optional-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let suffix = std::env::consts::EXE_SUFFIX;
        let ffmpeg = root.join(format!("ffmpeg{suffix}"));
        fs::write(&ffmpeg, b"ffmpeg").expect("write ffmpeg");
        let path_var = std::env::join_paths([root.as_path()])
            .expect("join PATH")
            .to_string_lossy()
            .to_string();

        let tools = discover_ffmpeg_tools_in_path(&path_var, suffix).expect("discover ffmpeg");

        assert_eq!(tools.ffmpeg, ffmpeg);
        assert_eq!(tools.ffprobe, None);
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn discovery_prefers_candidate_directory_and_uses_neighboring_ffprobe() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-ffmpeg-candidate-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        let bundled = root.join("bundled");
        let path_bin = root.join("path");
        fs::create_dir_all(&bundled).expect("create bundled dir");
        fs::create_dir_all(&path_bin).expect("create path dir");
        let suffix = std::env::consts::EXE_SUFFIX;
        let bundled_ffmpeg = bundled.join(format!("ffmpeg{suffix}"));
        let bundled_ffprobe = bundled.join(format!("ffprobe{suffix}"));
        let path_ffmpeg = path_bin.join(format!("ffmpeg{suffix}"));
        fs::write(&bundled_ffmpeg, b"bundled ffmpeg").expect("write bundled ffmpeg");
        fs::write(&bundled_ffprobe, b"bundled ffprobe").expect("write bundled ffprobe");
        fs::write(&path_ffmpeg, b"path ffmpeg").expect("write path ffmpeg");
        let path_var = std::env::join_paths([path_bin.as_path()])
            .expect("join PATH")
            .to_string_lossy()
            .to_string();

        let tools = discover_ffmpeg_tools_in_locations(&path_var, suffix, [bundled.clone()])
            .expect("discover bundled ffmpeg");

        assert_eq!(tools.ffmpeg, bundled_ffmpeg);
        assert_eq!(tools.ffprobe, Some(bundled_ffprobe));
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn discovery_returns_none_without_ffmpeg() {
        let root =
            std::env::temp_dir().join(format!("oddsnap-ffmpeg-missing-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create temp root");
        let path_var = std::env::join_paths([root.as_path()])
            .expect("join PATH")
            .to_string_lossy()
            .to_string();

        let tools = discover_ffmpeg_tools_in_path(&path_var, std::env::consts::EXE_SUFFIX);

        assert!(tools.is_none());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn builds_mp4_recording_output_args_with_quality_scale() {
        let args = build_recording_output_args(&FfmpegRecordingRequest {
            input_args: vec!["-f".into(), "gdigrab".into(), "-i".into(), "desktop".into()],
            output_path: PathBuf::from("capture.mp4"),
            format: RecordingFormat::Mp4,
            quality: RecordingQuality::P720,
            fps: 30,
        });

        assert_eq!(args[0], "-y");
        assert!(args.windows(2).any(|pair| pair == ["-r", "30"]));
        assert!(args.windows(2).any(|pair| pair == ["-vf", "scale=-2:720"]));
        assert!(args.windows(2).any(|pair| pair == ["-c:v", "libx264"]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["-movflags", "+faststart"]));
        assert_eq!(args.last().map(String::as_str), Some("capture.mp4"));
    }

    #[test]
    fn builds_format_specific_recording_output_args() {
        let gif_args = build_recording_output_args(&FfmpegRecordingRequest {
            input_args: vec!["-i".into(), "pipe:0".into()],
            output_path: PathBuf::from("capture.gif"),
            format: RecordingFormat::Gif,
            quality: RecordingQuality::Original,
            fps: 999,
        });
        let webm_args = build_recording_output_args(&FfmpegRecordingRequest {
            input_args: vec!["-i".into(), "pipe:0".into()],
            output_path: PathBuf::from("capture.webm"),
            format: RecordingFormat::WebM,
            quality: RecordingQuality::Original,
            fps: 60,
        });
        let mkv_args = build_recording_output_args(&FfmpegRecordingRequest {
            input_args: vec!["-i".into(), "pipe:0".into()],
            output_path: PathBuf::from("capture.mkv"),
            format: RecordingFormat::Mkv,
            quality: RecordingQuality::Original,
            fps: 0,
        });

        assert!(gif_args.windows(2).any(|pair| pair == ["-loop", "0"]));
        assert!(gif_args.windows(2).any(|pair| pair == ["-r", "240"]));
        assert!(webm_args
            .windows(2)
            .any(|pair| pair == ["-c:v", "libvpx-vp9"]));
        assert!(mkv_args.windows(2).any(|pair| pair == ["-c:v", "libx264"]));
        assert!(mkv_args.windows(2).any(|pair| pair == ["-r", "1"]));
    }

    #[test]
    fn builds_video_thumbnail_args_with_legacy_filters() {
        let request = FfmpegThumbnailRequest {
            input_path: PathBuf::from("capture.mp4"),
            output_path: PathBuf::from("thumb.jpg"),
            seek_seconds: Some("0.40".into()),
        };

        let args = build_video_thumbnail_args(&request);
        let fallback_args = build_video_thumbnail_fallback_args(&request);

        assert!(args.windows(2).any(|pair| pair == ["-ss", "0.40"]));
        assert!(args.windows(2).any(|pair| pair == ["-vf", "scale=480:-1"]));
        assert!(args.windows(2).any(|pair| pair == ["-q:v", "3"]));
        assert_eq!(args.last().map(String::as_str), Some("thumb.jpg"));
        assert!(fallback_args
            .windows(2)
            .any(|pair| pair == ["-vf", "thumbnail=24,scale=480:-1"]));
        assert!(fallback_args
            .windows(2)
            .any(|pair| pair == ["-frames:v", "1"]));
    }
}
