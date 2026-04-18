using System.IO;
using System.Net.Http;

namespace OddSnap.Services;

/// <summary>
/// Downloads Tesseract traineddata files from the official tessdata repository
/// so OCR can recognize non-Latin scripts.
/// </summary>
public static class TessdataService
{
    private const string TessdataBaseUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/";

    /// <summary>All language packs available from tessdata with human-readable labels.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> AvailableLanguages = new[]
    {
        ("afr", "Afrikaans"),
        ("amh", "Amharic"),
        ("ara", "Arabic"),
        ("asm", "Assamese"),
        ("aze", "Azerbaijani"),
        ("bel", "Belarusian"),
        ("ben", "Bengali"),
        ("bod", "Tibetan"),
        ("bos", "Bosnian"),
        ("bre", "Breton"),
        ("bul", "Bulgarian"),
        ("cat", "Catalan"),
        ("ceb", "Cebuano"),
        ("ces", "Czech"),
        ("chi_sim", "Chinese (Simplified)"),
        ("chi_tra", "Chinese (Traditional)"),
        ("chr", "Cherokee"),
        ("cos", "Corsican"),
        ("cym", "Welsh"),
        ("dan", "Danish"),
        ("deu", "German"),
        ("div", "Divehi"),
        ("dzo", "Dzongkha"),
        ("ell", "Greek"),
        ("eng", "English"),
        ("enm", "English (Middle)"),
        ("epo", "Esperanto"),
        ("est", "Estonian"),
        ("eus", "Basque"),
        ("fao", "Faroese"),
        ("fas", "Persian"),
        ("fil", "Filipino"),
        ("fin", "Finnish"),
        ("fra", "French"),
        ("frm", "French (Middle)"),
        ("fry", "Western Frisian"),
        ("gla", "Scottish Gaelic"),
        ("gle", "Irish"),
        ("glg", "Galician"),
        ("grc", "Greek (Ancient)"),
        ("guj", "Gujarati"),
        ("hat", "Haitian"),
        ("heb", "Hebrew"),
        ("hin", "Hindi"),
        ("hrv", "Croatian"),
        ("hun", "Hungarian"),
        ("hye", "Armenian"),
        ("iku", "Inuktitut"),
        ("ind", "Indonesian"),
        ("isl", "Icelandic"),
        ("ita", "Italian"),
        ("jav", "Javanese"),
        ("jpn", "Japanese"),
        ("kan", "Kannada"),
        ("kat", "Georgian"),
        ("kaz", "Kazakh"),
        ("khm", "Khmer"),
        ("kir", "Kyrgyz"),
        ("kor", "Korean"),
        ("lao", "Lao"),
        ("lat", "Latin"),
        ("lav", "Latvian"),
        ("lit", "Lithuanian"),
        ("ltz", "Luxembourgish"),
        ("mal", "Malayalam"),
        ("mar", "Marathi"),
        ("mkd", "Macedonian"),
        ("mlt", "Maltese"),
        ("mon", "Mongolian"),
        ("mri", "Maori"),
        ("msa", "Malay"),
        ("mya", "Myanmar"),
        ("nep", "Nepali"),
        ("nld", "Dutch"),
        ("nor", "Norwegian"),
        ("oci", "Occitan"),
        ("ori", "Oriya"),
        ("pan", "Panjabi"),
        ("pol", "Polish"),
        ("por", "Portuguese"),
        ("pus", "Pashto"),
        ("que", "Quechua"),
        ("ron", "Romanian"),
        ("rus", "Russian"),
        ("san", "Sanskrit"),
        ("sin", "Sinhala"),
        ("slk", "Slovak"),
        ("slv", "Slovenian"),
        ("snd", "Sindhi"),
        ("spa", "Spanish"),
        ("sqi", "Albanian"),
        ("srp", "Serbian"),
        ("srp_latn", "Serbian (Latin)"),
        ("sun", "Sundanese"),
        ("swa", "Swahili"),
        ("swe", "Swedish"),
        ("syr", "Syriac"),
        ("tam", "Tamil"),
        ("tat", "Tatar"),
        ("tel", "Telugu"),
        ("tgk", "Tajik"),
        ("tha", "Thai"),
        ("tir", "Tigrinya"),
        ("ton", "Tonga"),
        ("tur", "Turkish"),
        ("uig", "Uyghur"),
        ("ukr", "Ukrainian"),
        ("urd", "Urdu"),
        ("uzb", "Uzbek"),
        ("vie", "Vietnamese"),
        ("yid", "Yiddish"),
        ("yor", "Yoruba"),
        ("zho", "Chinese"),
    };

