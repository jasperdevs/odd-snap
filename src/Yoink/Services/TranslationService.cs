using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Yoink.Services;

public enum TranslationModel
{
    Argos = 0,
    Google = 1,
    OpenSourceLocal = 2
}

public static class TranslationService
{
    private const string PythonLauncherArg = "-3";
    private static readonly TimeSpan ArgosProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HttpClient GoogleHttp = CreateGoogleHttpClient();
    private static readonly string ArgosStateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yoink",
        "argos");
    private static readonly string ArgosMarkerPath = Path.Combine(ArgosStateDir, "runtime.marker");
    private static readonly object ArgosProbeGate = new();
    private static bool? _cachedArgosReady;
    private static string _cachedArgosStatus = "Checking install state...";
    private static DateTime _cachedArgosCheckedUtc;

    private sealed record PythonRunResult(int ExitCode, string StdOut, string StdErr);

    public static string GetModelLabel(TranslationModel model) => model switch
    {
        TranslationModel.Argos => "Argos Translate",
        TranslationModel.Google => "Google Translate",
        TranslationModel.OpenSourceLocal => "Open-source Local",
        _ => "Argos Translate"
    };

    public static readonly IReadOnlyList<(string Code, string Name)> SupportedLanguages = new[]
    {
        ("auto", "Auto-detect"),
        ("ar", "Arabic"),
        ("az", "Azerbaijani"),
        ("bg", "Bulgarian"),
        ("bn", "Bengali"),
        ("ca", "Catalan"),
        ("cs", "Czech"),
        ("da", "Danish"),
        ("de", "German"),
        ("el", "Greek"),
        ("en", "English"),
        ("eo", "Esperanto"),
        ("es", "Spanish"),
        ("et", "Estonian"),
        ("fa", "Persian"),
        ("fi", "Finnish"),
        ("fr", "French"),
        ("ga", "Irish"),
        ("he", "Hebrew"),
        ("hi", "Hindi"),
        ("hu", "Hungarian"),
        ("id", "Indonesian"),
        ("it", "Italian"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("lt", "Lithuanian"),
        ("lv", "Latvian"),
        ("ms", "Malay"),
        ("nb", "Norwegian"),
        ("nl", "Dutch"),
        ("pl", "Polish"),
        ("pt", "Portuguese"),
        ("ro", "Romanian"),
        ("ru", "Russian"),
        ("sk", "Slovak"),
        ("sl", "Slovenian"),
        ("sq", "Albanian"),
        ("sr", "Serbian"),
        ("sv", "Swedish"),
        ("th", "Thai"),
        ("tl", "Tagalog"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("ur", "Urdu"),
        ("vi", "Vietnamese"),
        ("zh", "Chinese"),
    };

    public static string GetLanguageName(string code)
    {
        foreach (var (c, n) in SupportedLanguages)
            if (c.Equals(code, StringComparison.OrdinalIgnoreCase)) return n;
        return code;
    }

    // --- Install (Argos only — Google needs no install) ---

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsArgosReadyAsync(cancellationToken).ConfigureAwait(false))
            return;

        progress?.Report("Installing Argos Translate...");
        var result = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-m", "pip", "install", "--user", "--upgrade", "argostranslate"
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            var message = !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr.Trim() : result.StdOut.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Couldn't install Argos Translate." : message);
        }

        TryWriteArgosMarker();
        await IsArgosReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task UninstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Uninstalling Argos Translate...");
        await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-m", "pip", "uninstall", "-y", "argostranslate"
        }, cancellationToken).ConfigureAwait(false);
        TryDeleteArgosMarker();
        UpdateArgosProbeCache(false, "Not installed");
    }

    public static async Task<bool> IsArgosReadyAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetArgosCachedStatus(out var cachedReady, out _))
            return cachedReady;

        if (File.Exists(ArgosMarkerPath))
        {
            UpdateArgosProbeCache(true, "Installed");
            return true;
        }

        if (!await IsPythonLauncherAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            UpdateArgosProbeCache(false, "Python not found");
            return false;
        }

        var result = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-c", "import argostranslate; print('ok')"
        }, cancellationToken).ConfigureAwait(false);

        var ready = result.ExitCode == 0;
        UpdateArgosProbeCache(ready, ready ? "Installed" : "Not installed");
        return ready;
    }

    public static bool TryGetArgosCachedStatus(out bool isReady, out string status)
    {
        lock (ArgosProbeGate)
        {
            if (_cachedArgosReady.HasValue && DateTime.UtcNow - _cachedArgosCheckedUtc <= ArgosProbeCacheTtl)
            {
                isReady = _cachedArgosReady.Value;
                status = _cachedArgosStatus;
                return true;
            }
        }

        if (File.Exists(ArgosMarkerPath))
        {
            UpdateArgosProbeCache(true, "Installed");
            isReady = true;
            status = "Installed";
            return true;
        }

        isReady = false;
        status = "Checking install state...";
        return false;
    }

    // --- Translate ---

    public static async Task<string> TranslateAsync(string text, string fromCode, string toCode, TranslationModel model, CancellationToken cancellationToken = default)
    {
        if (model == TranslationModel.Google)
        {
            var apiKey = _googleApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Google Translate API key not set. Add it in Settings → OCR.");

            try
            {
                return await TranslateWithGoogleAsync(text, fromCode, toCode, apiKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("translation.google.translate", ex);
                throw;
            }
        }

        if (model == TranslationModel.OpenSourceLocal)
        {
            try
            {
                return await OpenSourceTranslationRuntimeService.TranslateAsync(text, fromCode, toCode, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("translation.local.translate", ex);
                throw;
            }
        }

        // Argos Translate
        var result = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-c", BuildArgosTranslateScript(), text, fromCode, toCode
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            var message = !string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdErr.Trim()
                : result.StdOut.Trim();

            if (message.Contains("No module named", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteArgosMarker();
                UpdateArgosProbeCache(false, "Not installed");
            }

            AppDiagnostics.LogWarning("translation.argos.translate", string.IsNullOrWhiteSpace(message) ? "Argos translation failed." : message);

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? "Translation failed."
                : message);
        }

        return result.StdOut.TrimEnd();
    }

    private static string? _googleApiKey;

    public static void SetGoogleApiKey(string? key)
    {
        _googleApiKey = key;
    }

    public static bool HasGoogleApiKey => !string.IsNullOrWhiteSpace(_googleApiKey);

    public static bool SupportsAutoDetect(TranslationModel model) =>
        model is TranslationModel.Google or TranslationModel.OpenSourceLocal;

    public static async Task<string?> GetConfigurationErrorAsync(string fromCode, TranslationModel model, CancellationToken cancellationToken = default)
    {
        if (model == TranslationModel.Google)
            return string.IsNullOrWhiteSpace(_googleApiKey)
                ? "Google Translate API key not set. Add it in Settings -> OCR."
                : null;

        if (model == TranslationModel.OpenSourceLocal)
        {
            if (OpenSourceTranslationRuntimeService.TryGetCachedStatus(out var localReady, out _))
                return localReady ? null : "Open-source local translation is not installed. Install it in Settings -> OCR.";

            return await OpenSourceTranslationRuntimeService.IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false)
                ? null
                : "Open-source local translation is not installed. Install it in Settings -> OCR.";
        }

        if (string.Equals(fromCode, "auto", StringComparison.OrdinalIgnoreCase))
            return "Argos Translate does not support auto-detect. Pick a source language or use Google Translate.";

        if (TryGetArgosCachedStatus(out var argosReady, out _))
            return argosReady ? null : "Argos Translate is not installed. Install it in Settings -> OCR.";

        return await IsArgosReadyAsync(cancellationToken).ConfigureAwait(false)
            ? null
            : "Argos Translate is not installed. Install it in Settings -> OCR.";
    }

    private static void UpdateArgosProbeCache(bool ready, string status)
    {
        lock (ArgosProbeGate)
        {
            _cachedArgosReady = ready;
            _cachedArgosStatus = status;
            _cachedArgosCheckedUtc = DateTime.UtcNow;
        }
    }

    public static async Task EnsureReadyAsync(string fromCode, TranslationModel model, CancellationToken cancellationToken = default)
    {
        var error = await GetConfigurationErrorAsync(fromCode, model, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException(error);
    }

    private static async Task<string> TranslateWithGoogleAsync(string text, string fromCode, string toCode, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"language/translate/v2?key={Uri.EscapeDataString(apiKey)}")
        {
            Content = new FormUrlEncodedContent(BuildGoogleForm(text, fromCode, toCode))
        };

        using var response = await GoogleHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractGoogleError(payload) ?? $"Google Translate request failed ({(int)response.StatusCode}).");

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement
            .GetProperty("data")
            .GetProperty("translations")[0]
            .GetProperty("translatedText")
            .GetString() ?? "";
    }

    private static async Task<bool> IsPythonLauncherAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await RunPythonAsync(new[] { PythonLauncherArg, "--version" }, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static async Task<PythonRunResult> RunPythonAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "py",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var errorMode = WindowsErrorModeScope.SuppressSystemDialogs();
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            AppDiagnostics.LogWarning("translation.python.start", "Could not start Python launcher.");
            return new PythonRunResult(-1, "", "Could not start Python launcher.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminateProcess(process);
            throw;
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new PythonRunResult(process.ExitCode, stdout, stderr);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            process.WaitForExit(5000);
        }
        catch
        {
        }
    }

    private static HttpClient CreateGoogleHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://translation.googleapis.com/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Yoink/1.0");
        return client;
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildGoogleForm(string text, string fromCode, string toCode)
    {
        yield return new("q", text);
        yield return new("target", toCode);
        if (!string.Equals(fromCode, "auto", StringComparison.OrdinalIgnoreCase))
            yield return new("source", fromCode);
        yield return new("format", "text");
    }

    private static string? ExtractGoogleError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message))
                    return message.GetString();
                return error.ToString();
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(payload) ? null : payload.Trim();
    }

    private static string BuildArgosTranslateScript() => """
import sys
import argostranslate.translate as tr

text = sys.argv[1]
from_code = sys.argv[2]
to_code = sys.argv[3]

# Check if language pack is installed, install if needed
installed = tr.get_installed_languages()
from_lang = next((l for l in installed if l.code == from_code), None)
to_lang = next((l for l in installed if l.code == to_code), None)

if not from_lang or not to_lang or not from_lang.get_translation(to_lang):
    import argostranslate.package as pkg
    pkg.update_package_index()
    available = pkg.get_available_packages()
    match = next((p for p in available if p.from_code == from_code and p.to_code == to_code), None)
    if not match:
        raise RuntimeError("No Argos language pack is available for this language pair.")
    download_path = match.download()
    pkg.install_from_path(download_path)
    installed = tr.get_installed_languages()
    from_lang = next((l for l in installed if l.code == from_code), None)
    to_lang = next((l for l in installed if l.code == to_code), None)
    if not from_lang or not to_lang or not from_lang.get_translation(to_lang):
        raise RuntimeError("Argos language pack install completed, but the translation pair is still unavailable.")

translated = tr.translate(text, from_code, to_code)
print(translated)
""";

    private static void TryWriteArgosMarker()
    {
        try
        {
            Directory.CreateDirectory(ArgosStateDir);
            File.WriteAllText(ArgosMarkerPath, "installed");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("translation.argos.marker-write", ex.Message, ex);
        }
    }

    private static void TryDeleteArgosMarker()
    {
        try
        {
            if (File.Exists(ArgosMarkerPath))
                File.Delete(ArgosMarkerPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("translation.argos.marker-delete", ex.Message, ex);
        }
    }
}
