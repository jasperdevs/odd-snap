#[derive(Debug, Clone, PartialEq)]
pub struct OcrLineLayout {
    pub text: String,
    pub left: f64,
    pub top: f64,
    pub right: f64,
    pub bottom: f64,
}

impl OcrLineLayout {
    pub fn new(text: impl Into<String>, left: f64, top: f64, right: f64, bottom: f64) -> Self {
        Self {
            text: text.into(),
            left,
            top,
            right,
            bottom,
        }
    }

    pub fn width(&self) -> f64 {
        (self.right - self.left).max(0.0)
    }

    pub fn height(&self) -> f64 {
        (self.bottom - self.top).max(0.0)
    }
}

pub fn format_recognized_ocr_text(lines: &[OcrLineLayout], fallback_text: Option<&str>) -> String {
    if lines.is_empty() {
        return fallback_text.unwrap_or_default().trim().to_string();
    }

    let mut ordered = lines
        .iter()
        .filter(|line| !line.text.trim().is_empty())
        .collect::<Vec<_>>();
    ordered.sort_by(|left, right| {
        left.top
            .total_cmp(&right.top)
            .then_with(|| left.left.total_cmp(&right.left))
    });

    if ordered.is_empty() {
        return fallback_text.unwrap_or_default().trim().to_string();
    }

    let mut heights = ordered
        .iter()
        .map(|line| line.height())
        .filter(|value| *value > 0.0)
        .collect::<Vec<_>>();
    let mut median_height = median(&mut heights);
    if median_height <= 0.0 {
        median_height = 16.0;
    }

    let mut char_widths = ordered
        .iter()
        .filter_map(|line| {
            let length = line.text.trim().chars().count();
            (length > 0 && line.width() > 0.0).then_some(line.width() / length as f64)
        })
        .filter(|value| *value > 0.0)
        .collect::<Vec<_>>();
    let mut median_char_width = median(&mut char_widths);
    if median_char_width <= 0.0 {
        median_char_width = 6.0_f64.max(median_height * 0.45);
    }

    let min_left = ordered
        .iter()
        .map(|line| line.left)
        .fold(f64::INFINITY, f64::min);
    let baseline_window = (median_char_width * 2.0).max(8.0);
    let baseline_candidates = ordered
        .iter()
        .filter_map(|line| (line.left - min_left <= baseline_window).then_some(line.left))
        .collect::<Vec<_>>();
    let baseline_left = if baseline_candidates.is_empty() {
        min_left
    } else {
        baseline_candidates.iter().sum::<f64>() / baseline_candidates.len() as f64
    };

    let mut output = String::new();
    let mut previous: Option<&OcrLineLayout> = None;

    for line in ordered {
        let text = line.text.trim();
        if text.is_empty() {
            continue;
        }

        let paragraph_break = previous
            .map(|prior| (line.top - prior.bottom) > (median_height * 0.85).max(8.0))
            .unwrap_or(false);
        let indent_spaces = compute_indent_spaces(line.left - baseline_left, median_char_width);
        let previous_indent = previous
            .map(|prior| compute_indent_spaces(prior.left - baseline_left, median_char_width))
            .unwrap_or(0);
        let paragraph_start =
            previous.is_none() || paragraph_break || indent_spaces >= previous_indent + 2;

        if !output.is_empty() {
            if paragraph_start {
                output.push_str("\n\n");
            } else {
                output.push('\n');
            }
        }

        if paragraph_start && indent_spaces >= 2 {
            output.push_str(&" ".repeat(indent_spaces.clamp(2, 8) as usize));
        }

        output.push_str(text);
        previous = Some(line);
    }

    output.trim().to_string()
}

fn compute_indent_spaces(indent_pixels: f64, median_char_width: f64) -> i32 {
    if indent_pixels <= 0.0 || median_char_width <= 0.0 {
        return 0;
    }

    (indent_pixels / median_char_width).round() as i32
}

fn median(values: &mut [f64]) -> f64 {
    if values.is_empty() {
        return 0.0;
    }

    values.sort_by(f64::total_cmp);
    let mid = values.len() / 2;
    if values.len() & 1 == 1 {
        values[mid]
    } else {
        (values[mid - 1] + values[mid]) / 2.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ocr_formatter_uses_fallback_when_no_layout_lines_exist() {
        assert_eq!(
            format_recognized_ocr_text(&[], Some("  fallback text  ")),
            "fallback text"
        );
        assert_eq!(
            format_recognized_ocr_text(&[OcrLineLayout::new(" ", 0.0, 0.0, 0.0, 0.0)], Some("raw")),
            "raw"
        );
    }

    #[test]
    fn ocr_formatter_orders_lines_by_position() {
        let lines = vec![
            OcrLineLayout::new("second", 10.0, 30.0, 70.0, 46.0),
            OcrLineLayout::new("first", 10.0, 10.0, 60.0, 26.0),
        ];

        assert_eq!(format_recognized_ocr_text(&lines, None), "first\nsecond");
    }

    #[test]
    fn ocr_formatter_preserves_paragraph_breaks_from_vertical_gap() {
        let lines = vec![
            OcrLineLayout::new("Heading", 10.0, 10.0, 80.0, 26.0),
            OcrLineLayout::new("Next paragraph", 10.0, 48.0, 150.0, 64.0),
        ];

        assert_eq!(
            format_recognized_ocr_text(&lines, None),
            "Heading\n\nNext paragraph"
        );
    }

    #[test]
    fn ocr_formatter_preserves_indented_paragraph_starts() {
        let lines = vec![
            OcrLineLayout::new("Root", 10.0, 10.0, 50.0, 26.0),
            OcrLineLayout::new("Indented", 42.0, 28.0, 122.0, 44.0),
            OcrLineLayout::new("Still indented", 42.0, 46.0, 162.0, 62.0),
        ];

        assert_eq!(
            format_recognized_ocr_text(&lines, None),
            "Root\n\n   Indented\nStill indented"
        );
    }

    #[test]
    fn ocr_formatter_trims_empty_text_lines() {
        let lines = vec![
            OcrLineLayout::new("  alpha  ", 10.0, 10.0, 70.0, 26.0),
            OcrLineLayout::new("", 10.0, 28.0, 10.0, 28.0),
            OcrLineLayout::new("beta", 10.0, 30.0, 50.0, 46.0),
        ];

        assert_eq!(format_recognized_ocr_text(&lines, None), "alpha\nbeta");
    }
}
