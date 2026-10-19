using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace OddSnap.Services;

public sealed record LocalizationLanguage(string Code, string EnglishName, string NativeName, bool IsRightToLeft);

/// <summary>
/// One piece of a runtime-composed string: a translation key plus the arguments
/// that are formatted into it. Text passed to the UI as a formatted string
/// cannot be re-translated later, so the pieces are kept and re-rendered on a
/// language change.
/// </summary>
public readonly record struct LocalizedTextSegment(string Key, params object?[] Args);

public static class LocalizationService
{
    public const string AutoLanguageCode = "auto";
    public const string DefaultLanguageCode = "en";

    private static readonly LocalizationLanguage[] BuiltInLanguages =
    [
        new(DefaultLanguageCode, "English", "English", false),
        new("ar", "Arabic", "العربية", true),
        new("bg", "Bulgarian", "Български", false),
        new("ca", "Catalan", "Català", false),
        new("cs", "Czech", "Čeština", false),
        new("da", "Danish", "Dansk", false),
        new("de", "German", "Deutsch", false),
        new("el", "Greek", "Ελληνικά", false),
        new("es", "Spanish", "Español", false),
        new("et", "Estonian", "Eesti", false),
        new("fi", "Finnish", "Suomi", false),
        new("fr", "French", "Français", false),
        new("he", "Hebrew", "עברית", true),
        new("hi", "Hindi", "हिन्दी", false),
        new("hr", "Croatian", "Hrvatski", false),
        new("hu", "Hungarian", "Magyar", false),
        new("id", "Indonesian", "Indonesia", false),
        new("it", "Italian", "Italiano", false),
        new("ja", "Japanese", "日本語", false),
        new("ko", "Korean", "한국어", false),
        new("lt", "Lithuanian", "Lietuvių", false),
        new("lv", "Latvian", "Latviešu", false),
        new("ms", "Malay", "Melayu", false),
        new("nb", "Norwegian Bokmål", "Norsk bokmål", false),
        new("nl", "Dutch", "Nederlands", false),
        new("pl", "Polish", "Polski", false),
        new("pt-BR", "Portuguese (Brazil)", "Português (Brasil)", false),
        new("pt-PT", "Portuguese (Portugal)", "Português (Portugal)", false),
        new("ro", "Romanian", "Română", false),
        new("ru", "Russian", "Русский", false),
        new("sk", "Slovak", "Slovenčina", false),
        new("sl", "Slovenian", "Slovenščina", false),
        new("sr-Latn", "Serbian (Latin)", "Srpski (latinica)", false),
        new("sv", "Swedish", "Svenska", false),
        new("th", "Thai", "ไทย", false),
        new("tr", "Turkish", "Türkçe", false),
        new("uk", "Ukrainian", "Українська", false),
        new("vi", "Vietnamese", "Tiếng Việt", false),
        new("zh-Hans", "Chinese (Simplified)", "简体中文", false),
        new("zh-Hant", "Chinese (Traditional)", "繁體中文", false),
    ];

