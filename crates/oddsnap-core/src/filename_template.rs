use std::{
    path::{Path, PathBuf},
    sync::OnceLock,
};

use chrono::{DateTime, Datelike, Local, Timelike};
use regex::Regex;

pub const DEFAULT_FILE_NAME_TEMPLATE: &str = "{year}-{month}-{day}-{hour}-{min}-{sec}-{rand}";
pub const LEGACY_DEFAULT_FILE_NAME_TEMPLATE: &str =
    "oddsnap_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}";

pub fn normalize_file_name_template(template: &str) -> String {
    let trimmed = template.trim();
    if trimmed.is_empty() || trimmed == LEGACY_DEFAULT_FILE_NAME_TEMPLATE {
        DEFAULT_FILE_NAME_TEMPLATE.into()
    } else {
        template.into()
    }
}

pub fn format_file_name_template(template: &str, width: u32, height: u32) -> String {
    let now = Local::now();
    let random_token = random_file_name_token(&now);
    render_file_name_template(template, &now, &random_token, width, height)
}

pub fn build_available_capture_path(
    root_directory: &Path,
    file_name: &str,
    use_monthly_folder: bool,
) -> PathBuf {
    let directory = if use_monthly_folder {
        month_directory(root_directory, &Local::now())
    } else {
        root_directory.to_path_buf()
    };
    available_path(directory.join(file_name))
}

fn render_file_name_template(
    template: &str,
    now: &DateTime<Local>,
    random_token: &str,
    width: u32,
    height: u32,
) -> String {
    let blank_template = template.trim().is_empty();
    let mut template = normalize_legacy_placeholders(template);
    if blank_template {
        template = DEFAULT_FILE_NAME_TEMPLATE.into();
    }

    let replacements = [
        ("{datetime}", now.format("%Y%m%d_%H%M%S").to_string()),
        ("{date}", now.format("%Y%m%d").to_string()),
        ("{time}", now.format("%H%M%S").to_string()),
        ("{year}", format!("{:04}", now.year())),
        ("{month}", format!("{:02}", now.month())),
        ("{day}", format!("{:02}", now.day())),
        ("{hour}", format!("{:02}", now.hour())),
        ("{min}", format!("{:02}", now.minute())),
        ("{sec}", format!("{:02}", now.second())),
        (
            "{w}",
            if width > 0 {
                width.to_string()
            } else {
                String::new()
            },
        ),
        (
            "{h}",
            if height > 0 {
                height.to_string()
            } else {
                String::new()
            },
        ),
        ("{aspect}", format_aspect_ratio(width, height)),
        ("{rand}", random_token.into()),
    ];

    let mut result = template;
    for (token, value) in replacements {
        result = result.replace(token, &value);
    }

    sanitize_file_stem(&result, now, random_token)
}

fn normalize_legacy_placeholders(template: &str) -> String {
    if template.trim().is_empty() {
        return "{datetime}_{rand}".into();
    }

    replace_loose_placeholder(
        &replace_loose_placeholder(template, "rand", "{rand}"),
        "datetime",
        "{datetime}",
    )
}

fn replace_loose_placeholder(template: &str, token: &str, replacement: &str) -> String {
    let escaped = regex::escape(token);
    let parenthesized =
        Regex::new(&format!(r"(?i)\(\s*{escaped}\s*\)")).expect("valid placeholder regex");
    let bracketed =
        Regex::new(&format!(r"(?i)\[\s*{escaped}\s*\]")).expect("valid placeholder regex");
    let loose = Regex::new(&format!(
        r"(?i)(?P<prefix>^|[^A-Za-z0-9{{\[(]){escaped}(?P<suffix>$|[^A-Za-z0-9}}\])])"
    ))
    .expect("valid placeholder regex");

    let template = parenthesized.replace_all(template, replacement);
    let template = bracketed.replace_all(&template, replacement);
    loose
        .replace_all(&template, format!("$prefix{replacement}$suffix"))
        .into_owned()
}

fn sanitize_file_stem(value: &str, now: &DateTime<Local>, random_token: &str) -> String {
    let mut result = value
        .chars()
        .map(|character| {
            if is_invalid_file_name_char(character) {
                '_'
            } else {
                character
            }
        })
        .collect::<String>();

    while result.contains("__") {
        result = result.replace("__", "_");
    }

    let result = result.trim_matches(['_', '-', '.', ' ']).to_string();
    if result.trim().is_empty() || result.eq_ignore_ascii_case("oddsnap") {
        format!("oddsnap_{}_{random_token}", now.format("%Y-%m-%d_%H-%M-%S"))
    } else {
        result
    }
}

fn is_invalid_file_name_char(character: char) -> bool {
    matches!(
        character,
        '<' | '>' | ':' | '"' | '/' | '\\' | '|' | '?' | '*' | '\0'
    ) || character.is_control()
}

