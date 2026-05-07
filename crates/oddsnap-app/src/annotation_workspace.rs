#![allow(dead_code)]

use oddsnap_core::{
    hit_test_annotations, Annotation, AnnotationColor, AnnotationPoint, AnnotationRect,
    AnnotationToolGroup, AppSettings, TOOL_DEFINITIONS,
};

const UNDO_LIMIT: usize = 100;

#[derive(Debug, Clone, Default)]
pub(crate) struct AnnotationWorkspace {
    annotations: Vec<Annotation>,
    selected_index: Option<usize>,
    preview: Option<Annotation>,
    undo_stack: Vec<AnnotationSnapshot>,
    redo_stack: Vec<AnnotationSnapshot>,
}

#[derive(Debug, Clone)]
struct AnnotationSnapshot {
    annotations: Vec<Annotation>,
    selected_index: Option<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct AnnotationToolbarTool {
    pub(crate) id: &'static str,
    pub(crate) label: &'static str,
    pub(crate) hotkey: Option<(u32, u32)>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub(crate) struct AnnotationToolState {
    active_tool_id: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum AnnotationRenderLayer {
    Committed,
    Preview,
}

#[derive(Debug, Clone, PartialEq)]
pub(crate) struct AnnotationRenderItem {
    pub(crate) layer: AnnotationRenderLayer,
    pub(crate) selected: bool,
    pub(crate) bounds: AnnotationRect,
    pub(crate) primitive: AnnotationRenderPrimitive,
}

#[derive(Debug, Clone, PartialEq)]
pub(crate) enum AnnotationRenderPrimitive {
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

impl AnnotationToolState {
    pub(crate) fn new(settings: &AppSettings) -> Self {
        let mut state = Self {
            active_tool_id: Some("select".into()),
        };
        state.reconcile_with_settings(settings);
        state
    }

    pub(crate) fn active_tool_id(&self) -> Option<&str> {
        self.active_tool_id.as_deref()
    }

    pub(crate) fn select_tool(&mut self, tool_id: &str, settings: &AppSettings) -> bool {
        let Some(tool) = annotation_toolbar_tools(settings)
            .into_iter()
            .find(|tool| tool.id.eq_ignore_ascii_case(tool_id))
        else {
            return false;
        };

        self.active_tool_id = Some(tool.id.into());
        true
    }

    pub(crate) fn select_hotkey(
        &mut self,
        modifiers: u32,
        key: u32,
        settings: &AppSettings,
    ) -> Option<&'static str> {
        let visible_tool_ids = annotation_toolbar_tools(settings)
            .into_iter()
            .map(|tool| tool.id.to_string())
            .collect::<Vec<_>>();
        let tool_id = settings.find_annotation_tool_id(modifiers, key, Some(&visible_tool_ids))?;
        self.active_tool_id = Some(tool_id.into());
        Some(tool_id)
    }

    pub(crate) fn reconcile_with_settings(&mut self, settings: &AppSettings) {
        let tools = annotation_toolbar_tools(settings);
        if tools.is_empty() {
            self.active_tool_id = None;
            return;
        }

        let active_is_visible = self.active_tool_id.as_ref().is_some_and(|active| {
            tools
                .iter()
                .any(|tool| tool.id.eq_ignore_ascii_case(active))
        });
        if !active_is_visible {
            self.active_tool_id = Some(tools[0].id.into());
        }
    }
}

pub(crate) fn annotation_toolbar_tools(settings: &AppSettings) -> Vec<AnnotationToolbarTool> {
    TOOL_DEFINITIONS
        .iter()
        .filter(|tool| tool.group == AnnotationToolGroup::Annotation)
        .filter(|tool| {
            settings.enabled_tools.as_ref().is_none_or(|enabled_tools| {
                enabled_tools
                    .iter()
                    .any(|enabled_tool| enabled_tool.eq_ignore_ascii_case(tool.id))
            })
        })
        .map(|tool| {
            let hotkey = settings
                .annotation_tool_hotkey(tool.id)
                .filter(|(_, key)| *key != 0);
            AnnotationToolbarTool {
                id: tool.id,
                label: tool.label,
                hotkey,
            }
        })
        .collect()
}

impl AnnotationWorkspace {
    pub(crate) fn annotations(&self) -> &[Annotation] {
        &self.annotations
    }

    pub(crate) fn render_plan(&self) -> Vec<AnnotationRenderItem> {
        let committed = self
            .annotations
            .iter()
            .enumerate()
            .map(|(index, annotation)| {
                annotation_render_item(
                    annotation,
                    AnnotationRenderLayer::Committed,
                    self.selected_index == Some(index),
                )
            });
        let preview = self.preview.as_ref().map(|annotation| {
            annotation_render_item(annotation, AnnotationRenderLayer::Preview, false)
        });

        committed.chain(preview).collect()
    }

    pub(crate) fn selected_index(&self) -> Option<usize> {
        self.selected_index
    }

    pub(crate) fn preview(&self) -> Option<&Annotation> {
        self.preview.as_ref()
    }

    pub(crate) fn set_preview(&mut self, annotation: Annotation) {
        self.preview = Some(annotation);
    }

    pub(crate) fn clear_preview(&mut self) {
        self.preview = None;
    }

    pub(crate) fn commit_preview(&mut self) -> Option<usize> {
        let annotation = self.preview.take()?;
        Some(self.add_annotation(annotation))
    }

    pub(crate) fn add_annotation(&mut self, annotation: Annotation) -> usize {
        self.capture_undo_snapshot();
        self.annotations.push(annotation);
        self.selected_index = self.annotations.len().checked_sub(1);
        self.redo_stack.clear();
        self.selected_index
            .expect("new annotation should be selected")
    }

    pub(crate) fn select_at(&mut self, point: AnnotationPoint) -> Option<usize> {
        self.selected_index = hit_test_annotations(&self.annotations, point);
        self.selected_index
    }

    pub(crate) fn clear_selection(&mut self) {
        self.selected_index = None;
    }

    pub(crate) fn move_selected(&mut self, dx: i32, dy: i32) -> bool {
        let Some(index) = self.selected_index else {
            return false;
        };
        let Some(current) = self.annotations.get(index).cloned() else {
            self.selected_index = None;
            return false;
        };

        self.capture_undo_snapshot();
        self.annotations[index] = current.moved(dx, dy);
        self.redo_stack.clear();
        true
    }

    pub(crate) fn scale_selected_to_bounds(&mut self, new_bounds: AnnotationRect) -> bool {
        let Some(index) = self.selected_index else {
            return false;
        };
        let Some(current) = self.annotations.get(index).cloned() else {
            self.selected_index = None;
            return false;
        };

        self.capture_undo_snapshot();
        let old_bounds = current.bounds();
        self.annotations[index] = current.scaled_to_bounds(old_bounds, new_bounds);
        self.redo_stack.clear();
        true
    }

    pub(crate) fn remove_selected(&mut self) -> Option<Annotation> {
        let index = self.selected_index?;
        if index >= self.annotations.len() {
            self.selected_index = None;
            return None;
        }

        self.capture_undo_snapshot();
        let removed = self.annotations.remove(index);
        self.selected_index = if self.annotations.is_empty() {
            None
        } else {
            Some(index.min(self.annotations.len() - 1))
        };
        self.redo_stack.clear();
        Some(removed)
    }

    pub(crate) fn undo(&mut self) -> bool {
        let Some(snapshot) = self.undo_stack.pop() else {
            return false;
        };

        self.redo_stack.push(self.snapshot());
        self.restore(snapshot);
        true
    }

    pub(crate) fn redo(&mut self) -> bool {
        let Some(snapshot) = self.redo_stack.pop() else {
            return false;
        };

        self.undo_stack.push(self.snapshot());
        self.restore(snapshot);
        true
    }

    fn capture_undo_snapshot(&mut self) {
        self.undo_stack.push(self.snapshot());
        if self.undo_stack.len() > UNDO_LIMIT {
            self.undo_stack.remove(0);
        }
    }

    fn snapshot(&self) -> AnnotationSnapshot {
        AnnotationSnapshot {
            annotations: self.annotations.clone(),
            selected_index: self.selected_index,
        }
    }

    fn restore(&mut self, snapshot: AnnotationSnapshot) {
        self.annotations = snapshot.annotations;
        self.selected_index = snapshot
            .selected_index
            .filter(|index| *index < self.annotations.len());
        self.preview = None;
    }
}

fn annotation_render_item(
    annotation: &Annotation,
    layer: AnnotationRenderLayer,
    selected: bool,
) -> AnnotationRenderItem {
    AnnotationRenderItem {
        layer,
        selected,
        bounds: annotation.bounds(),
        primitive: annotation_render_primitive(annotation),
    }
}

fn annotation_render_primitive(annotation: &Annotation) -> AnnotationRenderPrimitive {
    match annotation {
        Annotation::DrawStroke { points, color } => AnnotationRenderPrimitive::DrawStroke {
            points: points.clone(),
            color: *color,
        },
        Annotation::BlurRect { rect } => AnnotationRenderPrimitive::BlurRect { rect: *rect },
        Annotation::Arrow { from, to, color } => AnnotationRenderPrimitive::Arrow {
            from: *from,
            to: *to,
            color: *color,
        },
        Annotation::CurvedArrow { points, color } => AnnotationRenderPrimitive::CurvedArrow {
            points: points.clone(),
            color: *color,
        },
        Annotation::Highlight { rect, color } => AnnotationRenderPrimitive::Highlight {
            rect: *rect,
            color: *color,
        },
        Annotation::StepNumber {
            position,
            number,
            color,
        } => AnnotationRenderPrimitive::StepNumber {
            position: *position,
            number: *number,
            color: *color,
        },
        Annotation::EraserFill { rect, color } => AnnotationRenderPrimitive::EraserFill {
            rect: *rect,
            color: *color,
        },
        Annotation::Text {
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
        } => AnnotationRenderPrimitive::Text {
            position: *position,
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
        Annotation::Magnifier {
            position,
            source_rect,
        } => AnnotationRenderPrimitive::Magnifier {
            position: *position,
            source_rect: *source_rect,
        },
        Annotation::Emoji {
            position,
            emoji,
            size,
        } => AnnotationRenderPrimitive::Emoji {
            position: *position,
            emoji: emoji.clone(),
            size: *size,
        },
        Annotation::Line { from, to, color } => AnnotationRenderPrimitive::Line {
            from: *from,
            to: *to,
            color: *color,
        },
        Annotation::Ruler { from, to } => AnnotationRenderPrimitive::Ruler {
            from: *from,
            to: *to,
        },
        Annotation::RectShape { rect, color } => AnnotationRenderPrimitive::RectShape {
            rect: *rect,
            color: *color,
        },
        Annotation::CircleShape { rect, color } => AnnotationRenderPrimitive::CircleShape {
            rect: *rect,
            color: *color,
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use oddsnap_core::AnnotationColor;

    fn color() -> AnnotationColor {
        AnnotationColor {
            red: 255,
            green: 0,
            blue: 0,
            alpha: 255,
        }
    }

    #[test]
    fn toolbar_tools_are_annotation_only_and_follow_settings_visibility() {
        let mut settings = AppSettings {
            enabled_tools: Some(vec!["ocr".into(), "arrow".into(), "text".into()]),
            ..AppSettings::default()
        };
        settings.tool_hotkeys.insert("arrow".into(), vec![2, 65]);
        settings.tool_hotkeys.insert("text".into(), vec![0, 0]);

        let tools = annotation_toolbar_tools(&settings);

        assert_eq!(
            tools
                .iter()
                .map(|tool| (tool.id, tool.label, tool.hotkey))
                .collect::<Vec<_>>(),
            vec![("arrow", "Arrow", Some((2, 65))), ("text", "Text", None)]
        );
    }

    #[test]
    fn tool_state_tracks_visible_tools_and_custom_hotkeys() {
        let mut settings = AppSettings {
            enabled_tools: Some(vec!["arrow".into(), "text".into()]),
            ..AppSettings::default()
        };
        settings.tool_hotkeys.insert("arrow".into(), vec![2, 65]);

        let mut state = AnnotationToolState::new(&settings);

        assert_eq!(state.active_tool_id(), Some("arrow"));
        assert_eq!(state.select_hotkey(2, 65, &settings), Some("arrow"));
        assert_eq!(state.active_tool_id(), Some("arrow"));
        assert!(state.select_tool("TEXT", &settings));
        assert_eq!(state.active_tool_id(), Some("text"));
        assert!(!state.select_tool("select", &settings));
        assert_eq!(state.active_tool_id(), Some("text"));
    }

    #[test]
    fn tool_state_clears_when_no_annotation_tools_are_visible() {
        let settings = AppSettings {
            enabled_tools: Some(vec!["ocr".into(), "picker".into()]),
            ..AppSettings::default()
        };

        let mut state = AnnotationToolState::new(&settings);

        assert_eq!(annotation_toolbar_tools(&settings), Vec::new());
        assert_eq!(state.active_tool_id(), None);
        assert_eq!(state.select_hotkey(0, 0x31, &settings), None);

        state.select_tool("select", &AppSettings::default());
        state.reconcile_with_settings(&settings);
        assert_eq!(state.active_tool_id(), None);
    }

    #[test]
    fn render_plan_preserves_committed_selection_and_preview_order() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.add_annotation(Annotation::Line {
            from: AnnotationPoint { x: 0, y: 0 },
            to: AnnotationPoint { x: 10, y: 10 },
            color: color(),
        });
        workspace.add_annotation(Annotation::RectShape {
            rect: AnnotationRect {
                x: 20,
                y: 20,
                width: 30,
                height: 40,
            },
            color: color(),
        });
        workspace.set_preview(Annotation::BlurRect {
            rect: AnnotationRect {
                x: 3,
                y: 4,
                width: 5,
                height: 6,
            },
        });

        let plan = workspace.render_plan();

        assert_eq!(plan.len(), 3);
        assert_eq!(plan[0].layer, AnnotationRenderLayer::Committed);
        assert!(!plan[0].selected);
        assert_eq!(plan[1].layer, AnnotationRenderLayer::Committed);
        assert!(plan[1].selected);
        assert_eq!(
            plan[1].bounds,
            AnnotationRect {
                x: 20,
                y: 20,
                width: 30,
                height: 40,
            }
        );
        assert_eq!(plan[2].layer, AnnotationRenderLayer::Preview);
        assert!(!plan[2].selected);
        assert!(matches!(
            plan[2].primitive,
            AnnotationRenderPrimitive::BlurRect { .. }
        ));
    }

    #[test]
    fn workspace_commits_preview_selects_and_moves_annotations() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.set_preview(Annotation::RectShape {
            rect: AnnotationRect {
                x: 10,
                y: 20,
                width: 30,
                height: 40,
            },
            color: color(),
        });

        assert!(workspace.preview().is_some());
        assert_eq!(workspace.commit_preview(), Some(0));
        assert!(workspace.preview().is_none());
        assert_eq!(workspace.selected_index(), Some(0));
        assert_eq!(
            workspace.select_at(AnnotationPoint { x: 15, y: 25 }),
            Some(0)
        );
        assert!(workspace.move_selected(5, -10));

        assert_eq!(
            workspace.annotations()[0].bounds(),
            AnnotationRect {
                x: 15,
                y: 10,
                width: 30,
                height: 40
            }
        );
    }

    #[test]
    fn workspace_undo_redo_and_remove_restore_state() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.add_annotation(Annotation::Line {
            from: AnnotationPoint { x: 0, y: 0 },
            to: AnnotationPoint { x: 10, y: 10 },
            color: color(),
        });
        workspace.add_annotation(Annotation::BlurRect {
            rect: AnnotationRect {
                x: 20,
                y: 20,
                width: 10,
                height: 10,
            },
        });

        assert_eq!(workspace.annotations().len(), 2);
        assert!(matches!(
            workspace.remove_selected(),
            Some(Annotation::BlurRect { .. })
        ));
        assert_eq!(workspace.annotations().len(), 1);
        assert!(workspace.undo());
        assert_eq!(workspace.annotations().len(), 2);
        assert!(workspace.redo());
        assert_eq!(workspace.annotations().len(), 1);
    }

    #[test]
    fn workspace_scales_selected_annotation_to_new_bounds() {
        let mut workspace = AnnotationWorkspace::default();
        workspace.add_annotation(Annotation::RectShape {
            rect: AnnotationRect {
                x: 10,
                y: 10,
                width: 20,
                height: 20,
            },
            color: color(),
        });

        assert!(workspace.scale_selected_to_bounds(AnnotationRect {
            x: 0,
            y: 0,
            width: 40,
            height: 10,
        }));
        assert_eq!(
            workspace.annotations()[0].bounds(),
            AnnotationRect {
                x: 0,
                y: 0,
                width: 40,
                height: 10,
            }
        );
        workspace.clear_selection();
        assert!(!workspace.move_selected(1, 1));
        assert!(workspace.undo());
    }
}
