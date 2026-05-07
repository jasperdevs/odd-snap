use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AnnotationPoint {
    pub x: i32,
    pub y: i32,
}

impl AnnotationPoint {
    pub fn offset(self, dx: i32, dy: i32) -> Self {
        Self {
            x: self.x + dx,
            y: self.y + dy,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AnnotationRect {
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

impl AnnotationRect {
    pub const fn empty() -> Self {
        Self {
            x: 0,
            y: 0,
            width: 0,
            height: 0,
        }
    }

    pub fn contains(self, point: AnnotationPoint) -> bool {
        point.x >= self.x
            && point.y >= self.y
            && point.x < self.x.saturating_add(self.width)
            && point.y < self.y.saturating_add(self.height)
    }

    pub fn offset(self, dx: i32, dy: i32) -> Self {
        Self {
            x: self.x + dx,
            y: self.y + dy,
            ..self
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AnnotationColor {
    pub red: u8,
    pub green: u8,
    pub blue: u8,
    pub alpha: u8,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Annotation {
    DrawStroke {
        points: Vec<AnnotationPoint>,
        color: AnnotationColor,
    },
    BlurRect {
        rect: AnnotationRect,
    },
    Arrow {
        from: AnnotationPoint,
        to: AnnotationPoint,
        color: AnnotationColor,
    },
    CurvedArrow {
        points: Vec<AnnotationPoint>,
        color: AnnotationColor,
    },
    Highlight {
        rect: AnnotationRect,
        color: AnnotationColor,
    },
    StepNumber {
        position: AnnotationPoint,
        number: u32,
        color: AnnotationColor,
    },
    EraserFill {
        rect: AnnotationRect,
        color: AnnotationColor,
    },
    Text {
        position: AnnotationPoint,
        text: String,
        font_size: f32,
        color: AnnotationColor,
        bold: bool,
        italic: bool,
        stroke: bool,
        shadow: bool,
        background: bool,
        font_family: String,
    },
    Magnifier {
        position: AnnotationPoint,
        source_rect: AnnotationRect,
    },
    Emoji {
        position: AnnotationPoint,
        emoji: String,
        size: f32,
    },
    Line {
        from: AnnotationPoint,
        to: AnnotationPoint,
        color: AnnotationColor,
    },
    Ruler {
        from: AnnotationPoint,
        to: AnnotationPoint,
    },
    RectShape {
        rect: AnnotationRect,
        color: AnnotationColor,
    },
    CircleShape {
        rect: AnnotationRect,
        color: AnnotationColor,
    },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AnnotationToolGroup {
    Capture,
    Annotation,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AnnotationToolDef {
    pub id: &'static str,
    pub label: &'static str,
    pub group: AnnotationToolGroup,
}

pub const TOOL_DEFINITIONS: &[AnnotationToolDef] = &[
    AnnotationToolDef {
        id: "rect",
        label: "Rectangle Select",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "center",
        label: "Center Select",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "free",
        label: "Freeform Select",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "ocr",
        label: "OCR",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "sticker",
        label: "Sticker",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "upscale",
        label: "Upscale",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "picker",
        label: "Color Picker",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "scan",
        label: "QR/Barcode",
        group: AnnotationToolGroup::Capture,
    },
    AnnotationToolDef {
        id: "select",
        label: "Select",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "arrow",
        label: "Arrow",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "curvedArrow",
        label: "Curved Arrow",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "text",
        label: "Text",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "highlight",
        label: "Highlight",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "blur",
        label: "Blur",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "step",
        label: "Step Number",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "draw",
        label: "Draw",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "line",
        label: "Line",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "ruler",
        label: "Ruler",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "magnifier",
        label: "Magnifier",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "rectShape",
        label: "Rectangle",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "circleShape",
        label: "Circle",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "emoji",
        label: "Emoji",
        group: AnnotationToolGroup::Annotation,
    },
    AnnotationToolDef {
        id: "eraser",
        label: "Eraser",
        group: AnnotationToolGroup::Annotation,
    },
];

const ANNOTATION_DEFAULT_KEY_VKS: &[u32] = &[
    0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC,
];

impl Annotation {
    pub fn bounds(&self) -> AnnotationRect {
        match self {
            Self::Arrow { from, to, .. } => rect_from_points(*from, *to, 8),
            Self::CurvedArrow { points, .. } => bounds_of_points(points, 8),
            Self::Line { from, to, .. } => rect_from_points(*from, *to, 6),
            Self::Ruler { from, to } => rect_from_points(*from, *to, 8),
            Self::DrawStroke { points, .. } => bounds_of_points(points, 4),
            Self::BlurRect { rect }
            | Self::Highlight { rect, .. }
            | Self::RectShape { rect, .. }
            | Self::CircleShape { rect, .. }
            | Self::EraserFill { rect, .. } => *rect,
            Self::StepNumber { position, .. } => AnnotationRect {
                x: position.x - 14,
                y: position.y - 14,
                width: 28,
                height: 28,
            },
            Self::Emoji { position, size, .. } => AnnotationRect {
                x: position.x,
                y: position.y,
                width: size.round() as i32,
                height: size.round() as i32,
            },
            Self::Magnifier { position, .. } => AnnotationRect {
                x: position.x - 52,
                y: position.y - 52,
                width: 104,
                height: 104,
            },
            Self::Text {
                position,
                text,
                font_size,
                background,
                ..
            } => text_annotation_bounds(*position, text, *font_size, *background),
        }
    }

    pub fn moved(&self, dx: i32, dy: i32) -> Self {
        match self {
            Self::Arrow { from, to, color } => Self::Arrow {
                from: from.offset(dx, dy),
                to: to.offset(dx, dy),
                color: *color,
            },
            Self::CurvedArrow { points, color } => Self::CurvedArrow {
                points: points.iter().map(|point| point.offset(dx, dy)).collect(),
                color: *color,
            },
            Self::Line { from, to, color } => Self::Line {
                from: from.offset(dx, dy),
                to: to.offset(dx, dy),
                color: *color,
            },
            Self::Ruler { from, to } => Self::Ruler {
                from: from.offset(dx, dy),
                to: to.offset(dx, dy),
            },
            Self::DrawStroke { points, color } => Self::DrawStroke {
                points: points.iter().map(|point| point.offset(dx, dy)).collect(),
                color: *color,
            },
            Self::BlurRect { rect } => Self::BlurRect {
                rect: rect.offset(dx, dy),
            },
            Self::Highlight { rect, color } => Self::Highlight {
                rect: rect.offset(dx, dy),
                color: *color,
            },
            Self::RectShape { rect, color } => Self::RectShape {
                rect: rect.offset(dx, dy),
                color: *color,
            },
            Self::CircleShape { rect, color } => Self::CircleShape {
                rect: rect.offset(dx, dy),
                color: *color,
            },
            Self::EraserFill { rect, color } => Self::EraserFill {
                rect: rect.offset(dx, dy),
                color: *color,
            },
            Self::StepNumber {
                position,
                number,
                color,
            } => Self::StepNumber {
                position: position.offset(dx, dy),
                number: *number,
                color: *color,
            },
            Self::Emoji {
                position,
                emoji,
                size,
            } => Self::Emoji {
                position: position.offset(dx, dy),
                emoji: emoji.clone(),
                size: *size,
            },
            Self::Magnifier {
                position,
                source_rect,
            } => Self::Magnifier {
                position: position.offset(dx, dy),
                source_rect: *source_rect,
            },
            Self::Text {
                position,
                text,
                font_size,
                color,
                bold,
                italic,
                stroke,
                shadow,
                background,
                font_family,
            } => Self::Text {
                position: position.offset(dx, dy),
                text: text.clone(),
                font_size: *font_size,
                color: *color,
                bold: *bold,
                italic: *italic,
                stroke: *stroke,
                shadow: *shadow,
                background: *background,
                font_family: font_family.clone(),
            },
        }
    }

    pub fn scaled_to_bounds(&self, old_bounds: AnnotationRect, new_bounds: AnnotationRect) -> Self {
        if old_bounds.width <= 0 || old_bounds.height <= 0 {
            return self.clone();
        }

        let sx = new_bounds.width as f64 / old_bounds.width as f64;
        let sy = new_bounds.height as f64 / old_bounds.height as f64;
        let ox = new_bounds.x - (old_bounds.x as f64 * sx) as i32;
        let oy = new_bounds.y - (old_bounds.y as f64 * sy) as i32;
        let scale_point = |point: AnnotationPoint| AnnotationPoint {
            x: (point.x as f64 * sx) as i32 + ox,
            y: (point.y as f64 * sy) as i32 + oy,
        };
        let scale_rect = |rect: AnnotationRect| AnnotationRect {
            x: (rect.x as f64 * sx) as i32 + ox,
            y: (rect.y as f64 * sy) as i32 + oy,
            width: ((rect.width as f64 * sx) as i32).max(1),
            height: ((rect.height as f64 * sy) as i32).max(1),
        };
        let scale_factor = sx.max(sy) as f32;

        match self {
            Self::Arrow { from, to, color } => Self::Arrow {
                from: scale_point(*from),
                to: scale_point(*to),
                color: *color,
            },
            Self::Line { from, to, color } => Self::Line {
                from: scale_point(*from),
                to: scale_point(*to),
                color: *color,
            },
            Self::Ruler { from, to } => Self::Ruler {
                from: scale_point(*from),
                to: scale_point(*to),
            },
            Self::BlurRect { rect } => Self::BlurRect {
                rect: scale_rect(*rect),
            },
            Self::Highlight { rect, color } => Self::Highlight {
                rect: scale_rect(*rect),
                color: *color,
            },
            Self::RectShape { rect, color } => Self::RectShape {
                rect: scale_rect(*rect),
                color: *color,
            },
            Self::CircleShape { rect, color } => Self::CircleShape {
                rect: scale_rect(*rect),
                color: *color,
            },
            Self::EraserFill { rect, color } => Self::EraserFill {
                rect: scale_rect(*rect),
                color: *color,
            },
            Self::Emoji {
                position,
                emoji,
                size,
            } => Self::Emoji {
                position: scale_point(*position),
                emoji: emoji.clone(),
                size: (*size * scale_factor).max(8.0),
            },
            Self::Text {
                position,
                text,
                font_size,
                color,
                bold,
                italic,
                stroke,
                shadow,
                background,
                font_family,
            } => Self::Text {
                position: scale_point(*position),
                text: text.clone(),
                font_size: (*font_size * scale_factor).clamp(10.0, 120.0),
                color: *color,
                bold: *bold,
                italic: *italic,
                stroke: *stroke,
                shadow: *shadow,
                background: *background,
                font_family: font_family.clone(),
            },
            Self::StepNumber {
                position,
                number,
                color,
            } => Self::StepNumber {
                position: scale_point(*position),
                number: *number,
                color: *color,
            },
            Self::DrawStroke { points, color } => Self::DrawStroke {
                points: points.iter().copied().map(scale_point).collect(),
                color: *color,
            },
            Self::CurvedArrow { points, color } => Self::CurvedArrow {
                points: points.iter().copied().map(scale_point).collect(),
                color: *color,
            },
            Self::Magnifier {
                position,
                source_rect,
            } => Self::Magnifier {
                position: scale_point(*position),
                source_rect: *source_rect,
            },
        }
    }
}

pub fn annotation_tool_default_key(tool_id: &str) -> Option<u32> {
    TOOL_DEFINITIONS
        .iter()
        .filter(|tool| tool.group == AnnotationToolGroup::Annotation)
        .position(|tool| tool.id.eq_ignore_ascii_case(tool_id))
        .and_then(|index| ANNOTATION_DEFAULT_KEY_VKS.get(index).copied())
}

pub fn default_enabled_tool_ids() -> Vec<String> {
    TOOL_DEFINITIONS
        .iter()
        .map(|tool| tool.id.to_string())
        .collect()
}

pub fn annotation_tool_hotkey(
    tool_id: &str,
    enabled_tools: Option<&[String]>,
    tool_hotkeys: &BTreeMap<String, Vec<u32>>,
) -> Option<(u32, u32)> {
    if let Some(values) = tool_hotkeys.get(tool_id).filter(|values| values.len() >= 2) {
        return Some((values[0], values[1]));
    }
    if let Some(enabled_tools) = enabled_tools {
        if !enabled_tools
            .iter()
            .any(|enabled_tool| enabled_tool.eq_ignore_ascii_case(tool_id))
        {
            return Some((0, 0));
        }
    }
    annotation_tool_default_key(tool_id).map(|key| (0, key))
}

pub fn find_annotation_tool_id(
    modifiers: u32,
    key: u32,
    visible_tool_ids: Option<&[String]>,
    enabled_tools: Option<&[String]>,
    tool_hotkeys: &BTreeMap<String, Vec<u32>>,
) -> Option<&'static str> {
    if key == 0 {
        return None;
    }

    TOOL_DEFINITIONS
        .iter()
        .filter(|tool| tool.group == AnnotationToolGroup::Annotation)
        .filter(|tool| {
            visible_tool_ids.is_none_or(|visible_tools| {
                visible_tools
                    .iter()
                    .any(|visible_tool| visible_tool.eq_ignore_ascii_case(tool.id))
            })
        })
        .find(|tool| {
            annotation_tool_hotkey(tool.id, enabled_tools, tool_hotkeys) == Some((modifiers, key))
        })
        .map(|tool| tool.id)
}

pub fn hit_test_annotations(annotations: &[Annotation], point: AnnotationPoint) -> Option<usize> {
    annotations
        .iter()
        .enumerate()
        .rev()
        .find(|(_, annotation)| annotation.bounds().contains(point))
        .map(|(index, _)| index)
}

fn rect_from_points(a: AnnotationPoint, b: AnnotationPoint, pad: i32) -> AnnotationRect {
    AnnotationRect {
        x: a.x.min(b.x) - pad,
        y: a.y.min(b.y) - pad,
        width: (b.x - a.x).abs() + pad * 2,
        height: (b.y - a.y).abs() + pad * 2,
    }
}

fn bounds_of_points(points: &[AnnotationPoint], pad: i32) -> AnnotationRect {
    let Some(first) = points.first() else {
        return AnnotationRect::empty();
    };
    let (mut min_x, mut min_y, mut max_x, mut max_y) = (first.x, first.y, first.x, first.y);
    for point in points.iter().skip(1) {
        min_x = min_x.min(point.x);
        min_y = min_y.min(point.y);
        max_x = max_x.max(point.x);
        max_y = max_y.max(point.y);
    }
    AnnotationRect {
        x: min_x - pad,
        y: min_y - pad,
        width: max_x - min_x + pad * 2,
        height: max_y - min_y + pad * 2,
    }
}

fn text_annotation_bounds(
    position: AnnotationPoint,
    text: &str,
    font_size: f32,
    background: bool,
) -> AnnotationRect {
    let pad_x = if background { 16 } else { 10 };
    let pad_y = if background { 12 } else { 6 };
    let width = ((text.chars().count() as f32 * font_size * 0.6).ceil() as i32).max(1);
    let height = (font_size * 1.3).ceil() as i32;
    AnnotationRect {
        x: position.x - (pad_x / 2),
        y: position.y - (pad_y / 2),
        width: width + pad_x,
        height: height + pad_y,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn color() -> AnnotationColor {
        AnnotationColor {
            red: 10,
            green: 20,
            blue: 30,
            alpha: 255,
        }
    }

    #[test]
    fn annotation_tool_defaults_match_legacy_order() {
        assert_eq!(annotation_tool_default_key("select"), Some(0x31));
        assert_eq!(annotation_tool_default_key("line"), Some(0x39));
        assert_eq!(annotation_tool_default_key("ruler"), Some(0x30));
        assert_eq!(annotation_tool_default_key("eraser"), Some(0xDC));
        assert_eq!(annotation_tool_default_key("rect"), None);
        assert!(default_enabled_tool_ids().contains(&"sticker".to_string()));
    }

    #[test]
    fn annotation_hotkeys_respect_custom_and_visible_tools() {
        let mut hotkeys = BTreeMap::new();
        hotkeys.insert("arrow".to_string(), vec![2, 65]);
        assert_eq!(
            annotation_tool_hotkey("arrow", Some(&["select".into()]), &hotkeys),
            Some((2, 65))
        );
        assert_eq!(
            annotation_tool_hotkey("text", Some(&["select".into()]), &BTreeMap::new()),
            Some((0, 0))
        );
        assert_eq!(
            find_annotation_tool_id(2, 65, None, None, &hotkeys),
            Some("arrow")
        );
        assert_eq!(
            find_annotation_tool_id(0, 0x31, Some(&["arrow".into()]), None, &BTreeMap::new()),
            None
        );
    }

    #[test]
    fn annotation_bounds_move_scale_and_hit_testing_are_ported() {
        let stroke = Annotation::DrawStroke {
            points: vec![
                AnnotationPoint { x: 10, y: 20 },
                AnnotationPoint { x: 30, y: 50 },
            ],
            color: color(),
        };
        assert_eq!(
            stroke.bounds(),
            AnnotationRect {
                x: 6,
                y: 16,
                width: 28,
                height: 38,
            }
        );

        let moved = stroke.moved(5, -10);
        assert_eq!(
            moved.bounds(),
            AnnotationRect {
                x: 11,
                y: 6,
                width: 28,
                height: 38,
            }
        );

        let scaled = stroke.scaled_to_bounds(
            AnnotationRect {
                x: 0,
                y: 0,
                width: 100,
                height: 100,
            },
            AnnotationRect {
                x: 0,
                y: 0,
                width: 200,
                height: 50,
            },
        );
        if let Annotation::DrawStroke { points, .. } = scaled {
            assert_eq!(points[1], AnnotationPoint { x: 60, y: 25 });
        } else {
            panic!("scaled draw stroke should remain a draw stroke");
        }

        let annotations = vec![
            Annotation::RectShape {
                rect: AnnotationRect {
                    x: 0,
                    y: 0,
                    width: 50,
                    height: 50,
                },
                color: color(),
            },
            Annotation::CircleShape {
                rect: AnnotationRect {
                    x: 10,
                    y: 10,
                    width: 50,
                    height: 50,
                },
                color: color(),
            },
        ];
        assert_eq!(
            hit_test_annotations(&annotations, AnnotationPoint { x: 20, y: 20 }),
            Some(1)
        );
        assert_eq!(
            hit_test_annotations(&annotations, AnnotationPoint { x: 90, y: 90 }),
            None
        );
    }
}
