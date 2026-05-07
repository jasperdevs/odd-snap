use gpui::KeyDownEvent;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum SettingsTextTarget {
    RemoveBg,
    Photoroom,
    DeepAi,
}

impl SettingsTextTarget {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::RemoveBg => "Remove.bg API key",
            Self::Photoroom => "Photoroom API key",
            Self::DeepAi => "DeepAI API key",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct SettingsTextInputState {
    pub(crate) target: SettingsTextTarget,
    pub(crate) value: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum SettingsTextInputEvent {
    Editing,
    Commit {
        target: SettingsTextTarget,
        value: String,
    },
    Cancel,
    Ignored,
}

impl SettingsTextInputState {
    pub(crate) fn new(target: SettingsTextTarget, value: String) -> Self {
        Self { target, value }
    }

    pub(crate) fn handle_key_down(&mut self, event: &KeyDownEvent) -> SettingsTextInputEvent {
        if event.keystroke.modifiers.control
            || event.keystroke.modifiers.platform
            || event.keystroke.modifiers.alt
        {
            return SettingsTextInputEvent::Ignored;
        }

        let key = event.keystroke.key.to_ascii_lowercase();
        match key.as_str() {
            "escape" => SettingsTextInputEvent::Cancel,
            "enter" => SettingsTextInputEvent::Commit {
                target: self.target,
                value: self.value.trim().to_string(),
            },
            "backspace" => {
                self.value.pop();
                SettingsTextInputEvent::Editing
            }
            "delete" => {
                self.value.clear();
                SettingsTextInputEvent::Editing
            }
            "space" | "tab" | "shift" | "control" | "alt" | "cmd" | "super" => {
                SettingsTextInputEvent::Ignored
            }
            _ => {
                let Some(text) = event.keystroke.key_char.as_deref() else {
                    return SettingsTextInputEvent::Ignored;
                };
                if text.chars().any(char::is_control) {
                    return SettingsTextInputEvent::Ignored;
                }
                self.value.push_str(text);
                SettingsTextInputEvent::Editing
            }
        }
    }
}

pub(crate) fn masked_secret_summary(value: &str) -> String {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return "not set".into();
    }

    let char_count = trimmed.chars().count();
    if char_count <= 4 {
        return "set".into();
    }

    let suffix = trimmed
        .chars()
        .rev()
        .take(4)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect::<String>();
    format!("set ...{suffix}")
}

#[cfg(test)]
mod tests {
    use super::masked_secret_summary;

    #[test]
    fn masked_secret_summary_hides_most_of_configured_key() {
        assert_eq!(masked_secret_summary(""), "not set");
        assert_eq!(masked_secret_summary("abcd"), "set");
        assert_eq!(masked_secret_summary("secret-token"), "set ...oken");
    }
}