    private static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.RegisterAttached("SourceText", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SourceContentProperty =
        DependencyProperty.RegisterAttached("SourceContent", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SourceHeaderProperty =
        DependencyProperty.RegisterAttached("SourceHeader", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SourceToolTipProperty =
        DependencyProperty.RegisterAttached("SourceToolTip", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SourceAutomationNameProperty =
        DependencyProperty.RegisterAttached("SourceAutomationName", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SourceAutomationHelpTextProperty =
        DependencyProperty.RegisterAttached("SourceAutomationHelpText", typeof(string), typeof(LocalizationService));

    private static readonly DependencyProperty SkipContentLocalizationProperty =
        DependencyProperty.RegisterAttached("SkipContentLocalization", typeof(bool), typeof(LocalizationService));

    private static readonly DependencyProperty LocalizationHookedProperty =
        DependencyProperty.RegisterAttached("LocalizationHooked", typeof(bool), typeof(LocalizationService));

    /// <summary>
    /// Set while our own callback is writing a translated value, so the value
    /// watcher does not treat our write as a fresh assignment from app code and
    /// try to translate the translation.
    /// </summary>
    private static readonly DependencyProperty SuppressHookProperty =
        DependencyProperty.RegisterAttached("SuppressHook", typeof(bool), typeof(LocalizationService));

    /// <summary>
    /// Remembers the template and arguments behind a runtime-composed string.
    /// A formatted string ("Use {0} as the default target." + "Bulgarian") is
    /// not a translation key, so it can never be re-translated by looking at the
    /// control's text. Storing the pieces lets the string be regenerated when
    /// the language changes.
    /// </summary>
    private static readonly DependencyProperty LocalizedTextSpecProperty =
        DependencyProperty.RegisterAttached(
            "LocalizedTextSpec",
            typeof(LocalizedTextSegment[]),
            typeof(LocalizationService));

    private static readonly DependencyProperty LocalizedToolTipSpecProperty =
        DependencyProperty.RegisterAttached(
            "LocalizedToolTipSpec",
            typeof(LocalizedTextSegment[]),
            typeof(LocalizationService));

    private static readonly DependencyProperty LocalizedAutomationNameSpecProperty =
        DependencyProperty.RegisterAttached(
            "LocalizedAutomationNameSpec",
            typeof(LocalizedTextSegment[]),
            typeof(LocalizationService));

    private static readonly DependencyProperty LocalizedAutomationHelpTextSpecProperty =
        DependencyProperty.RegisterAttached(
            "LocalizedAutomationHelpTextSpec",
            typeof(LocalizedTextSegment[]),
            typeof(LocalizationService));

    /// <summary>
    /// Assigns a runtime-composed string to a <see cref="TextBlock"/> and
    /// remembers how it was built. String arguments are translated as well,
    /// because they are usually English literals such as a provider name.
    /// </summary>
    public static void SetLocalizedText(TextBlock target, params LocalizedTextSegment[] segments)
    {
        target.SetValue(LocalizedTextSpecProperty, segments);
        target.SetValue(SuppressHookProperty, true);
        try
        {
            target.Text = RenderSegments(segments);
        }
        finally
        {
            target.SetValue(SuppressHookProperty, false);
        }
    }

    /// <summary>
    /// Assigns a runtime-composed tooltip and remembers how it was built, so
    /// <see cref="ApplyTo"/> can regenerate it on a language change.
    /// </summary>
    public static void SetLocalizedToolTip(FrameworkElement target, params LocalizedTextSegment[] segments)
    {
        target.SetValue(LocalizedToolTipSpecProperty, segments);
        target.SetValue(SuppressHookProperty, true);
        try
        {
            target.ToolTip = RenderSegments(segments);
        }
        finally
        {
            target.SetValue(SuppressHookProperty, false);
        }
    }

    /// <summary>
    /// Assigns a runtime-composed accessibility name and remembers how it was
    /// built, so <see cref="ApplyTo"/> can regenerate it on a language change.
    /// </summary>
    public static void SetLocalizedAutomationName(DependencyObject target, params LocalizedTextSegment[] segments)
    {
        target.SetValue(LocalizedAutomationNameSpecProperty, segments);
        AutomationProperties.SetName(target, RenderSegments(segments));
    }

    /// <summary>
    /// Assigns a runtime-composed accessibility help text and remembers how it
    /// was built, so <see cref="ApplyTo"/> can regenerate it on a language change.
    /// </summary>
    public static void SetLocalizedAutomationHelpText(DependencyObject target, params LocalizedTextSegment[] segments)
    {
        target.SetValue(LocalizedAutomationHelpTextSpecProperty, segments);
        AutomationProperties.SetHelpText(target, RenderSegments(segments));
    }

    /// <summary>
    /// Clears a previously registered composed string, for example when the
    /// control is switched back to a plain literal.
    /// </summary>
    public static void ClearLocalizedText(TextBlock target)
        => target.ClearValue(LocalizedTextSpecProperty);

    /// <summary>
    /// Formats a composed string from its template and arguments. Each template
    /// key and each string argument is looked up in the active language.
    /// </summary>
    private static string RenderSegments(LocalizedTextSegment[] segments)
    {
        var parts = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var args = new object?[segment.Args.Length];
            for (var a = 0; a < args.Length; a++)
                args[a] = segment.Args[a] is string s ? Translate(s) : segment.Args[a];

            parts[i] = string.Format(Translate(segment.Key), args);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Marks a control whose <b>content</b> is data (a language name, a device
    /// name, a model name) so <see cref="ApplyTo"/> leaves that text alone.
    /// Tooltips and accessibility strings on the same control are still
    /// localized, because those are prose.
    /// </summary>
    public static void SetSkipContentLocalization(DependencyObject element, bool value)
        => element.SetValue(SkipContentLocalizationProperty, value);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> TranslationCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised after the interface language changes. Controls that build text by
    /// formatting a template (tooltips such as "Open {0} after a capture") cannot
    /// be re-translated by <see cref="ApplyTo"/>, because the formatted result is
    /// not a lookup key. They subscribe here and rebuild instead.
    /// </summary>
    public static event Action? LanguageChanged;

    internal static void RaiseLanguageChanged() => LanguageChanged?.Invoke();

    public static string CurrentLanguageCode { get; private set; } = DefaultLanguageCode;
    public static string CurrentLanguageSetting { get; private set; } = AutoLanguageCode;

    /// <summary>
    /// The <see cref="CultureInfo"/> that matches the active interface
    /// language, for date and number formatting in the UI.
    /// </summary>
    public static CultureInfo CurrentCulture
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(CurrentLanguageCode);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }

    public static IReadOnlyList<LocalizationLanguage> Languages => GetAvailableLanguages();

    public static bool HasInterfaceTranslations(string languageCode)
    {
        var normalized = NormalizeLanguageSetting(languageCode);
        if (string.Equals(normalized, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(normalized, AutoLanguageCode, StringComparison.OrdinalIgnoreCase))
            normalized = ResolveLanguageCode(normalized);

        return File.Exists(Path.Combine(GetLocalizationDirectory(), $"{normalized}.json"));
    }

    public static string NormalizeLanguageSetting(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) ||
            string.Equals(languageCode, AutoLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return AutoLanguageCode;
        }

        var normalized = NormalizeLanguageAlias(languageCode.Trim().Replace('_', '-'));
        var languages = Languages;
        var exact = languages.FirstOrDefault(language =>
            string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Code;

        var neutral = NormalizeLanguageAlias(normalized.Split('-', 2)[0]);
        return languages.Any(language => string.Equals(language.Code, neutral, StringComparison.OrdinalIgnoreCase))
            ? neutral
            : AutoLanguageCode;
    }

    private static string NormalizeLanguageAlias(string languageCode)
    {
        if (languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return languageCode.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                   languageCode.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                   languageCode.Equals("zh-MO", StringComparison.OrdinalIgnoreCase) ||
                   languageCode.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        if (languageCode.Equals("pt", StringComparison.OrdinalIgnoreCase))
            return "pt-BR";

        if (languageCode.Equals("no", StringComparison.OrdinalIgnoreCase))
            return "nb";

        return languageCode;
    }

    public static string ResolveLanguageCode(string? languageSetting, CultureInfo? systemCulture = null)
    {
        var normalized = NormalizeLanguageSetting(languageSetting);
        if (!string.Equals(normalized, AutoLanguageCode, StringComparison.OrdinalIgnoreCase))
            return normalized;

        var culture = systemCulture ?? CultureInfo.CurrentUICulture;
        var languages = Languages;
        var exact = languages.FirstOrDefault(language =>
            string.Equals(language.Code, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Code;

        var neutral = culture.TwoLetterISOLanguageName;
        return languages.Any(language => string.Equals(language.Code, neutral, StringComparison.OrdinalIgnoreCase))
            ? neutral
            : DefaultLanguageCode;
    }

    public static LocalizationLanguage GetLanguage(string languageCode)
    {
        var resolved = ResolveLanguageCode(languageCode);
        return Languages.First(language => string.Equals(language.Code, resolved, StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplyCurrentCulture(string? languageSetting)
    {
        CurrentLanguageSetting = NormalizeLanguageSetting(languageSetting);
        var previousLanguageCode = CurrentLanguageCode;
        var languageCode = ResolveLanguageCode(CurrentLanguageSetting);
        CurrentLanguageCode = languageCode;

        try
        {
            var culture = CultureInfo.GetCultureInfo(languageCode);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
        }
        catch (CultureNotFoundException)
        {
            CurrentLanguageCode = DefaultLanguageCode;
        }

        PublishUiFontResources(CurrentLanguageCode);

        if (!string.Equals(previousLanguageCode, CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
            RaiseLanguageChanged();
    }

    /// <summary>
    /// Comma-separated WPF font stack for the active interface language. The first family
    /// is the Windows 11 Latin UI face; the families after it are CJK fallbacks so CJK
    /// glyphs land on a known screen font instead of WPF's language-based auto-fallback.
    /// That fallback resolves zh-Hant to "Microsoft JhengHei UI", which is noticeably
    /// lighter than the YaHei used for zh-Hans and renders as faint, broken-looking
    /// strokes at the 11-13px sizes this app uses. Nothing is bundled; the stack relies
    /// only on fonts that ship with Windows. Keep the App.xaml defaults (UiFontFamily /
    /// UiDisplayFontFamily) in sync with this.
    /// </summary>
    public static string UiFontStack(string? languageCode = null)
        => BuildUiFontStack("Segoe UI Variable Text", languageCode);

    /// <inheritdoc cref="UiFontStack"/>
    public static string UiDisplayFontStack(string? languageCode = null)
        => BuildUiFontStack("Segoe UI Variable Display", languageCode);

    private static string BuildUiFontStack(string latinFamily, string? languageCode)
    {
        var code = ResolveLanguageCode(languageCode ?? CurrentLanguageCode);
        var cjk = code switch
        {
            // YaHei is heavier and crisper than JhengHei at UI sizes; JhengHei stays in the
            // list as a safety net for the rare glyph YaHei does not carry.
            var c when c.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                => "Microsoft YaHei UI, Microsoft JhengHei UI",
            var c when c.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                => "Yu Gothic UI, Meiryo UI, Microsoft YaHei UI",
            var c when c.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
                => "Malgun Gothic, Microsoft YaHei UI",
            // Everything else (Latin, Cyrillic, Arabic, Thai, …) is covered by the Latin face
            // plus WPF's own fallback; the CJK families are only a last resort.
            _ => "Microsoft YaHei UI, Microsoft JhengHei UI, Malgun Gothic, Yu Gothic UI",
        };

        return $"{latinFamily}, {cjk}, Segoe UI";
    }

    /// <summary>
    /// Writes the per-language font stacks into the application resources so every
    /// <c>{DynamicResource UiFontFamily}</c> / <c>{DynamicResource UiDisplayFontFamily}</c>
    /// reference re-resolves. No-ops before the application exists.
    /// </summary>
    public static void PublishUiFontResources(string? languageCode = null)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        void Apply()
        {
            app.Resources["UiFontFamily"] = new System.Windows.Media.FontFamily(UiFontStack(languageCode));
            app.Resources["UiDisplayFontFamily"] = new System.Windows.Media.FontFamily(UiDisplayFontStack(languageCode));
        }

        if (app.Dispatcher.CheckAccess())
            Apply();
        else
            app.Dispatcher.BeginInvoke((Action)Apply);
    }

    public static string ResolveContentLanguageCode(string? languageSetting = null, CultureInfo? systemCulture = null)
    {
        var normalized = NormalizeLanguageSetting(languageSetting ?? CurrentLanguageSetting);
        if (!string.Equals(normalized, AutoLanguageCode, StringComparison.OrdinalIgnoreCase))
            return ResolveLanguageCode(normalized);

        var culture = systemCulture ?? CultureInfo.InstalledUICulture;
        return string.IsNullOrWhiteSpace(culture.Name) ? DefaultLanguageCode : culture.Name;
    }

    public static string Translate(string text) => Translate(CurrentLanguageCode, text);

    public static string Translate(string languageCode, string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var resolved = ResolveLanguageCode(languageCode);
        if (string.Equals(resolved, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            return ToSourceKey(text);

        var translations = GetTranslations(resolved);
        if (TryLookupIgnoringPadding(translations, text, out var translated))
            return translated;

        // No entry for an explicit Translate() call: the string will show in
        // English inside a translated UI, so record where it came from.
        NoteMiss("Translate", resolved, text);
        return text;
    }

    /// <summary>
    /// Looks <paramref name="text"/> up in a translation table. XAML text often
    /// carries insignificant padding — for example
    /// <c>Text="  100MB max · Free"</c> uses leading spaces as a chip's inner
    /// spacing — so when the padded form is not a key the trimmed form is tried
    /// and the surrounding whitespace is re-applied to the result. Without this
    /// such labels silently stay English in every translated UI.
    /// </summary>
    private static bool TryLookupIgnoringPadding(
        IReadOnlyDictionary<string, string> translations,
        string text,
        out string result)
    {
        if (translations.TryGetValue(text, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            result = exact;
            return true;
        }

        var trimmed = text.Trim();
        if (trimmed.Length != 0 &&
            trimmed.Length != text.Length &&
            translations.TryGetValue(trimmed, out var inner) &&
            !string.IsNullOrWhiteSpace(inner))
        {
            var leadingLength = text.Length - text.TrimStart().Length;
            var trailingLength = text.Length - text.TrimEnd().Length;
            result = text[..leadingLength] + inner + text[(text.Length - trailingLength)..];
            return true;
        }

        result = text;
        return false;
    }

    private static readonly HashSet<string> MissLog = new(StringComparer.Ordinal);

    private static void NoteMiss(string origin, string languageCode, string text)
    {
        // A string with no Latin letters is already translated (or is data), so
        // reporting it would bury the real gaps in noise.
        var hasLatinLetter = false;
        foreach (var ch in text)
        {
            if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                hasLatinLetter = true;
                break;
            }
        }

        if (!hasLatinLetter)
            return;

        string fingerprint = languageCode + "\u0000" + origin + "\u0000" + text;
        lock (Gate)
        {
            if (!MissLog.Add(fingerprint))
                return;
        }

        AppDiagnostics.LogInfo("localization.miss", $"{origin} [{languageCode}]: {text}");
    }

    /// <summary>
    /// Maps an already-translated string back to its English source key, so a
    /// control that was localized once can be re-localized after a language
    /// switch. Without this an element that holds Chinese would keep the
    /// Chinese text when the user picks English, because the value already on
    /// the control looks like a valid source string.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> ReverseCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> GetReverseIndex(string languageCode)
    {
        lock (Gate)
        {
            if (ReverseCache.TryGetValue(languageCode, out var cached))
                return cached;

            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in GetTranslations(languageCode))
            {
                // First write wins: if two keys share a translation we keep the
                // earlier one so the mapping stays stable between runs.
                if (!string.IsNullOrWhiteSpace(pair.Value) && !reverse.ContainsKey(pair.Value))
                    reverse[pair.Value] = pair.Key;
            }

            ReverseCache[languageCode] = reverse;
            return reverse;
        }
    }

    public static void ApplyTo(DependencyObject root, string? languageSetting = null)
    {
        var languageCode = ResolveLanguageCode(languageSetting ?? CurrentLanguageCode);
        CurrentLanguageCode = languageCode;

        // From here on, anything added to this tree is localized the moment it
        // loads. Windows and panels build items in code at unpredictable times,
        // and without this the same control would show English or Chinese
        // depending on whether a later ApplyTo happened to run.
        if (root is FrameworkElement element)
            element.Loaded -= OnElementLoaded;
        if (root is FrameworkElement loadedRoot)
            loadedRoot.Loaded += OnElementLoaded;

        LocalizeTree(root, languageCode);
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var code = CurrentLanguageCode;
        if (string.Equals(code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            return;

        var language = GetLanguage(code);
        var flowDirection = language.IsRightToLeft
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

        ApplyToElement(element, code, flowDirection);
    }

    private static void LocalizeTree(DependencyObject root, string languageCode)
    {
        var language = GetLanguage(languageCode);
        var flowDirection = language.IsRightToLeft ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        foreach (var element in Enumerate(root))
        {
            try
            {
                ApplyToElement(element, languageCode, flowDirection);
                EnsureHooked(element);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("localization.apply-element", ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Watches the localizable dependency properties on <paramref name="element"/>
    /// so that text assigned by app code <i>after</i> the initial pass is
    /// translated as well. Without this the UI would show English or Chinese
    /// depending on whether the assignment happened before or after the last
    /// <see cref="ApplyTo"/> call — the flicker between translated and untranslated
    /// labels. TextBlock.Text is a plain CLR property, so it is covered by the
    /// Loaded handler and by explicit <see cref="Translate(string)"/> calls instead.
    /// </summary>
    private static void EnsureHooked(DependencyObject element)
    {
        if (element.GetValue(LocalizationHookedProperty) is bool hooked && hooked)
            return;

        var ownerType = element.GetType();
        var any = false;

        any |= TryHook(element, ownerType, ContentControl.ContentProperty);
        any |= TryHook(element, ownerType, HeaderedContentControl.HeaderProperty);
        any |= TryHook(element, ownerType, ToolTipService.ToolTipProperty);

        if (element is Window)
            any |= TryHook(element, ownerType, Window.TitleProperty);

        any |= TryHook(element, ownerType, AutomationProperties.NameProperty);
        any |= TryHook(element, ownerType, AutomationProperties.HelpTextProperty);

        if (any)
            element.SetValue(LocalizationHookedProperty, true);
    }

    private static bool TryHook(DependencyObject element, Type ownerType, DependencyProperty property)
    {
        try
        {
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, ownerType);
            if (descriptor is null)
                return false;

            descriptor.RemoveValueChanged(element, OnWatchedValueChanged);
            descriptor.AddValueChanged(element, OnWatchedValueChanged);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("localization.hook-property", ex.Message, ex);
            return false;
        }
    }

    /// <summary>
    /// Fires whenever a watched property is assigned. Re-entrant writes made by
    /// the localization pass itself are ignored via <see cref="SuppressHookProperty"/>.
    /// </summary>
    private static void OnWatchedValueChanged(object? sender, EventArgs e)
    {
        if (sender is not DependencyObject element)
            return;

        if (element.GetValue(SuppressHookProperty) is bool suppressed && suppressed)
            return;

        // This is a fresh assignment from app code, so whatever it just wrote is
        // the new source text. Refresh the remembered originals first, otherwise
        // GetOrSetSource would hand back the value from the previous assignment
        // and the control would snap back to it (a Select button that toggles to
        // Done would immediately revert to Select).
        RefreshSourceFromCurrentValues(element);

        var code = CurrentLanguageCode;
        if (string.Equals(code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            return;

        var language = GetLanguage(code);
        var flowDirection = language.IsRightToLeft
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

        try
        {
            ApplyToElement(element, code, flowDirection);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("localization.hook-value", ex.Message, ex);
        }
    }

    /// <summary>
    /// Re-reads the localizable properties on <paramref name="element"/> and
    /// stores their current values as the new English originals.
    /// </summary>
    private static void RefreshSourceFromCurrentValues(DependencyObject element)
    {
        if (element is Window window)
        {
            if (!string.IsNullOrWhiteSpace(window.Title))
                window.SetValue(SourceTextProperty, ToSourceKey(window.Title));
        }

        if (element is HeaderedContentControl headered && headered.Header is string header)
            headered.SetValue(SourceHeaderProperty, ToSourceKey(header));

        if (element is ContentControl contentControl && contentControl.Content is string content)
            contentControl.SetValue(SourceContentProperty, ToSourceKey(content));

        if (element is FrameworkElement { ToolTip: string toolTip } toolTipElement)
            toolTipElement.SetValue(SourceToolTipProperty, ToSourceKey(toolTip));

        if (AutomationProperties.GetName(element) is { Length: > 0 } autoName)
            element.SetValue(SourceAutomationNameProperty, ToSourceKey(autoName));

        if (AutomationProperties.GetHelpText(element) is { Length: > 0 } helpText)
            element.SetValue(SourceAutomationHelpTextProperty, ToSourceKey(helpText));
    }

    private static void ApplyToElement(DependencyObject element, string languageCode, System.Windows.FlowDirection flowDirection)
    {
        // Guard the whole pass: every write below raises the value watcher, and
        // without this flag the watcher would immediately translate the value we
        // just wrote (and could loop).
        if (element.GetValue(SuppressHookProperty) is not bool suppressed || !suppressed)
        {
            element.SetValue(SuppressHookProperty, true);
            try
            {
                ApplyToElementCore(element, languageCode, flowDirection);
            }
            finally
            {
                element.SetValue(SuppressHookProperty, false);
            }
        }
        else
        {
            ApplyToElementCore(element, languageCode, flowDirection);
        }
    }

    private static void ApplyToElementCore(DependencyObject element, string languageCode, System.Windows.FlowDirection flowDirection)
    {
        // Content marked as data (a language name, a device name, a model name)
        // keeps its authored text, but the tooltip and accessibility strings on
        // the same element are prose and still need localizing.
        var skipContent = element.GetValue(SkipContentLocalizationProperty) is bool skip && skip;

        if (element is FrameworkElement frameworkElement)
        {
            frameworkElement.FlowDirection = ShouldPreserveLeftToRight(frameworkElement)
                ? System.Windows.FlowDirection.LeftToRight
                : flowDirection;
            try { frameworkElement.Language = XmlLanguage.GetLanguage(languageCode); } catch { }
        }

        if (element is Window window)
        {
            window.FlowDirection = flowDirection;
            if (!string.IsNullOrWhiteSpace(window.Title))
            {
                var source = GetOrSetSource(window, SourceTextProperty, window.Title);
                window.Title = TranslateField(window, "Title", languageCode, source);
            }
        }

        if (element is TextBlock textBlock)
        {
            if (IsIconTextBlock(textBlock))
            {
                textBlock.FlowDirection = System.Windows.FlowDirection.LeftToRight;
                return;
            }

            if (textBlock.Inlines.Count > 0)
            {
                foreach (var inline in textBlock.Inlines.ToArray())
                    ApplyToInline(inline, languageCode);
            }
            else if (textBlock.GetValue(LocalizedTextSpecProperty) is LocalizedTextSegment[] spec && spec.Length > 0)
            {
                // The text is composed from a template and arguments, so it is
                // regenerated rather than reverse-translated. This is the only
                // correct treatment: the formatted result is not a key.
                textBlock.Text = RenderSegments(spec);
            }
            else if (!string.IsNullOrEmpty(textBlock.Text) && !HasTextBinding(textBlock))
            {
                var source = GetOrSetSource(textBlock, SourceTextProperty, textBlock.Text);
                textBlock.Text = TranslateField(textBlock, "Text", languageCode, source);
            }
        }

        if (element is HeaderedContentControl headered && headered.Header is string header)
        {
            var source = GetOrSetSource(headered, SourceHeaderProperty, header);
            headered.Header = TranslateField(headered, "Header", languageCode, source);
        }

        // MenuItem and other HeaderedItemsControl hosts keep their caption in
        // Header too, but they do not derive from HeaderedContentControl, so
        // they need their own branch (the Filters menu and card action menus).
        if (element is System.Windows.Controls.HeaderedItemsControl headeredItems &&
            element is not HeaderedContentControl &&
            headeredItems.Header is string itemsHeader)
        {
            var source = GetOrSetSource(headeredItems, SourceHeaderProperty, itemsHeader);
            headeredItems.Header = TranslateField(headeredItems, "Header", languageCode, source);
        }

        if (element is ContentControl contentControl && contentControl.Content is string content && !skipContent)
        {
            var source = GetOrSetSource(contentControl, SourceContentProperty, content);
            contentControl.Content = TranslateField(contentControl, "Content", languageCode, source);
        }

        if (element is FrameworkElement toolTipElement)
        {
            if (toolTipElement.GetValue(LocalizedToolTipSpecProperty) is LocalizedTextSegment[] toolTipSpec && toolTipSpec.Length > 0)
            {
                toolTipElement.ToolTip = RenderSegments(toolTipSpec);
            }
            else if (toolTipElement.ToolTip is string toolTip)
            {
                var source = GetOrSetSource(toolTipElement, SourceToolTipProperty, toolTip);
                toolTipElement.ToolTip = TranslateField(toolTipElement, "ToolTip", languageCode, source);
            }
        }

        // Accessibility strings are user-facing too (screen readers, narrator),
        // and they are frequently built in code, so they need the same
        // original-text-then-retranslate treatment as Text and ToolTip.
        if (element is DependencyObject accessible)
        {
            if (accessible.GetValue(LocalizedAutomationNameSpecProperty) is LocalizedTextSegment[] nameSpec && nameSpec.Length > 0)
            {
                AutomationProperties.SetName(accessible, RenderSegments(nameSpec));
            }
            else if (AutomationProperties.GetName(accessible) is { Length: > 0 } autoName)
            {
                var source = GetOrSetSource(accessible, SourceAutomationNameProperty, autoName);
                AutomationProperties.SetName(accessible, TranslateField(accessible, "AutomationName", languageCode, source));
            }

            if (accessible.GetValue(LocalizedAutomationHelpTextSpecProperty) is LocalizedTextSegment[] helpSpec && helpSpec.Length > 0)
            {
                AutomationProperties.SetHelpText(accessible, RenderSegments(helpSpec));
            }
            else if (AutomationProperties.GetHelpText(accessible) is { Length: > 0 } helpText)
            {
                var source = GetOrSetSource(accessible, SourceAutomationHelpTextProperty, helpText);
                AutomationProperties.SetHelpText(accessible, TranslateField(accessible, "AutomationHelpText", languageCode, source));
            }
        }
    }

    /// <summary>
    /// Translates one field of one element and records the element type and
    /// field name when no translation exists, so an in-app English label inside
    /// a translated UI can be traced back to the control that produced it.
    /// </summary>
    private static string TranslateField(DependencyObject element, string field, string languageCode, string source)
    {
        var result = Translate(languageCode, source);
        if (ReferenceEquals(result, source) && !string.IsNullOrWhiteSpace(source))
            NoteMiss($"<{element.GetType().Name}>.{field}", languageCode, source);
        return result;
    }

    private static string GetOrSetSource(DependencyObject element, DependencyProperty property, string currentValue)
    {
        if (element.GetValue(property) is string existing)
            return existing;

        // The value on the control may already be a translation, because a
        // text assignment elsewhere could have localized it first. Store the
        // English key instead, otherwise switching back to English would keep
        // showing the translated text.
        var source = ToSourceKey(currentValue);
        element.SetValue(property, source);
        return source;
    }

    /// <summary>
    /// Returns the English source key for <paramref name="value"/> by looking
    /// it up in every loaded translation table. If the value is already an
    /// English key (or unknown) it is returned unchanged. Controls that show a
    /// title stored in a custom property use this to remember the authored text
    /// while a language is active.
    /// </summary>
    public static string ToSourceKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Fast path: the string is itself a known key.
        if (GetTranslations(DefaultLanguageCode) is { } english && english.ContainsKey(value))
            return value;

        // Padded text (XAML uses leading spaces as inner spacing) maps to the
        // trimmed key, so the stored source stays clean.
        var trimmed = value.Trim();
        if (trimmed.Length != 0 && trimmed.Length != value.Length &&
            GetTranslations(DefaultLanguageCode) is { } englishTrimmed && englishTrimmed.ContainsKey(trimmed))
        {
            return trimmed;
        }

        foreach (var language in GetAvailableLanguages())
        {
            if (string.Equals(language.Code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (GetReverseIndex(language.Code).TryGetValue(value, out var englishKey))
                return englishKey;
        }

        return value;
    }

    private static bool ShouldPreserveLeftToRight(FrameworkElement element) =>
        element is TextBlock textBlock && IsIconTextBlock(textBlock);

    private static bool IsIconTextBlock(TextBlock textBlock)
    {
        if (IsIconFont(textBlock.FontFamily))
            return true;

        return IsPrivateUseGlyph(textBlock.Text);
    }

    /// <summary>
    /// True when the text is supplied by a binding. Writing to such a property
    /// would overwrite the binding, so the value has to be localized at its
    /// source instead (for example <c>OddSnapTitleBar</c> translates its own
    /// Title and the binding then shows the translated value).
    /// </summary>
    private static bool HasTextBinding(TextBlock textBlock) =>
        System.Windows.Data.BindingOperations.GetBindingExpression(
            textBlock, TextBlock.TextProperty) is not null;

    private static bool IsIconFont(System.Windows.Media.FontFamily? fontFamily)
    {
        var family = fontFamily?.ToString();
        return !string.IsNullOrWhiteSpace(family) &&
               (family.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateUseGlyph(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return trimmed.Length <= 2 && trimmed.All(ch => ch is >= '\uE000' and <= '\uF8FF');
    }

    private static void ApplyToInline(Inline inline, string languageCode)
    {
        if (inline is Run run)
        {
            if (string.IsNullOrEmpty(run.Text))
                return;

            var source = GetOrSetSource(run, SourceTextProperty, run.Text);
            run.Text = Translate(languageCode, source);
            return;
        }

        if (inline is Span span)
        {
            foreach (var child in span.Inlines.ToArray())
                ApplyToInline(child, languageCode);
        }
    }

    private static IEnumerable<DependencyObject> Enumerate(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            DependencyObject[] logicalChildren;
            try
            {
                logicalChildren = LogicalTreeHelper.GetChildren(current)
                    .OfType<DependencyObject>()
                    .ToArray();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("localization.enumerate-logical", ex.Message, ex);
                logicalChildren = [];
            }

            foreach (var child in logicalChildren)
                stack.Push(child);

            // A ContextMenu is not part of the logical or visual tree of its
            // owner — it lives in its own popup tree — so its items would never
            // be visited. Push it explicitly so menu captions get localized.
            if (current is FrameworkElement { ContextMenu: { } contextMenu })
                stack.Push(contextMenu);

            int visualChildren;
            try { visualChildren = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); }
            catch { visualChildren = 0; }

            for (int i = visualChildren - 1; i >= 0; i--)
            {
                try { stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i)); }
                catch { }
            }
        }
    }

    private static IReadOnlyDictionary<string, string> GetTranslations(string languageCode)
    {
        lock (Gate)
        {
            if (TranslationCache.TryGetValue(languageCode, out var cached))
                return cached;

            var loaded = LoadTranslations(languageCode);
            TranslationCache[languageCode] = loaded;
            return loaded;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadTranslations(string languageCode)
    {
        var path = Path.Combine(GetLocalizationDirectory(), $"{languageCode}.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ??
                   new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("localization.load", ex, $"Failed to load {path}.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyList<LocalizationLanguage> GetAvailableLanguages()
    {
        var languages = new Dictionary<string, LocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in BuiltInLanguages)
            languages[language.Code] = language;

        var directory = GetLocalizationDirectory();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(code) && !languages.ContainsKey(code))
                    languages[code] = CreateLanguageFromCode(code);
            }
        }

        return languages.Values
            .OrderBy(language => string.Equals(language.Code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(language => language.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LocalizationLanguage CreateLanguageFromCode(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return new LocalizationLanguage(code, culture.EnglishName, culture.NativeName, culture.TextInfo.IsRightToLeft);
        }
        catch (CultureNotFoundException)
        {
            return new LocalizationLanguage(code, code, code, false);
        }
    }

    private static string GetLocalizationDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Localization");
}
