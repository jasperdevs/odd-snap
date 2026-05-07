use std::{fs, path::PathBuf};

use gpui::prelude::FluentBuilder;
use gpui::{
    div, img, px, rgb, Context, InteractiveElement, IntoElement, ObjectFit, ParentElement, Render,
    SharedString, StatefulInteractiveElement, Styled, StyledImage, Window,
};

use crate::{
    copy_image_to_host_clipboard, copy_text_to_host_clipboard, reveal_history_path,
    ProcessedCaptureTool,
};

pub(crate) struct ProcessedResultPreviewWindow {
    tool: ProcessedCaptureTool,
    provider_name: String,
    original_path: Option<PathBuf>,
    result_path: PathBuf,
    mode: PreviewMode,
    status: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum PreviewMode {
    Split,
    Before,
    After,
}

impl PreviewMode {
    fn label(self) -> &'static str {
        match self {
            Self::Split => "Split",
            Self::Before => "Before",
            Self::After => "After",
        }
    }
}

impl ProcessedResultPreviewWindow {
    pub(crate) fn new(
        tool: ProcessedCaptureTool,
        provider_name: String,
        original_path: Option<PathBuf>,
        result_path: PathBuf,
    ) -> Self {
        Self {
            tool,
            provider_name,
            original_path,
            result_path,
            mode: PreviewMode::Split,
            status: format!("{} result ready.", tool.label()),
        }
    }

    fn copy_result_image(&mut self) {
        self.status = match copy_image_to_host_clipboard(&self.result_path) {
            Ok(()) => format!("{} result copied.", self.tool.label()),
            Err(error) => format!("Copy image failed: {error}"),
        };
    }

    fn copy_result_path(&mut self) {
        let path = self.result_path.display().to_string();
        self.status = match copy_text_to_host_clipboard(&path) {
            Ok(()) => "Result path copied.".into(),
            Err(error) => format!("Copy path failed: {error}"),
        };
    }

    fn reveal_result_path(&mut self) {
        self.status = match reveal_history_path(&self.result_path) {
            Ok(action) => format!("{action} {}.", self.result_path.display()),
            Err(error) => format!("Open failed: {error}"),
        };
    }

    fn set_preview_mode(&mut self, mode: PreviewMode) {
        self.mode = mode;
        self.status = format!("Preview mode: {}.", mode.label());
    }

    fn preview_image_panel(&self, label: &'static str, path: Option<PathBuf>) -> impl IntoElement {
        let path_missing = path.is_none();
        div()
            .flex()
            .flex_col()
            .gap(px(7.0))
            .min_w(px(0.0))
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(crate::ui::skin::color(crate::ui::skin::MUTED_TEXT))
                    .child(label),
            )
            .child(
                div()
                    .flex_1()
                    .min_h(px(0.0))
                    .rounded(px(6.0))
                    .border_1()
                    .border_color(rgb(0x2b3039))
                    .bg(rgb(0x0f1116))
                    .p(px(8.0))
                    .when_some(path, |this, path| {
                        this.child(
                            img(path)
                                .size_full()
                                .object_fit(ObjectFit::Contain)
                                .with_loading(|| {
                                    div()
                                        .flex()
                                        .items_center()
                                        .justify_center()
                                        .size_full()
                                        .text_size(px(12.0))
                                        .text_color(rgb(0x8b93a3))
                                        .child("Loading preview")
                                        .into_any_element()
                                })
                                .with_fallback(|| {
                                    div()
                                        .flex()
                                        .items_center()
                                        .justify_center()
                                        .size_full()
                                        .text_size(px(12.0))
                                        .text_color(rgb(0x8b93a3))
                                        .child("Preview unavailable")
                                        .into_any_element()
                                }),
                        )
                    })
                    .when(path_missing, |this| {
                        this.flex()
                            .items_center()
                            .justify_center()
                            .text_size(px(12.0))
                            .text_color(rgb(0x8b93a3))
                            .child("Before preview unavailable")
                    }),
            )
    }

    fn copy_result_image_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        crate::ui::action_button_style(
            div().id("processed-preview-copy-image"),
            crate::ui::ButtonVariant::History,
        )
        .child("Copy image")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_result_image();
            cx.notify();
        }))
    }

    fn copy_result_path_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        crate::ui::action_button_style(
            div().id("processed-preview-copy-path"),
            crate::ui::ButtonVariant::History,
        )
        .child("Copy path")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.copy_result_path();
            cx.notify();
        }))
    }

    fn reveal_result_button(&self, cx: &mut Context<Self>) -> impl IntoElement {
        crate::ui::action_button_style(
            div().id("processed-preview-reveal"),
            crate::ui::ButtonVariant::History,
        )
        .child("Open")
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.reveal_result_path();
            cx.notify();
        }))
    }

    fn preview_mode_button(&self, cx: &mut Context<Self>, mode: PreviewMode) -> impl IntoElement {
        let active = self.mode == mode;
        crate::ui::action_button_style(
            div().id(SharedString::from(format!(
                "processed-preview-mode-{}",
                mode.label().to_ascii_lowercase()
            ))),
            crate::ui::ButtonVariant::History,
        )
        .when(active, |button| button.bg(rgb(0x2d4f75)))
        .child(mode.label())
        .on_click(cx.listener(move |this: &mut Self, _, _, cx| {
            cx.stop_propagation();
            this.set_preview_mode(mode);
            cx.notify();
        }))
    }
}

