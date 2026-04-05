namespace Yoink.Helpers;

public static class FileNameTemplate
{
    public static string Format(string template, int width = 0, int height = 0)
    {
        var now = DateTime.Now;

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
            .Replace("{rand}", Guid.NewGuid().ToString("N").Substring(0, 4));

        // Ensure all filenames start with yoink_
        if (!result.StartsWith("yoink", StringComparison.OrdinalIgnoreCase))
            result = "yoink_" + result;

        // Sanitize invalid filename chars
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');

        return result;
    }

    /// <summary>Format a preset with a fixed example date (2026-04-05 14:30:52) for display.</summary>
    public static string FormatExample(string template)
    {
        return template
            .Replace("{datetime}", "20260405_143052")
            .Replace("{date}", "20260405")
            .Replace("{time}", "143052")
            .Replace("{year}", "2026")
            .Replace("{month}", "04")
            .Replace("{day}", "05")
            .Replace("{hour}", "14")
            .Replace("{min}", "30")
            .Replace("{sec}", "52")
            .Replace("{w}", "1920")
            .Replace("{h}", "1080")
            .Replace("{rand}", "a3f1");
    }

    public static readonly string[] Presets =
    {
        "yoink_{year}-{month}-{day}_{hour}-{min}_{rand}",
        "yoink_{year}-{month}-{day}_{hour}-{min}-{sec}_{rand}",
        "yoink_{year}.{month}.{day}_{hour}.{min}.{sec}_{rand}",
    };
}
