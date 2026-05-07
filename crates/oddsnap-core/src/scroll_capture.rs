use image::{Rgba, RgbaImage};

const DUPLICATE_THRESHOLD: f64 = 0.985;
const MINIMUM_AUTO_NEW_CONTENT_PIXELS: u32 = 24;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScrollingCaptureMode {
    Automatic,
    Manual,
}

impl ScrollingCaptureMode {
    pub fn from_setting(value: &str) -> Self {
        if value.eq_ignore_ascii_case("manual") {
            Self::Manual
        } else {
            Self::Automatic
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Automatic => "Automatic",
            Self::Manual => "Manual",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ScrollAppendPlan {
    pub new_content_height: u32,
    pub match_count: u32,
    pub match_index: u32,
    pub ignore_bottom_offset: u32,
    pub used_best_guess: bool,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ScrollAppendHints {
    pub best_match_count: u32,
    pub best_match_index: u32,
    pub best_ignore_bottom_offset: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScrollFrameCaptureResult {
    Accepted,
    Pending,
    Duplicate,
    Rejected,
}

#[derive(Debug)]
pub struct ScrollCaptureSession {
    mode: ScrollingCaptureMode,
    stitched: Option<RgbaImage>,
    previous_frame: Option<RgbaImage>,
    pending_auto_frame: Option<RgbaImage>,
    frame_count: u32,
    hints: ScrollAppendHints,
}

impl ScrollCaptureSession {
    pub fn new(mode: ScrollingCaptureMode) -> Self {
        Self {
            mode,
            stitched: None,
            previous_frame: None,
            pending_auto_frame: None,
            frame_count: 0,
            hints: ScrollAppendHints::default(),
        }
    }

    pub fn mode(&self) -> ScrollingCaptureMode {
        self.mode
    }

    pub fn frame_count(&self) -> u32 {
        self.frame_count
    }

    pub fn capture_frame(
        &mut self,
        frame: RgbaImage,
        force_accept: bool,
    ) -> ScrollFrameCaptureResult {
        if force_accept || self.mode == ScrollingCaptureMode::Manual {
            self.pending_auto_frame = None;
            return self.try_accept_frame(frame, force_accept);
        }

        self.process_automatic_frame(frame)
    }

    pub fn finish(mut self) -> Option<RgbaImage> {
        if let Some(frame) = self.pending_auto_frame.take() {
            let _ = self.try_accept_frame(frame, false);
        }

        self.stitched
    }

    fn process_automatic_frame(&mut self, frame: RgbaImage) -> ScrollFrameCaptureResult {
        if self
            .previous_frame
            .as_ref()
            .is_some_and(|previous| are_frames_duplicate(previous, &frame))
        {
            self.pending_auto_frame = None;
            return ScrollFrameCaptureResult::Duplicate;
        }

        let should_keep = self.stitched.as_ref().is_none_or(|stitched| {
            should_keep_frame(
                stitched,
                self.previous_frame.as_ref(),
                &frame,
                self.mode,
                false,
                self.hints,
            )
        });

        if should_keep {
            self.pending_auto_frame = None;
            return self.try_accept_frame(frame, false);
        }

        self.pending_auto_frame = Some(frame);
        ScrollFrameCaptureResult::Pending
    }

    fn try_accept_frame(
        &mut self,
        frame: RgbaImage,
        force_accept: bool,
    ) -> ScrollFrameCaptureResult {
        let Some(stitched) = self.stitched.as_ref() else {
            self.stitched = Some(frame.clone());
            self.previous_frame = Some(frame);
            self.frame_count = 1;
            return ScrollFrameCaptureResult::Accepted;
        };

        if self
            .previous_frame
            .as_ref()
            .is_some_and(|previous| are_frames_duplicate(previous, &frame))
        {
            return ScrollFrameCaptureResult::Duplicate;
        }

        let Some((next_stitched, plan)) =
            append_scrolling_frame(stitched, &frame, self.mode, force_accept, self.hints)
        else {
            return ScrollFrameCaptureResult::Rejected;
        };

        if !plan.used_best_guess {
            self.hints.best_match_count = self.hints.best_match_count.max(plan.match_count);
            self.hints.best_match_index = plan.match_index;
            self.hints.best_ignore_bottom_offset = plan.ignore_bottom_offset;
        }

        self.stitched = Some(next_stitched);
        self.previous_frame = Some(frame);
        self.frame_count += 1;
        ScrollFrameCaptureResult::Accepted
    }
}

pub fn estimate_new_content_height(previous: &RgbaImage, current: &RgbaImage) -> u32 {
    try_estimate_new_content_height(previous, current)
        .map_or(current.height(), |plan| plan.new_content_height)
}

pub fn try_estimate_new_content_height(
    previous: &RgbaImage,
    current: &RgbaImage,
) -> Option<ScrollAppendPlan> {
    find_scrolling_append(previous, current, ScrollAppendHints::default())
}

pub fn are_frames_duplicate(previous: &RgbaImage, current: &RgbaImage) -> bool {
    if previous.dimensions() != current.dimensions() {
        return false;
    }

    compare_regions(previous, current, 0, 0, previous.height()) > DUPLICATE_THRESHOLD
}

pub fn should_keep_frame(
    stitched: &RgbaImage,
    previous_frame: Option<&RgbaImage>,
    current: &RgbaImage,
    mode: ScrollingCaptureMode,
    force_accept: bool,
    hints: ScrollAppendHints,
) -> bool {
    if previous_frame.is_some_and(|previous| are_frames_duplicate(previous, current)) {
        return false;
    }

    find_scrolling_append(stitched, current, hints).is_some_and(|plan| {
        plan.new_content_height >= minimum_new_content_height(current.height(), mode, force_accept)
    })
}

pub fn append_scrolling_frame(
    stitched: &RgbaImage,
    current: &RgbaImage,
    mode: ScrollingCaptureMode,
    force_accept: bool,
    hints: ScrollAppendHints,
) -> Option<(RgbaImage, ScrollAppendPlan)> {
    let plan = find_scrolling_append(stitched, current, hints)?;
    if plan.new_content_height < minimum_new_content_height(current.height(), mode, force_accept) {
        return None;
    }

    let keep_stitched_height = stitched.height().checked_sub(plan.ignore_bottom_offset)?;
    let total_height = keep_stitched_height
        .saturating_add(plan.new_content_height)
        .min(32_000);
    if total_height <= keep_stitched_height {
        return None;
    }

    let mut output = RgbaImage::new(stitched.width(), total_height);
    for y in 0..keep_stitched_height {
        for x in 0..stitched.width() {
            output.put_pixel(x, y, *stitched.get_pixel(x, y));
        }
    }

    let draw_height = total_height - keep_stitched_height;
    let source_y = plan.match_index.saturating_add(1);
    for y in 0..draw_height {
        for x in 0..current.width() {
            output.put_pixel(
                x,
                keep_stitched_height + y,
                *current.get_pixel(x, source_y + y),
            );
        }
    }

    Some((output, plan))
}

fn find_scrolling_append(
    stitched: &RgbaImage,
    current: &RgbaImage,
    hints: ScrollAppendHints,
) -> Option<ScrollAppendPlan> {
    if stitched.width() != current.width() || stitched.height() == 0 || current.height() == 0 {
        return None;
    }

    let mut ignore_side_offset = (current.width() / 20).max(50);
    ignore_side_offset = ignore_side_offset.min(current.width() / 3);
    let mut compare_width = current.width().saturating_sub(ignore_side_offset * 2);
    if compare_width == 0 {
        ignore_side_offset = 0;
        compare_width = current.width();
    }

    let ignore_bottom_offset_max = current.height() / 3;
    let mut ignore_bottom_offset = 0;
    if ignore_bottom_offset_max > 0 {
        let stitched_last_y = stitched.height() - 1;
        let current_last_y = current.height() - 1;
        for offset in 0..=ignore_bottom_offset_max {
            if !rows_equal(
                stitched,
                current,
                stitched_last_y - offset,
                current_last_y - offset,
                ignore_side_offset,
                compare_width,
            ) {
                ignore_bottom_offset = offset;
                break;
            }
        }

        ignore_bottom_offset = ignore_bottom_offset
            .max(hints.best_ignore_bottom_offset)
            .min(ignore_bottom_offset_max);
    }

    let result_bottom_y = stitched.height().checked_sub(ignore_bottom_offset + 1)?;
    let match_limit = (current.height() / 2).max(1);
    let mut match_count = 0;
    let mut match_index = 0;

    for current_y in (0..current.height()).rev() {
        if match_count >= match_limit {
            break;
        }

        let mut current_match_count = 0;
        for row in 0..match_limit {
            let Some(stitched_y) = result_bottom_y.checked_sub(row) else {
                break;
            };
            let Some(candidate_y) = current_y.checked_sub(row) else {
                break;
            };

            if !rows_equal(
                stitched,
                current,
                stitched_y,
                candidate_y,
                ignore_side_offset,
                compare_width,
            ) {
                break;
            }

            current_match_count += 1;
        }

        if current_match_count > match_count {
            match_count = current_match_count;
            match_index = current_y;
        }
    }

    let mut used_best_guess = false;
    if match_count == 0 && hints.best_match_count > 0 {
        match_count = hints.best_match_count;
        match_index = hints.best_match_index;
        ignore_bottom_offset = hints.best_ignore_bottom_offset;
        used_best_guess = true;
    }

    if match_count == 0 {
        return None;
    }

    let new_content_height = current.height().checked_sub(match_index + 1)?;
    if new_content_height == 0 {
        return None;
    }

    Some(ScrollAppendPlan {
        new_content_height,
        match_count,
        match_index,
        ignore_bottom_offset,
        used_best_guess,
    })
}

fn rows_equal(a: &RgbaImage, b: &RgbaImage, a_y: u32, b_y: u32, x_offset: u32, width: u32) -> bool {
    if a_y >= a.height() || b_y >= b.height() || width == 0 {
        return false;
    }

    (x_offset..x_offset + width).all(|x| a.get_pixel(x, a_y) == b.get_pixel(x, b_y))
}

fn compare_regions(
    previous: &RgbaImage,
    current: &RgbaImage,
    previous_y: u32,
    current_y: u32,
    height: u32,
) -> f64 {
    if height == 0 {
        return 0.0;
    }

    let row_step = (height / 24).max(1);
    let column_step = (previous.width() / 64).max(4);
    let mut matches = 0_u32;
    let mut total = 0_u32;

    for row in (0..height).step_by(row_step as usize) {
        let py = previous_y + row;
        let cy = current_y + row;
        if py >= previous.height() || cy >= current.height() {
            continue;
        }

        for x in (0..previous.width()).step_by(column_step as usize) {
            total += 1;
            if colors_close(previous.get_pixel(x, py), current.get_pixel(x, cy)) {
                matches += 1;
            }
        }
    }

    if total == 0 {
        0.0
    } else {
        f64::from(matches) / f64::from(total)
    }
}

fn colors_close(a: &Rgba<u8>, b: &Rgba<u8>) -> bool {
    let dr = i32::from(a[0]) - i32::from(b[0]);
    let dg = i32::from(a[1]) - i32::from(b[1]);
    let db = i32::from(a[2]) - i32::from(b[2]);
    dr * dr + dg * dg + db * db < 100
}

fn minimum_new_content_height(
    frame_height: u32,
    mode: ScrollingCaptureMode,
    force_accept: bool,
) -> u32 {
    if force_accept || mode == ScrollingCaptureMode::Manual {
        1
    } else {
        MINIMUM_AUTO_NEW_CONTENT_PIXELS.max(frame_height / 20)
    }
}

#[cfg(test)]
mod tests {
    use super::{
        append_scrolling_frame, are_frames_duplicate, estimate_new_content_height,
        should_keep_frame, try_estimate_new_content_height, ScrollAppendHints,
        ScrollCaptureSession, ScrollFrameCaptureResult, ScrollingCaptureMode,
    };
    use image::{Rgba, RgbaImage};

    #[test]
    fn estimate_new_content_height_detects_vertical_scroll_delta() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_scrollable_frame(96, 120, 40);

        let new_content = estimate_new_content_height(&previous, &current);

        assert!((36..=44).contains(&new_content));
    }

    #[test]
    fn try_estimate_new_content_height_rejects_unrelated_frames() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_different_frame(96, 120);

        assert!(try_estimate_new_content_height(&previous, &current).is_none());
    }

    #[test]
    fn estimate_new_content_height_ignores_fixed_top_header() {
        let previous = create_scrollable_frame_with_header(96, 120, 0, 16);
        let current = create_scrollable_frame_with_header(96, 120, 40, 16);

        let new_content = estimate_new_content_height(&previous, &current);

        assert!((36..=44).contains(&new_content));
    }

    #[test]
    fn estimate_new_content_height_detects_large_fast_scroll_delta() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_scrollable_frame(96, 120, 100);

        let new_content = estimate_new_content_height(&previous, &current);

        assert!((96..=104).contains(&new_content));
    }

    #[test]
    fn are_frames_duplicate_returns_true_for_identical_frames() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_scrollable_frame(96, 120, 0);

        assert!(are_frames_duplicate(&previous, &current));
    }

    #[test]
    fn are_frames_duplicate_returns_false_for_scrolled_frames() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_scrollable_frame(96, 120, 40);

        assert!(!are_frames_duplicate(&previous, &current));
    }