impl Drop for ProcessedResultPreviewWindow {
    fn drop(&mut self) {
        if let Some(path) = self.original_path.as_ref() {
            let _ = fs::remove_file(path);
        }
    }
}

impl Render for ProcessedResultPreviewWindow {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let title = format!("{} preview", self.tool.label());
        let original_path = self.original_path.clone();
        let result_path = self.result_path.clone();
        let mode = self.mode;

        div()
            .size_full()
            .flex()
            .flex_col()
            .gap(px(10.0))
            .p(px(14.0))
            .bg(crate::ui::skin::color(crate::ui::skin::APP_BG))
            .text_color(crate::ui::skin::color(crate::ui::skin::BRIGHT_TEXT))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(
                        div()
                            .flex()
                            .flex_col()
                            .gap(px(2.0))
                            .child(div().text_size(px(15.0)).child(SharedString::from(title)))
                            .child(
                                div()
                                    .text_size(px(11.0))
                                    .text_color(crate::ui::skin::color(crate::ui::skin::MUTED_TEXT))
                                    .child(SharedString::from(format!(
                                        "Provider: {}",
                                        self.provider_name
                                    ))),
                            ),
                    )
                    .child(
                        div()
                            .flex()
                            .flex_wrap()
                            .gap(px(8.0))
                            .child(self.preview_mode_button(cx, PreviewMode::Split))
                            .child(self.preview_mode_button(cx, PreviewMode::Before))
                            .child(self.preview_mode_button(cx, PreviewMode::After))
                            .child(self.copy_result_image_button(cx))
                            .child(self.copy_result_path_button(cx))
                            .child(self.reveal_result_button(cx)),
                    ),
            )
            .child(
                div()
                    .flex_1()
                    .min_h(px(0.0))
                    .gap(px(10.0))
                    .when(mode == PreviewMode::Split, |panel| {
                        panel
                            .grid()
                            .grid_cols(2)
                            .child(self.preview_image_panel("Before", original_path.clone()))
                            .child(self.preview_image_panel("After", Some(result_path.clone())))
                    })
                    .when(mode == PreviewMode::Before, |panel| {
                        panel.child(self.preview_image_panel("Before", original_path.clone()))
                    })
                    .when(mode == PreviewMode::After, |panel| {
                        panel.child(self.preview_image_panel("After", Some(result_path.clone())))
                    }),
            )
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(crate::ui::skin::color(crate::ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.status.clone())),
            )
    }
}

#[cfg(test)]
mod tests {
    use super::PreviewMode;

    #[test]
    fn preview_mode_labels_are_stable() {
        assert_eq!(PreviewMode::Split.label(), "Split");
        assert_eq!(PreviewMode::Before.label(), "Before");
        assert_eq!(PreviewMode::After.label(), "After");
    }
}
