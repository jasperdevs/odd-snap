#![allow(dead_code)]

use image::{Rgba, RgbaImage};
use oddsnap_core::{AnnotationColor, AnnotationPoint, AnnotationRect};

use crate::annotation_workspace::{AnnotationRenderItem, AnnotationRenderPrimitive::*};

pub(crate) fn compose_annotations(
    mut image: RgbaImage,
    plan: &[AnnotationRenderItem],
) -> RgbaImage {
    for item in plan {
        draw_item(&mut image, item);
        if item.selected {
            stroke_rect(&mut image, item.bounds, Rgba([80, 160, 255, 220]), 1);
        }
    }
    image
}

fn draw_item(image: &mut RgbaImage, item: &AnnotationRenderItem) {
    match &item.primitive {
        DrawStroke { points, color } | CurvedArrow { points, color } => {
            draw_polyline(image, points, rgba(*color), 2);
        }
        BlurRect { rect } => fill_rect(image, *rect, Rgba([160, 160, 160, 96])),
        Arrow { from, to, color } => {
            draw_line(image, *from, *to, rgba(*color), 2);
            draw_arrow_head(image, *from, *to, rgba(*color));
        }
        Highlight { rect, color } | EraserFill { rect, color } => {
            fill_rect(image, *rect, rgba(*color));
        }
        StepNumber {
            position,
            number: _,
            color,
        } => {
            fill_rect(
                image,
                AnnotationRect {
                    x: position.x - 8,
                    y: position.y - 8,
                    width: 16,
                    height: 16,
                },
                rgba(*color),
            );
        }
        Text {
            position,
            text,
            font_size,
            color,
            ..
        } => {
            let width = ((text.len() as f32 * font_size.max(8.0) * 0.6).round() as i32).max(8);
            let height = font_size.round() as i32;
            stroke_rect(
                image,
                AnnotationRect {
                    x: position.x,
                    y: position.y,
                    width,
                    height,
                },
                rgba(*color),
                1,
            );
        }
        Magnifier {
            position,
            source_rect,
        } => {
            stroke_rect(
                image,
                AnnotationRect {
                    x: position.x,
                    y: position.y,
                    width: source_rect.width,
                    height: source_rect.height,
                },
                Rgba([255, 255, 255, 220]),
                2,
            );
        }
        Emoji {
            position,
            emoji: _,
            size,
        } => fill_rect(
            image,
            AnnotationRect {
                x: position.x,
                y: position.y,
                width: size.round() as i32,
                height: size.round() as i32,
            },
            Rgba([255, 255, 255, 180]),
        ),
        Line { from, to, color } => draw_line(image, *from, *to, rgba(*color), 2),
        Ruler { from, to } => draw_line(image, *from, *to, Rgba([255, 255, 255, 230]), 1),
        RectShape { rect, color } => stroke_rect(image, *rect, rgba(*color), 2),
        CircleShape { rect, color } => stroke_ellipse(image, *rect, rgba(*color), 2),
    }
}

fn rgba(color: AnnotationColor) -> Rgba<u8> {
    Rgba([color.red, color.green, color.blue, color.alpha])
}

fn draw_polyline(image: &mut RgbaImage, points: &[AnnotationPoint], color: Rgba<u8>, width: i32) {
    for pair in points.windows(2) {
        draw_line(image, pair[0], pair[1], color, width);
    }
}

fn draw_arrow_head(
    image: &mut RgbaImage,
    from: AnnotationPoint,
    to: AnnotationPoint,
    color: Rgba<u8>,
) {
    let dx = (to.x - from.x).signum();
    let dy = (to.y - from.y).signum();
    draw_line(
        image,
        to,
        AnnotationPoint {
            x: to.x - dx * 8 - dy * 4,
            y: to.y - dy * 8 + dx * 4,
        },
        color,
        2,
    );
    draw_line(
        image,
        to,
        AnnotationPoint {
            x: to.x - dx * 8 + dy * 4,
            y: to.y - dy * 8 - dx * 4,
        },
        color,
        2,
    );
}

fn draw_line(
    image: &mut RgbaImage,
    from: AnnotationPoint,
    to: AnnotationPoint,
    color: Rgba<u8>,
    width: i32,
) {
    let mut x = from.x;
    let mut y = from.y;
    let dx = (to.x - from.x).abs();
    let sx = if from.x < to.x { 1 } else { -1 };
    let dy = -(to.y - from.y).abs();
    let sy = if from.y < to.y { 1 } else { -1 };
    let mut err = dx + dy;

    loop {
        draw_brush(image, x, y, color, width);
        if x == to.x && y == to.y {
            break;
        }
        let e2 = err * 2;
        if e2 >= dy {
            err += dy;
            x += sx;
        }
        if e2 <= dx {
            err += dx;
            y += sy;
        }
    }
}

fn draw_brush(image: &mut RgbaImage, x: i32, y: i32, color: Rgba<u8>, width: i32) {
    let radius = width.max(1) / 2;
    for py in y - radius..=y + radius {
        for px in x - radius..=x + radius {
            blend_pixel(image, px, py, color);
        }
    }
}

fn fill_rect(image: &mut RgbaImage, rect: AnnotationRect, color: Rgba<u8>) {
    let (x0, y0, x1, y1) = clipped_rect(image, rect);
    for y in y0..y1 {
        for x in x0..x1 {
            blend_pixel(image, x, y, color);
        }
    }
}