    #[test]
    fn append_scrolling_frame_stitches_only_new_content() {
        let previous = create_scrollable_frame(96, 120, 0);
        let current = create_scrollable_frame(96, 120, 40);

        let (stitched, plan) = append_scrolling_frame(
            &previous,
            &current,
            ScrollingCaptureMode::Automatic,
            false,
            ScrollAppendHints::default(),
        )
        .expect("append");

        assert!((36..=44).contains(&plan.new_content_height));
        assert_eq!(stitched.width(), 96);
        assert_eq!(stitched.height(), 120 + plan.new_content_height);
    }

    #[test]
    fn should_keep_frame_applies_automatic_minimum_delta() {
        let previous = create_scrollable_frame(96, 120, 0);
        let tiny_delta = create_scrollable_frame(96, 120, 8);

        assert!(!should_keep_frame(
            &previous,
            Some(&previous),
            &tiny_delta,
            ScrollingCaptureMode::Automatic,
            false,
            ScrollAppendHints::default()
        ));
        assert!(should_keep_frame(
            &previous,
            Some(&previous),
            &tiny_delta,
            ScrollingCaptureMode::Manual,
            false,
            ScrollAppendHints::default()
        ));
    }

    #[test]
    fn automatic_session_rejects_tiny_pending_frame_on_finish() {
        let first = create_scrollable_frame(96, 120, 0);
        let small_delta = create_scrollable_frame(96, 120, 8);
        let mut session = ScrollCaptureSession::new(ScrollingCaptureMode::Automatic);

        assert_eq!(
            session.capture_frame(first, true),
            ScrollFrameCaptureResult::Accepted
        );
        assert_eq!(
            session.capture_frame(small_delta, false),
            ScrollFrameCaptureResult::Pending
        );

        let output = session.finish().expect("stitched output");

        assert_eq!(output.width(), 96);
        assert_eq!(output.height(), 120);
    }

