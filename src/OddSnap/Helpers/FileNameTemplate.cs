namespace OddSnap.Helpers;

public static class FileNameTemplate
{
    public const string DefaultTemplate = "{year}-{month}-{day}-{hour}-{min}-{sec}-{rand}";
    public const string LegacyDefaultTemplate = "oddsnap_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}";

    public const string SourceAppToken = "{app}";

    public static string Format(string template, int width = 0, int height = 0)
    {
        var now = DateTime.Now;
        var randomToken = Guid.NewGuid().ToString("N").Substring(0, 4);
        return Render(template, now, randomToken, width, height, sourceApp: null, appendSourceApp: false);
    }

    /// <summary>
    /// Format a template for a capture taken from <paramref name="sourceApp"/>. When the template has no
    /// <c>{app}</c> token and <paramref name="appendSourceApp"/> is set, the app name is appended instead,
    /// so saved files stay searchable by the app they came from.
    /// </summary>
    public static string Format(string template, int width, int height, string? sourceApp, bool appendSourceApp)
    {
        var now = DateTime.Now;
        var randomToken = Guid.NewGuid().ToString("N").Substring(0, 4);
        return Render(template, now, randomToken, width, height, sourceApp, appendSourceApp);
    }

    /// <summary>Format a preset with a fixed example date (2026-04-05 14:30:52) for display.</summary>
    public static string FormatExample(string template)
        => Render(template, new DateTime(2026, 4, 5, 14, 30, 52), "a3f1", 1920, 1080, sourceApp: null, appendSourceApp: false);

    /// <summary>Preview a template as it will look for a capture taken from <paramref name="sourceApp"/>.</summary>
    public static string FormatExample(string template, string? sourceApp, bool appendSourceApp)
        => Render(template, new DateTime(2026, 4, 5, 14, 30, 52), "a3f1", 1920, 1080, sourceApp, appendSourceApp);

    private static string Render(
        string template,
        DateTime now,
        string randomToken,
        int width,
        int height,
        string? sourceApp,
        bool appendSourceApp)
    {
        bool blankTemplate = string.IsNullOrWhiteSpace(template);
        template = NormalizeLegacyPlaceholders(template);
        if (blankTemplate)
            template = DefaultTemplate;

        var appToken = CaptureSourceApp.SanitizeForFileName(sourceApp);
        bool templateHasAppToken = template.Contains(SourceAppToken, StringComparison.OrdinalIgnoreCase);
        if (appendSourceApp && !templateHasAppToken && appToken is not null)
            template += $"_{SourceAppToken}";

        var result = template
            .Replace(SourceAppToken, appToken ?? "", StringComparison.OrdinalIgnoreCase)
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
            .Replace("{aspect}", FormatAspectRatio(width, height))
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

    private static string FormatAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "";

        var gcd = GreatestCommonDivisor(width, height);
        return $"{width / gcd}x{height / gcd}";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            var next = a % b;
            a = b;
            b = next;
        }

        return Math.Max(1, a);
    }
}