    public static string GetLanguageName(string code)
    {
        foreach (var (c, n) in AvailableLanguages)
            if (c.Equals(code, StringComparison.OrdinalIgnoreCase)) return $"{n} ({c})";
        return code;
    }

    public static string GetTessdataDirectory()
    {
        // Use persistent AppData location so downloads survive between launches
        // (single-file apps extract to temp dirs that change each run)
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OddSnap", "Tessdata");
        if (Directory.Exists(appData)) return appData;

        // Fall back to checking next to the exe (for dev builds)
        var baseDir = AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, "Tessdata");
        if (Directory.Exists(dir)) return dir;
        var lower = Path.Combine(baseDir, "tessdata");
        if (Directory.Exists(lower)) return lower;

        // Default to AppData for new downloads
        return appData;
    }

    public static bool IsLanguageInstalled(string code)
    {
        var dir = GetTessdataDirectory();
        return File.Exists(Path.Combine(dir, $"{code}.traineddata"));
    }

    /// <summary>Returns true if at least one OCR language model is downloaded.</summary>
    public static bool HasAnyLanguageInstalled()
    {
        var dir = GetTessdataDirectory();
        if (!Directory.Exists(dir)) return false;
        return Directory.EnumerateFiles(dir, "*.traineddata", SearchOption.TopDirectoryOnly).Any();
    }

    public static async Task DownloadLanguageAsync(string code, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Release any Tesseract engine locks on traineddata files
        OcrService.ClearEngines();
        // Small delay to let file handles close
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        var dir = GetTessdataDirectory();
        Directory.CreateDirectory(dir);

        var targetPath = Path.Combine(dir, $"{code}.traineddata");

        // Clean up any stale temp files from previous failed downloads
        foreach (var tmp in Directory.GetFiles(dir, "*.tmp"))
        {
            try { File.Delete(tmp); } catch { }
        }

        if (File.Exists(targetPath))
        {
            progress?.Report($"{code} already installed");
            return;
        }

        var url = $"{TessdataBaseUrl}{code}.traineddata";
        progress?.Report($"Downloading {code}...");

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tempPath = Path.Combine(dir, $"{code}_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            progress?.Report($"Installed {code}");
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>Returns Tesseract language codes matching the system's installed UI languages.</summary>
    public static List<string> DetectSystemLanguages()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include English
        result.Add("eng");
        seen.Add("eng");

        // Map system culture to Tesseract codes
        var cultures = new[]
        {
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Globalization.CultureInfo.InstalledUICulture,
        };

        foreach (var culture in cultures)
        {
            if (culture == null) continue;
            var iso = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            var iso3 = culture.ThreeLetterISOLanguageName.ToLowerInvariant();

            // Try to match against available languages
            foreach (var (code, _) in AvailableLanguages)
            {
                if (seen.Contains(code)) continue;
                var codeLower = code.ToLowerInvariant();
                if (codeLower == iso3 || codeLower == iso || codeLower.StartsWith(iso + "_"))
                {
                    result.Add(code);
                    seen.Add(code);
                }
            }
        }

        return result;
    }

    /// <summary>Downloads OCR models for detected system languages if none are installed.</summary>
    public static async Task EnsureSystemLanguagesAsync(IProgress<string>? progress = null)
    {
        if (HasAnyLanguageInstalled()) return;

        var langs = DetectSystemLanguages();
        foreach (var code in langs)
        {
            try
            {
                progress?.Report($"Setting up OCR: downloading {code}...");
                await DownloadLanguageAsync(code, progress).ConfigureAwait(false);
            }
            catch { /* non-fatal — continue with other languages */ }
        }

        OcrService.GetAvailableRecognizerLanguages(refresh: true);
    }

    public static bool RemoveLanguage(string code)
    {
        OcrService.ClearEngines();
        var path = Path.Combine(GetTessdataDirectory(), $"{code}.traineddata");
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch { return false; }
    }
}