fn stroke_rect(image: &mut RgbaImage, rect: AnnotationRect, color: Rgba<u8>, width: i32) {
    draw_line(
        image,
        AnnotationPoint {
            x: rect.x,
            y: rect.y,
        },
        AnnotationPoint {
            x: rect.x + rect.width,
            y: rect.y,
        },
        color,
        width,
    );
    draw_line(
        image,
        AnnotationPoint {
            x: rect.x,
            y: rect.y,
        },
        AnnotationPoint {
            x: rect.x,
            y: rect.y + rect.height,
        },
        color,
        width,
    );
    draw_line(
        image,
        AnnotationPoint {
            x: rect.x + rect.width,
            y: rect.y,
        },
        AnnotationPoint {
            x: rect.x + rect.width,
            y: rect.y + rect.height,
        },
        color,
        width,
    );
    draw_line(
        image,
        AnnotationPoint {
            x: rect.x,
            y: rect.y + rect.height,
        },
        AnnotationPoint {
            x: rect.x + rect.width,
            y: rect.y + rect.height,
        },
        color,
        width,
    );
}

fn stroke_ellipse(image: &mut RgbaImage, rect: AnnotationRect, color: Rgba<u8>, width: i32) {
    let rx = (rect.width.abs() as f32 / 2.0).max(1.0);
    let ry = (rect.height.abs() as f32 / 2.0).max(1.0);
    let cx = rect.x as f32 + rx;
    let cy = rect.y as f32 + ry;
    let steps = ((rx + ry) * 2.0).round().max(16.0) as i32;
    let mut previous = None;
    for step in 0..=steps {
        let theta = (step as f32 / steps as f32) * std::f32::consts::TAU;
        let point = AnnotationPoint {
            x: (cx + theta.cos() * rx).round() as i32,
            y: (cy + theta.sin() * ry).round() as i32,
        };
        if let Some(previous) = previous {
            draw_line(image, previous, point, color, width);
        }
        previous = Some(point);
    }
}

fn clipped_rect(image: &RgbaImage, rect: AnnotationRect) -> (i32, i32, i32, i32) {
    let x0 = rect.x.max(0).min(image.width() as i32);
    let y0 = rect.y.max(0).min(image.height() as i32);
    let x1 = rect
        .x
        .saturating_add(rect.width.max(0))
        .max(0)
        .min(image.width() as i32);
    let y1 = rect
        .y
        .saturating_add(rect.height.max(0))
        .max(0)
        .min(image.height() as i32);
    (x0, y0, x1, y1)
}

fn blend_pixel(image: &mut RgbaImage, x: i32, y: i32, color: Rgba<u8>) {
    if x < 0 || y < 0 || x >= image.width() as i32 || y >= image.height() as i32 {
        return;
    }

    let pixel = image.get_pixel_mut(x as u32, y as u32);
    let alpha = color[3] as u16;
    for channel in 0..3 {
        pixel[channel] = (((color[channel] as u16 * alpha)
            + (pixel[channel] as u16 * (255 - alpha)))
            / 255) as u8;
    }
    pixel[3] = pixel[3].max(color[3]);
}

#[cfg(test)]
mod tests {
    use image::{Rgba, RgbaImage};
    use oddsnap_core::{Annotation, AnnotationColor, AnnotationPoint, AnnotationRect};

    use crate::annotation_workspace::AnnotationWorkspace;

    use super::compose_annotations;

    fn red() -> AnnotationColor {
        AnnotationColor {
            red: 255,
            green: 0,
            blue: 0,
            alpha: 255,
        }
    }

    #[test]
    fn compositor_draws_committed_rect_and_selected_bounds() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.add_annotation(Annotation::RectShape {
            rect: AnnotationRect {
                x: 2,
                y: 2,
                width: 8,
                height: 6,
            },
            color: red(),
        });

        let output = compose_annotations(
            RgbaImage::from_pixel(16, 16, Rgba([0, 0, 0, 255])),
            &workspace.render_plan(),
        );

        let selected_corner = output.get_pixel(2, 2);
        assert!(selected_corner[2] > selected_corner[0]);
        assert_eq!(output.get_pixel(12, 12), &Rgba([0, 0, 0, 255]));
    }

    #[test]
    fn compositor_alpha_blends_highlights() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.set_preview(Annotation::Highlight {
            rect: AnnotationRect {
                x: 0,
                y: 0,
                width: 2,
                height: 2,
            },
            color: AnnotationColor {
                red: 255,
                green: 255,
                blue: 0,
                alpha: 128,
            },
        });

        let output = compose_annotations(
            RgbaImage::from_pixel(4, 4, Rgba([10, 10, 10, 255])),
            &workspace.render_plan(),
        );

        assert!(output.get_pixel(0, 0)[0] > 100);
        assert_eq!(output.get_pixel(3, 3), &Rgba([10, 10, 10, 255]));
    }

    #[test]
    fn compositor_draws_preview_line_without_selected_outline() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.set_preview(Annotation::Line {
            from: AnnotationPoint { x: 0, y: 0 },
            to: AnnotationPoint { x: 4, y: 0 },
            color: red(),
        });

        let output = compose_annotations(
            RgbaImage::from_pixel(8, 8, Rgba([0, 0, 0, 255])),
            &workspace.render_plan(),
        );

        assert_eq!(output.get_pixel(0, 0), &Rgba([255, 0, 0, 255]));
        assert_eq!(output.get_pixel(0, 3), &Rgba([0, 0, 0, 255]));
    }
}
