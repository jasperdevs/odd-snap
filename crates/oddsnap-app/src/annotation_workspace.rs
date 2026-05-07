#![allow(dead_code)]

use oddsnap_core::{hit_test_annotations, Annotation, AnnotationPoint, AnnotationRect};

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

impl AnnotationWorkspace {
    pub(crate) fn annotations(&self) -> &[Annotation] {
        &self.annotations
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
