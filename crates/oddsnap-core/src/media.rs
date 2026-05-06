use std::path::{Path, PathBuf};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FfmpegTools {
    pub ffmpeg: PathBuf,
    pub ffprobe: Option<PathBuf>,
}

pub fn discover_ffmpeg_tools() -> Option<FfmpegTools> {
    let path_var = std::env::var_os("PATH")?;
    discover_ffmpeg_tools_in_path(&path_var.to_string_lossy(), std::env::consts::EXE_SUFFIX)
}

pub fn discover_ffmpeg_tools_in_path(path_var: &str, exe_suffix: &str) -> Option<FfmpegTools> {
    let ffmpeg = find_executable_in_path(path_var, executable_name("ffmpeg", exe_suffix))?;
    let ffprobe = find_executable_in_path(path_var, executable_name("ffprobe", exe_suffix));

    Some(FfmpegTools { ffmpeg, ffprobe })
}

fn executable_name(name: &'static str, exe_suffix: &str) -> String {
    if exe_suffix.is_empty() || name.ends_with(exe_suffix) {
        name.to_string()
    } else {
        format!("{name}{exe_suffix}")
    }
}

fn find_executable_in_path(path_var: &str, executable: String) -> Option<PathBuf> {
    std::env::split_paths(path_var)
        .map(|directory| directory.join(&executable))
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

    use super::discover_ffmpeg_tools_in_path;

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
}