fn format_aspect_ratio(width: u32, height: u32) -> String {
    if width == 0 || height == 0 {
        return String::new();
    }

    let gcd = greatest_common_divisor(width, height);
    format!("{}x{}", width / gcd, height / gcd)
}

fn greatest_common_divisor(mut a: u32, mut b: u32) -> u32 {
    while b != 0 {
        let next = a % b;
        a = b;
        b = next;
    }
    a.max(1)
}

fn random_file_name_token(now: &DateTime<Local>) -> String {
    format!(
        "{:04x}",
        ((now.timestamp_subsec_nanos() ^ std::process::id()) & 0xffff) as u16
    )
}

fn month_directory(root_directory: &Path, now: &DateTime<Local>) -> PathBuf {
    root_directory.join(now.format("%Y-%m").to_string())
}

fn available_path(path: PathBuf) -> PathBuf {
    if !path.exists() {
        return path;
    }

    let directory = path.parent().unwrap_or_else(|| Path::new(""));
    let file_stem = path
        .file_stem()
        .and_then(|stem| stem.to_str())
        .unwrap_or("");
    let extension = path.extension().and_then(|extension| extension.to_str());

    for index in 2..10_000 {
        let candidate = match extension {
            Some(extension) if !extension.is_empty() => {
                directory.join(format!("{file_stem} ({index}).{extension}"))
            }
            _ => directory.join(format!("{file_stem} ({index})")),
        };
        if !candidate.exists() {
            return candidate;
        }
    }

    directory.join(format!(
        "{file_stem} ({:x}).tmp",
        collision_fallback_token()
    ))
}

fn collision_fallback_token() -> u128 {
    static COUNTER: OnceLock<std::sync::atomic::AtomicU64> = OnceLock::new();
    let counter = COUNTER
        .get_or_init(|| std::sync::atomic::AtomicU64::new(0))
        .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    now ^ u128::from(counter)
}

#[cfg(test)]
mod tests {
    use chrono::TimeZone;

    use super::{
        available_path, build_available_capture_path, format_aspect_ratio,
        normalize_file_name_template, render_file_name_template, DEFAULT_FILE_NAME_TEMPLATE,
        LEGACY_DEFAULT_FILE_NAME_TEMPLATE,
    };
    use std::fs;

    #[test]
    fn normalizes_blank_and_legacy_default_templates() {
        assert_eq!(
            normalize_file_name_template("   "),
            DEFAULT_FILE_NAME_TEMPLATE
        );
        assert_eq!(
            normalize_file_name_template(LEGACY_DEFAULT_FILE_NAME_TEMPLATE),
            DEFAULT_FILE_NAME_TEMPLATE
        );
    }

    #[test]
    fn render_replaces_tokens_and_sanitizes_invalid_filename_characters() {
        let now = chrono::Local
            .with_ymd_and_hms(2026, 4, 5, 14, 30, 52)
            .single()
            .expect("valid date");

        let value = render_file_name_template(
            "Screenshot {day}/{month}/{year} {hour}:{min} {w}x{h} {aspect} {rand}",
            &now,
            "a3f1",
            1920,
            1080,
        );

        assert_eq!(value, "Screenshot 05_04_2026 14_30 1920x1080 16x9 a3f1");
    }

    #[test]
    fn render_replaces_loose_rand_placeholders() {
        let now = chrono::Local
            .with_ymd_and_hms(2026, 4, 5, 14, 30, 52)
            .single()
            .expect("valid date");

        let value = render_file_name_template("oddsnap_rand", &now, "a3f1", 0, 0);

        assert_eq!(value, "oddsnap_a3f1");
    }

    #[test]
    fn format_aspect_ratio_reduces_common_dimensions() {
        assert_eq!(format_aspect_ratio(1920, 1080), "16x9");
        assert_eq!(format_aspect_ratio(1000, 1000), "1x1");
        assert_eq!(format_aspect_ratio(1200, 800), "3x2");
    }

    #[test]
    fn build_available_capture_path_uses_monthly_folder() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-core-month-path-test-{}",
            std::process::id()
        ));
        let path = build_available_capture_path(&root, "capture.png", true);
        let month = path
            .parent()
            .and_then(|path| path.file_name())
            .and_then(|name| name.to_str())
            .expect("monthly folder");

        assert_eq!(month.len(), 7);
        assert_eq!(&month[4..5], "-");
        assert!(path.ends_with("capture.png"));
    }

    #[test]
    fn available_path_appends_counter_when_file_exists() {
        let root = std::env::temp_dir().join(format!(
            "oddsnap-core-available-path-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).expect("create root");
        let existing = root.join("Screenshot.png");
        fs::write(&existing, "").expect("write existing");

        let path = available_path(existing);

        assert_eq!(path, root.join("Screenshot (2).png"));
        let _ = fs::remove_dir_all(root);
    }
}
