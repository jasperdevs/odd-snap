namespace OddSnap.Helpers;

public static class FileNameTemplate
{
    public const string DefaultTemplate = "{year}-{month}-{day} {hour}-{min}-{sec} {rand}";
    public const string LegacyDefaultTemplate = "oddsnap_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}";

    public static string Format(string template, int width = 0, int height = 0)
    {
        var now = DateTime.Now;
        var randomToken = Guid.NewGuid().ToString("N").Substring(0, 4);
        return Render(template, now, randomToken, width, height);
    }

    /// <summary>Format a preset with a fixed example date (2026-04-05 14:30:52) for display.</summary>
    public static string FormatExample(string template)
        => Render(template, new DateTime(2026, 4, 5, 14, 30, 52), "a3f1", 1920, 1080);

    private static string Render(string template, DateTime now, string randomToken, int width, int height)
    {
        bool blankTemplate = string.IsNullOrWhiteSpace(template);
        template = NormalizeLegacyPlaceholders(template);
        if (blankTemplate)
            template = DefaultTemplate;

        var result = template
            .Replace("{datetime}", now.ToString("yyyyMMdd_HHmmss"))
            .Replace("{date}", now.ToString("yyyyMMdd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{year}", now.ToString("yyyy"))
            .Replace("{month}", now.ToString("MM"))
            .Replace("{day}", now.ToString("dd"))
            .Replace("{hour}", now.ToString("HH"))
            .Replace("{min}", now.ToString("mm"))
            .Replace("{sec}", now.ToString("ss"))
            .Replace("{w}", width > 0 ? width.ToString() : "")
            .Replace("{h}", height > 0 ? height.ToString() : "")
            .Replace("{rand}", randomToken);

        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');

        while (result.Contains("__", StringComparison.Ordinal))
            result = result.Replace("__", "_", StringComparison.Ordinal);

        result = result.Trim('_', '-', '.', ' ');

        if (string.IsNullOrWhiteSpace(result) || result.Equals("oddsnap", StringComparison.OrdinalIgnoreCase))
            result = $"oddsnap_{now:yyyy-MM-dd_HH-mm-ss}_{randomToken}";

        return result;
    }

    public static readonly string[] Presets =
    {
        "oddsnap_{year}-{month}-{day}_{hour}-{min}_{rand}",
        "oddsnap_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}",
        "oddsnap_{year}.{month}.{day}_{hour}.{min}.{sec}_{rand}",
    };

    private static string NormalizeLegacyPlaceholders(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "{datetime}_{rand}";

        return ReplaceLoosePlaceholder(
            ReplaceLoosePlaceholder(template, "rand", "{rand}"),
            "datetime",
            "{datetime}");
    }

    private static string ReplaceLoosePlaceholder(string template, string token, string replacement)
    {
        var escapedToken = System.Text.RegularExpressions.Regex.Escape(token);

        template = System.Text.RegularExpressions.Regex.Replace(
            template,
            $@"(?i)\(\s*{escapedToken}\s*\)",
            replacement);
        template = System.Text.RegularExpressions.Regex.Replace(
            template,
            $@"(?i)\[\s*{escapedToken}\s*\]",
            replacement);
        template = System.Text.RegularExpressions.Regex.Replace(
            template,
            $@"(?i)(?<![A-Za-z0-9{{\[(]){escapedToken}(?![A-Za-z0-9}}\])])",
            replacement);
        return template;
    }
}
