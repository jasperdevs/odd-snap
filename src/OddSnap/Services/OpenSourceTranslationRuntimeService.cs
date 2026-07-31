using System.IO;

namespace OddSnap.Services;

public static class OpenSourceTranslationRuntimeService
{
    private const string RuntimeVersion = "m2m100-418m-ct2-v2";
    private const long MinModelFileBytes = 1L * 1024 * 1024;
    private static readonly string[] RuntimePackages =
    [
        "ctranslate2==4.7.1",
        "transformers==5.5.4",
        "sentencepiece==0.2.1",
        "langid==1.1.6",
        "huggingface_hub==1.10.2",
        "numpy==2.4.4",
        "torch==2.11.0"
    ];
    internal static IReadOnlyList<string> RequiredRuntimePackages => RuntimePackages;

    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap", "translate-local");
    private static readonly string ModelDir = Path.Combine(RootDir, "m2m100_ct2");
    private static readonly string TokenizerDir = Path.Combine(RootDir, "tokenizer");
    private static readonly string RuntimeVersionPath = Path.Combine(RootDir, "runtime.version");

    private static readonly object ProbeGate = new();
    private static bool? _cachedReady;
    private static string _cachedStatus = "Checking install state...";
    private static DateTime _cachedCheckedUtc;

    public static string RootDirectory => RootDir;

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            return;

        progress?.Report("Installing local translation dependencies...");
        var install = await RunPythonAsync(
            new[] { PythonRuntimeEnvironment.DefaultLauncherArgument, "-m", "pip", "install", "--user", "--disable-pip-version-check" }
                .Concat(RuntimePackages),
            cancellationToken).ConfigureAwait(false);

        if (install.ExitCode != 0)
        {
            var message = !string.IsNullOrWhiteSpace(install.StdErr) ? install.StdErr.Trim() : install.StdOut.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Couldn't install the local translation runtime." : message);
        }

