use oddsnap_core::DefaultCaptureMode;

#[derive(Clone, Copy)]
pub(crate) enum RecordingTarget {
    FullScreen,
    ActiveWindow,
    Region,
}

impl RecordingTarget {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::FullScreen => "desktop",
            Self::ActiveWindow => "active window",
            Self::Region => "region",
        }
    }

    pub(crate) fn capture_mode(self) -> CaptureMode {
        match self {
            Self::FullScreen => CaptureMode::FullScreen,
            Self::ActiveWindow => CaptureMode::ActiveWindow,
            Self::Region => CaptureMode::Rectangle,
        }
    }
}

#[derive(Clone, Copy)]
pub(crate) enum CaptureMode {
    Rectangle,
    FullScreen,
    ActiveWindow,
}

impl CaptureMode {
    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::Rectangle => "Rectangle",
            Self::FullScreen => "Full screen",
            Self::ActiveWindow => "Active window",
        }
    }
}

#[derive(Clone, Copy)]
pub(crate) enum SettingsAction {
    CaptureImageFormat,
    ToggleClipboardCopy,
    ToggleCursor,
    DefaultCaptureMode,
    CaptureDelay,
    ToggleCrosshair,
    ToggleMagnifier,
    ToggleWindowDetection,
    ScrollingCaptureMode,
    RecordingFormat,
    RecordingQuality,
    ToggleStartWithSystem,
    ToggleAutoCheckForUpdates,
    ToggleImageSearchBar,
    ToggleImageSearchFileName,
    ToggleImageSearchOcr,
    ToggleImageSearchExactMatch,
    ToggleImageSearchDiagnostics,
    ToggleImageSearchAutoIndex,
    StartImageSearchReindexQueue,
    CancelImageSearchReindexQueue,
    TranslationModel,
    TranslationSourceLanguage,
    TranslationTargetLanguage,
    InstallArgosTranslationRuntime,
    RemoveArgosTranslationRuntime,
    InstallLocalTranslationRuntime,
    RemoveLocalTranslationRuntime,
    StickerProvider,
    EditRemoveBgApiKey,
    EditPhotoroomApiKey,
    StickerLocalEngine,
    StickerExecutionProvider,
    ToggleStickerShadow,
    ToggleStickerStroke,
    InstallStickerRuntime,
    RemoveStickerRuntime,
    UpscaleProvider,
    EditDeepAiApiKey,
    UpscaleLocalEngine,
    UpscaleExecutionProvider,
    UpscaleScale,
    ToggleUpscalePreview,
    InstallUpscaleRuntime,
    RemoveUpscaleRuntime,
}

#[cfg(any(test, not(target_os = "windows")))]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum CrossPlatformHotkeyEvent {
    Capture,
    Recording,
    FullScreenCapture,
    ActiveWindowCapture,
    ColorPicker,
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
    ScrollCapture,
    AiRedirect,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum PendingTool {
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
    ScrollCapture,
    AiRedirect,
}

impl PendingTool {
    pub(crate) const ALL: [Self; 8] = [
        Self::Ocr,
        Self::Scan,
        Self::Sticker,
        Self::Upscale,
        Self::Center,
        Self::Ruler,
        Self::ScrollCapture,
        Self::AiRedirect,
    ];

    pub(crate) fn label(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "Scan",
            Self::Sticker => "Sticker",
            Self::Upscale => "Upscale",
            Self::Center => "Center",
            Self::Ruler => "Ruler",
            Self::ScrollCapture => "Scroll capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    pub(crate) fn hotkey_summary_label(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "scan",
            Self::Sticker => "sticker",
            Self::Upscale => "upscale",
            Self::Center => "center",
            Self::Ruler => "ruler",
            Self::ScrollCapture => "scroll-capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    fn parity_item(self) -> &'static str {
        match self {
            Self::Ocr => "OCR",
            Self::Scan => "scan",
            Self::Sticker => "sticker/background removal",
            Self::Upscale => "upscale",
            Self::Center => "center selection",
            Self::Ruler => "ruler",
            Self::ScrollCapture => "scroll capture",
            Self::AiRedirect => "AI redirect",
        }
    }