    #[test]
    fn manual_session_accepts_small_scroll_delta() {
        let first = create_scrollable_frame(96, 120, 0);
        let small_delta = create_scrollable_frame(96, 120, 8);
        let mut session = ScrollCaptureSession::new(ScrollingCaptureMode::Manual);

        assert_eq!(
            session.capture_frame(first, true),
            ScrollFrameCaptureResult::Accepted
        );
        assert_eq!(
            session.capture_frame(small_delta, false),
            ScrollFrameCaptureResult::Accepted
        );
        assert_eq!(session.frame_count(), 2);

        let output = session.finish().expect("stitched output");

        assert_eq!(output.height(), 128);
    }

    fn create_scrollable_frame(width: u32, height: u32, y_offset: u32) -> RgbaImage {
        RgbaImage::from_fn(width, height, |x, y| {
            let absolute_y = y + y_offset;
            Rgba([
                (absolute_y % 256) as u8,
                ((absolute_y * 2 + x) % 256) as u8,
                ((absolute_y * 3 + x * 2) % 256) as u8,
                255,
            ])
        })
    }

    fn create_different_frame(width: u32, height: u32) -> RgbaImage {
        RgbaImage::from_fn(width, height, |x, y| {
            Rgba([
                ((x * 11 + y * 7) % 256) as u8,
                ((x * 5 + y * 13) % 256) as u8,
                ((x * 17 + y * 3) % 256) as u8,
                255,
            ])
        })
    }

    fn create_scrollable_frame_with_header(
        width: u32,
        height: u32,
        y_offset: u32,
        header_height: u32,
    ) -> RgbaImage {
        RgbaImage::from_fn(width, height, |x, y| {
            if y < header_height {
                return Rgba([30, 40, 50, 255]);
            }

            let absolute_y = y - header_height + y_offset;
            Rgba([
                (absolute_y % 256) as u8,
                ((absolute_y * 2 + x) % 256) as u8,
                ((absolute_y * 3 + x * 2) % 256) as u8,
                255,
            ])
        })
    }
}
