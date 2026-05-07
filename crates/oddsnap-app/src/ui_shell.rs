use gpui::{
    div, px, rgb, Context, InteractiveElement, IntoElement, ParentElement, Render, SharedString,
    StatefulInteractiveElement, Styled, Window,
};
use oddsnap_core::CapabilityState;

use crate::{copy_text_to_host_clipboard, settings_text, ui};

pub(crate) struct OcrResultWindow {
    text: String,
    status: String,
}

impl OcrResultWindow {
    pub(crate) fn new(text: String) -> Self {
        Self {
            text,
            status: "OCR result ready.".into(),
        }
    }
}

impl Render for OcrResultWindow {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .size_full()
            .bg(ui::skin::color(ui::skin::APP_BG))
            .text_color(ui::skin::color(ui::skin::BRIGHT_TEXT))
            .p(px(16.0))
            .flex()
            .flex_col()
            .gap(px(10.0))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .child(div().text_size(px(15.0)).child("OCR result"))
                    .child(
                        ui::action_button_style(
                            div().id("ocr-result-window-copy"),
                            ui::ButtonVariant::History,
                        )
                        .child("Copy")
                        .on_click(cx.listener(
                            |this: &mut Self, _, _, cx| {
                                cx.stop_propagation();
                                this.status = match copy_text_to_host_clipboard(&this.text) {
                                    Ok(()) => "OCR text copied.".into(),
                                    Err(error) => format!("Copy OCR text failed: {error}"),
                                };
                                cx.notify();
                            },
                        )),
                    ),
            )
            .child(
                div()
                    .id("ocr-result-window-text")
                    .flex_1()
                    .min_h(px(0.0))
                    .overflow_y_scroll()
                    .rounded(px(6.0))
                    .border_1()
                    .border_color(rgb(0x2b3039))
                    .bg(rgb(0x151922))
                    .p(px(12.0))
                    .text_size(px(13.0))
                    .text_color(ui::skin::color(ui::skin::BODY_TEXT))
                    .child(SharedString::from(self.text.clone())),
            )
            .child(
                div()
                    .text_size(px(11.0))
                    .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                    .child(SharedString::from(self.status.clone())),
            )
    }
}

pub(crate) fn settings_text_input_panel(
    input: &settings_text::SettingsTextInputState,
) -> impl IntoElement {
    ui::surface_style(div())
        .flex()
        .flex_col()
        .gap(px(6.0))
        .child(
            div()
                .text_size(px(11.0))
                .text_color(ui::skin::color(ui::skin::MUTED_TEXT))
                .child(SharedString::from(format!(
                    "Editing {}",
                    input.target.label()
                ))),
        )
        .child(
            div()
                .rounded(px(6.0))
                .border_1()
                .border_color(rgb(0x5d6f92))
                .bg(rgb(0x151922))
                .px(px(10.0))
                .py(px(7.0))
                .text_size(px(12.0))
                .text_color(ui::skin::color(ui::skin::BRIGHT_TEXT))
                .child(SharedString::from(input.value.clone())),
        )
}

pub(crate) fn state_color(state: CapabilityState) -> u32 {
    match state {
        CapabilityState::Available => 0x5be49b,
        CapabilityState::InProgress => 0xf4cf65,
        CapabilityState::Blocked => 0xff7a7a,
        CapabilityState::Planned => 0x8b93a3,
    }
}