    #[cfg(test)]
    fn default_capture_mode(self) -> Option<DefaultCaptureMode> {
        match self {
            Self::Ocr => Some(DefaultCaptureMode::Ocr),
            Self::Scan => Some(DefaultCaptureMode::Scan),
            Self::Sticker => Some(DefaultCaptureMode::Sticker),
            Self::Upscale => Some(DefaultCaptureMode::Upscale),
            Self::Center => Some(DefaultCaptureMode::Center),
            Self::Ruler => Some(DefaultCaptureMode::Ruler),
            Self::ScrollCapture | Self::AiRedirect => None,
        }
    }

    #[cfg(test)]
    pub(crate) fn from_default_capture_mode(mode: DefaultCaptureMode) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|tool| tool.default_capture_mode() == Some(mode))
    }

    #[cfg(any(test, not(target_os = "windows")))]
    pub(crate) fn cross_platform_hotkey_event(self) -> CrossPlatformHotkeyEvent {
        match self {
            Self::Ocr => CrossPlatformHotkeyEvent::Ocr,
            Self::Scan => CrossPlatformHotkeyEvent::Scan,
            Self::Sticker => CrossPlatformHotkeyEvent::Sticker,
            Self::Upscale => CrossPlatformHotkeyEvent::Upscale,
            Self::Center => CrossPlatformHotkeyEvent::Center,
            Self::Ruler => CrossPlatformHotkeyEvent::Ruler,
            Self::ScrollCapture => CrossPlatformHotkeyEvent::ScrollCapture,
            Self::AiRedirect => CrossPlatformHotkeyEvent::AiRedirect,
        }
    }
}

#[derive(Clone, Copy)]
pub(crate) enum DefaultCaptureAction {
    Capture(CaptureMode),
    ColorPicker,
    Ocr,
    Scan,
    Sticker,
    Upscale,
    Center,
    Ruler,
}

pub(crate) fn default_capture_action(default_mode: DefaultCaptureMode) -> DefaultCaptureAction {
    match default_mode {
        DefaultCaptureMode::ActiveWindow => {
            DefaultCaptureAction::Capture(CaptureMode::ActiveWindow)
        }
        DefaultCaptureMode::Fullscreen => DefaultCaptureAction::Capture(CaptureMode::FullScreen),
        DefaultCaptureMode::Rectangle => DefaultCaptureAction::Capture(CaptureMode::Rectangle),
        DefaultCaptureMode::ColorPicker => DefaultCaptureAction::ColorPicker,
        DefaultCaptureMode::Ocr => DefaultCaptureAction::Ocr,
        DefaultCaptureMode::Scan => DefaultCaptureAction::Scan,
        DefaultCaptureMode::Sticker => DefaultCaptureAction::Sticker,
        DefaultCaptureMode::Upscale => DefaultCaptureAction::Upscale,
        DefaultCaptureMode::Center => DefaultCaptureAction::Center,
        DefaultCaptureMode::Ruler => DefaultCaptureAction::Ruler,
    }
}

pub(crate) fn pending_tool_hotkey_status(tool: PendingTool) -> String {
    pending_tool_trigger_status(&format!("{} hotkey", tool.label()), tool)
}

pub(crate) fn pending_tool_trigger_status(trigger: &str, tool: PendingTool) -> String {
    format!(
        "{trigger} received; Rust {} parity is pending.",
        tool.parity_item()
    )
}

#[cfg(test)]
pub(crate) fn pending_default_capture_status(trigger: &str, tool: PendingTool) -> String {
    format!(
        "{trigger} received; default capture mode '{}' needs Rust {} parity.",
        tool.label(),
        tool.parity_item()
    )
}