        progress?.Report("Preparing local translation model...");
        await PrepareRuntimeAsync(progress, cancellationToken).ConfigureAwait(false);
        UpdateProbeCache(true, "Installed");
    }

    public static Task UninstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Removing local translation runtime...");
        try
        {
            if (Directory.Exists(RootDir))
                Directory.Delete(RootDir, recursive: true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("translation.local.uninstall", ex);
            throw new InvalidOperationException($"Couldn't remove the local translation runtime: {ex.Message}", ex);
        }

        UpdateProbeCache(false, "Not installed");
        return Task.CompletedTask;
    }

    public static async Task<bool> IsRuntimeReadyAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedStatus(out var cachedReady, out _))
            return cachedReady;

        if (!HasRuntimeFiles())
        {
            UpdateProbeCache(false, "Not installed");
            return false;
        }

        var result = await RunPythonAsync(new[]
        {
            PythonRuntimeEnvironment.DefaultLauncherArgument,
            "-c",
            "import ctranslate2, transformers, sentencepiece, langid; print('ok')"
        }, cancellationToken).ConfigureAwait(false);

        var ready = result.ExitCode == 0;
        UpdateProbeCache(ready, ready ? "Installed" : "Not installed");
        return ready;
    }

    public static bool TryGetCachedStatus(out bool isReady, out string status)
    {
        if (HasRuntimeFiles())
        {
            UpdateProbeCache(true, "Installed");
            isReady = true;
            status = "Installed";
            return true;
        }

        lock (ProbeGate)
        {
            if (_cachedReady.HasValue && DateTime.UtcNow - _cachedCheckedUtc <= ProbeCacheTtl)
            {
                isReady = _cachedReady.Value;
                status = _cachedStatus;
                return true;
            }
        }

        isReady = false;
        status = "Checking install state...";
        return false;
    }

    public static async Task<string> TranslateAsync(string text, string fromCode, string toCode, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(null, cancellationToken).ConfigureAwait(false);

        var result = await RunPythonAsync(new[]
        {
            PythonRuntimeEnvironment.DefaultLauncherArgument,
            "-c",
            BuildTranslateScript(),
            fromCode,
            toCode,
            ModelDir,
            TokenizerDir
        }, cancellationToken, standardInput: text).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            AppDiagnostics.LogWarning("translation.local.translate", GetPythonFailureMessage(result, "Local translation failed."));
            throw new InvalidOperationException(GetPythonFailureMessage(result, "Local translation failed."));
        }

        return result.StdOut.TrimEnd();
    }

    private static Task<ProcessRunResult> RunPythonAsync(IEnumerable<string> arguments, CancellationToken cancellationToken, string? standardInput = null)
        => PythonRuntimeEnvironment.RunUtf8LauncherAsync(
            arguments,
            cancellationToken,
            "translation.local.python-start",
            standardInput);

    private static string GetPythonFailureMessage(ProcessRunResult result, string fallback)
    {
        var message = ProcessRunner.GetFailureMessage(result, fallback);
        return string.IsNullOrWhiteSpace(message) ? fallback : NormalizePythonError(message);
    }

    private static string NormalizePythonError(string message)
    {
        var text = message.Replace("\r", "\n", StringComparison.Ordinal);
        if (text.Contains("output directory", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("exists", StringComparison.OrdinalIgnoreCase))
        {
            return "Existing local translation files were incomplete. Retry install and OddSnap will rebuild them.";
        }

        if (text.Contains("No module named", StringComparison.OrdinalIgnoreCase))
            return "The local translation Python packages are missing or incomplete.";

        if (text.Contains("Traceback", StringComparison.OrdinalIgnoreCase))
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var lastMeaningful = lines.LastOrDefault(line =>
                !line.StartsWith("File ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("^", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(lastMeaningful))
                return lastMeaningful;
        }

        while (text.Contains('\n'))
            text = text.Replace("\n", " ", StringComparison.Ordinal);
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length <= 180 ? text : text[..177] + "...";
    }

    private static async Task PrepareRuntimeAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var prepare = await RunPrepareScriptAsync(cancellationToken).ConfigureAwait(false);
        if (prepare.ExitCode == 0)
            return;

        var normalized = NormalizePythonError(!string.IsNullOrWhiteSpace(prepare.StdErr) ? prepare.StdErr : prepare.StdOut);
        if (!normalized.Contains("incomplete", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("output directory", StringComparison.OrdinalIgnoreCase))
        {
            AppDiagnostics.LogWarning("translation.local.prepare", normalized);
            throw new InvalidOperationException(GetPythonFailureMessage(prepare, "Couldn't prepare the local translation model."));
        }

        progress?.Report("Repairing local translation files...");
        ForceCleanRuntimeDirectories();
        prepare = await RunPrepareScriptAsync(cancellationToken).ConfigureAwait(false);
        if (prepare.ExitCode != 0)
        {
            AppDiagnostics.LogWarning("translation.local.prepare-retry", GetPythonFailureMessage(prepare, "Couldn't prepare the local translation model."));
            throw new InvalidOperationException(GetPythonFailureMessage(prepare, "Couldn't prepare the local translation model."));
        }
    }

    private static async Task<ProcessRunResult> RunPrepareScriptAsync(CancellationToken cancellationToken)
    {
        return await RunPythonAsync(new[]
        {
            PythonRuntimeEnvironment.DefaultLauncherArgument,
            "-c",
            BuildInstallScript(),
            ModelDir,
            TokenizerDir,
            RuntimeVersionPath,
            RuntimeVersion
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ForceCleanRuntimeDirectories()
    {
        TryDeletePath(ModelDir);
        TryDeletePath(TokenizerDir);
        TryDeletePath(RuntimeVersionPath);
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("translation.local.cleanup", ex.Message, ex);
        }
    }

    private static bool HasRuntimeFiles()
    {
        try
        {
            var modelPath = Path.Combine(ModelDir, "model.bin");
            var tokenizerConfigPath = Path.Combine(TokenizerDir, "tokenizer_config.json");
            return File.Exists(modelPath) &&
                   new FileInfo(modelPath).Length >= MinModelFileBytes &&
                   File.Exists(tokenizerConfigPath) &&
                   new FileInfo(tokenizerConfigPath).Length > 0 &&
                   File.Exists(RuntimeVersionPath) &&
                   string.Equals(File.ReadAllText(RuntimeVersionPath).Trim(), RuntimeVersion, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "translation.local.runtime-scan",
                $"Failed to inspect local translation runtime files: {ex.Message}",
                ex);
            return false;
        }
    }

    private static void UpdateProbeCache(bool ready, string status)
    {
        lock (ProbeGate)
        {
            _cachedReady = ready;
            _cachedStatus = status;
            _cachedCheckedUtc = DateTime.UtcNow;
        }
    }

    private static string BuildInstallScript() => """
import os
import shutil
import sys
from pathlib import Path

from ctranslate2.converters import TransformersConverter
from transformers import AutoTokenizer

model_dir = sys.argv[1]
tokenizer_dir = sys.argv[2]
runtime_version_path = sys.argv[3]
runtime_version = sys.argv[4]
model_bin = os.path.join(model_dir, "model.bin")
min_model_bytes = 1024 * 1024

if not os.path.exists(model_bin) or os.path.getsize(model_bin) < min_model_bytes:
    if os.path.isdir(model_dir):
        shutil.rmtree(model_dir)
    Path(model_dir).parent.mkdir(parents=True, exist_ok=True)
    converter = TransformersConverter("facebook/m2m100_418M")
    converter.convert(model_dir, quantization="int8")
else:
    Path(model_dir).mkdir(parents=True, exist_ok=True)

if os.path.isdir(tokenizer_dir):
    shutil.rmtree(tokenizer_dir)
Path(tokenizer_dir).mkdir(parents=True, exist_ok=True)
tokenizer = AutoTokenizer.from_pretrained("facebook/m2m100_418M", use_fast=False)
tokenizer.save_pretrained(tokenizer_dir)

with open(runtime_version_path, "w", encoding="utf-8") as version_file:
    version_file.write(runtime_version)

print("ok")
""";

    private static string BuildTranslateScript() => """
import sys

import ctranslate2
import langid
from transformers import AutoTokenizer

from_code = sys.argv[1].strip().lower()
to_code = sys.argv[2].strip().lower()
model_dir = sys.argv[3]
tokenizer_dir = sys.argv[4]
text = sys.stdin.read()

aliases = {
    "nb": "no",
    "he": "he",
    "zh": "zh",
}

tokenizer = AutoTokenizer.from_pretrained(tokenizer_dir, use_fast=False)
available = set(tokenizer.lang_code_to_token.keys())

def resolve_language(code):
    if code == "auto":
        code = langid.classify(text)[0].lower()
    code = aliases.get(code, code)
    if code not in available:
        raise SystemExit(f"Language '{code}' is not supported by the open-source local model.")
    return code

source_language = resolve_language(from_code)
target_language = resolve_language(to_code)

tokenizer.src_lang = source_language
source_tokens = tokenizer.convert_ids_to_tokens(tokenizer.encode(text))
target_prefix = [tokenizer.lang_code_to_token[target_language]]

translator = ctranslate2.Translator(model_dir, device="cpu", compute_type="int8")
result = translator.translate_batch([source_tokens], target_prefix=[target_prefix], beam_size=4, max_batch_size=1)
target_tokens = result[0].hypotheses[0]
if target_tokens and target_tokens[0] == tokenizer.lang_code_to_token[target_language]:
    target_tokens = target_tokens[1:]

translated = tokenizer.decode(tokenizer.convert_tokens_to_ids(target_tokens), skip_special_tokens=True)
print(translated)
""";
}
