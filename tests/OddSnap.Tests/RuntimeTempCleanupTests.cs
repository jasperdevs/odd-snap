using Xunit;

namespace OddSnap.Tests;

public sealed class RuntimeTempCleanupTests
{
    [Fact]
    public void LocalImageRuntimeTempCleanupFailuresAreLogged()
    {
        var rembgSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "RembgRuntimeService.cs"));
        var upscaleSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UpscaleRuntimeService.cs"));
        var stickerSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "StickerService.cs"));
        var tessdataSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "TessdataService.cs"));

        var rembgCleanupBlock = GetMethodBlock(rembgSource, "private static void TryDeleteRuntimeTempFile(string path, string context)");
        Assert.Contains("\"stickers.runtime.temp-cleanup\"", rembgCleanupBlock);
        Assert.Contains("Failed to delete {context} temporary file", rembgCleanupBlock);
        Assert.Contains("TryDeleteRuntimeTempFile(tempInput, \"rembg input\");", rembgSource);
        Assert.Contains("TryDeleteRuntimeTempFile(tempOutput, \"rembg output\");", rembgSource);
        Assert.DoesNotContain("try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }", rembgSource);
        Assert.DoesNotContain("try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }", rembgSource);

        var upscaleCleanupBlock = GetMethodBlock(upscaleSource, "private static void TryDeleteRuntimeTempFile(string path, string context)");
        Assert.Contains("\"upscale.runtime.temp-cleanup\"", upscaleCleanupBlock);
        Assert.Contains("Failed to delete {context} temporary file", upscaleCleanupBlock);
        Assert.Contains("TryDeleteRuntimeTempFile(tempInput, \"upscale input\");", upscaleSource);
        Assert.Contains("TryDeleteRuntimeTempFile(tempOutput, \"upscale output\");", upscaleSource);
        Assert.Contains("TryDeleteRuntimeTempFile(tempPath, \"model download\");", upscaleSource);
        Assert.DoesNotContain("try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }", upscaleSource);
        Assert.DoesNotContain("try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }", upscaleSource);
        Assert.DoesNotContain("try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }", upscaleSource);

        var stickerCleanupBlock = GetMethodBlock(stickerSource, "private static void TryDeleteStickerTempFile(string path, string context)");
        Assert.Contains("\"stickers.api.temp-cleanup\"", stickerCleanupBlock);
        Assert.Contains("Failed to delete {context} temporary file", stickerCleanupBlock);
        Assert.Contains("TryDeleteStickerTempFile(temp, \"Remove.bg upload\");", stickerSource);
        Assert.Contains("TryDeleteStickerTempFile(temp, \"Photoroom upload\");", stickerSource);
        Assert.DoesNotContain("try { File.Delete(temp); } catch { }", stickerSource);

        var tessdataCleanupBlock = GetMethodBlock(tessdataSource, "private static void TryDeleteTessdataTempFile(string path, string context)");
        Assert.Contains("\"ocr.tessdata.temp-cleanup\"", tessdataCleanupBlock);
        Assert.Contains("Failed to delete {context} temporary file", tessdataCleanupBlock);
        Assert.Contains("TryDeleteTessdataTempFile(tmp, \"stale OCR language download\");", tessdataSource);
        Assert.Contains("TryDeleteTessdataTempFile(tempPath, \"failed OCR language download\");", tessdataSource);
        Assert.DoesNotContain("try { File.Delete(tmp); } catch { }", tessdataSource);
        Assert.DoesNotContain("try { File.Delete(tempPath); } catch { }", tessdataSource);

        var tessdataSystemLanguageBlock = GetMethodBlock(tessdataSource, "public static async Task EnsureSystemLanguagesAsync(IProgress<string>? progress = null)");
        Assert.Contains("\"ocr.tessdata.system-language\"", tessdataSystemLanguageBlock);
        Assert.Contains("Failed to download detected OCR language", tessdataSystemLanguageBlock);
        Assert.DoesNotContain("catch { /* non-fatal", tessdataSystemLanguageBlock);

        var tessdataRemoveLanguageBlock = GetMethodBlock(tessdataSource, "public static bool RemoveLanguage(string code)");
        Assert.Contains("\"ocr.tessdata.language-remove\"", tessdataRemoveLanguageBlock);
        Assert.Contains("Failed to remove OCR language", tessdataRemoveLanguageBlock);
        Assert.Contains("return false;", tessdataRemoveLanguageBlock);
        Assert.DoesNotContain("catch { return false; }", tessdataRemoveLanguageBlock);
    }

    [Fact]
    public void LocalRuntimeModelCleanupFailuresAreLogged()
    {
        var rembgSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "RembgRuntimeService.cs"));
        var upscaleSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UpscaleRuntimeService.cs"));

        var rembgRemoveModelBlock = GetMethodBlock(rembgSource, "public static bool RemoveCachedModel(LocalStickerEngine engine)");
        Assert.Contains("\"stickers.runtime.model-cleanup\"", rembgRemoveModelBlock);
        Assert.Contains("Failed to delete sticker model", rembgRemoveModelBlock);

        var rembgRemoveAllBlock = GetMethodBlock(rembgSource, "public static bool RemoveAllCachedModels()");
        Assert.Contains("\"stickers.runtime.model-cleanup\"", rembgRemoveAllBlock);
        Assert.Contains("Failed to remove cached sticker models", rembgRemoveAllBlock);

        var rembgEmptyDirBlock = GetMethodBlock(rembgSource, "private static void TryDeleteDirectoryIfEmpty(string path)");
        Assert.Contains("\"stickers.runtime.model-cleanup\"", rembgEmptyDirBlock);
        Assert.Contains("Failed to delete empty sticker model directory", rembgEmptyDirBlock);
        Assert.DoesNotContain("catch\r\n        {\r\n        }", rembgEmptyDirBlock);

        var upscaleRemoveModelBlock = GetMethodBlock(upscaleSource, "public static bool RemoveCachedModel(LocalUpscaleEngine engine)");
        Assert.Contains("\"upscale.runtime.model-cleanup\"", upscaleRemoveModelBlock);
        Assert.Contains("Failed to delete upscale model", upscaleRemoveModelBlock);

        var upscaleRemoveAllBlock = GetMethodBlock(upscaleSource, "public static bool RemoveAllCachedModels()");
        Assert.Contains("\"upscale.runtime.model-cleanup\"", upscaleRemoveAllBlock);
        Assert.Contains("Failed to remove cached upscale models", upscaleRemoveAllBlock);
    }

    [Fact]
    public void LocalRuntimeRemovalFailuresAreLogged()
    {
        var rembgSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "RembgRuntimeService.cs"));
        var upscaleSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UpscaleRuntimeService.cs"));
        var pythonRuntimeSource = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "PythonRuntimeEnvironment.cs"));

        var rembgRemoveRuntimeBlock = GetMethodBlock(rembgSource, "public static bool RemoveRuntime(StickerExecutionProvider provider)");
        Assert.Contains("\"stickers.runtime.remove-cleanup\"", rembgRemoveRuntimeBlock);
        Assert.Contains("Failed to remove {provider} sticker runtime", rembgRemoveRuntimeBlock);
        Assert.Contains("if (!PythonRuntimeEnvironment.TryDeleteDirectory(", rembgRemoveRuntimeBlock);
        Assert.Contains("return false;", rembgRemoveRuntimeBlock);
        Assert.DoesNotContain("catch\r\n        {\r\n            return false;\r\n        }", rembgRemoveRuntimeBlock);

        var upscaleRemoveRuntimeBlock = GetMethodBlock(upscaleSource, "public static bool RemoveRuntime(UpscaleExecutionProvider provider)");
        Assert.Contains("\"upscale.runtime.remove-cleanup\"", upscaleRemoveRuntimeBlock);
        Assert.Contains("Failed to remove {provider} upscale runtime", upscaleRemoveRuntimeBlock);
        Assert.Contains("if (!PythonRuntimeEnvironment.TryDeleteDirectory(", upscaleRemoveRuntimeBlock);
        Assert.Contains("return false;", upscaleRemoveRuntimeBlock);
        Assert.DoesNotContain("catch\r\n        {\r\n            return false;\r\n        }", upscaleRemoveRuntimeBlock);

        var pythonRuntimeDeleteBlock = GetMethodBlock(pythonRuntimeSource, "public static bool TryDeleteDirectory(string path, string diagnosticCategory, string context)");
        Assert.Contains("AppDiagnostics.LogWarning(", pythonRuntimeDeleteBlock);
        Assert.Contains("return false;", pythonRuntimeDeleteBlock);
        Assert.Contains("Path.GetFileName(path)", pythonRuntimeDeleteBlock);
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source[braceStart..(i + 1)];
        }

        throw new InvalidOperationException($"Could not parse method block: {signature}");
    }
}
