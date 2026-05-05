using Xunit;

namespace OddSnap.Tests;

public sealed class UpscaleResultWindowPolishTests
{
    [Fact]
    public void UpscalePreviewFailuresShowRecoveryCopy()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "UpscaleResultWindow.xaml.cs"));

        var upscaleBlock = GetMethodBlock(source, "private async void UpscaleBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("ShowUpscalePreviewFailed(result.Error ?? \"Upscale did not return an image.\");", upscaleBlock);
        Assert.Contains("AppDiagnostics.LogError(\"upscale.window\", ex);", upscaleBlock);
        Assert.Contains("ShowUpscalePreviewFailed(ex.Message);", upscaleBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upscale failed\", result.Error);", upscaleBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upscale failed\", ex.Message);", upscaleBlock);

        var failureBlock = GetMethodBlock(source, "private void ShowUpscalePreviewFailed(string details)");
        Assert.Contains("StatusText.Text = \"Upscale failed. Try again, or check Settings -> Upscale.\";", failureBlock);
        Assert.Contains("OddSnap could not generate the upscale preview. Try again, or check Settings -> Upscale.", failureBlock);
        Assert.Contains("{details}", failureBlock);
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
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
}
