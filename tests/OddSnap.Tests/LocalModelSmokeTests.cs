using System.Drawing;
using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class LocalModelSmokeTests
{
    private const string LiveSmokeEnvVar = "ODDSNAP_RUN_LIVE_MODEL_SMOKE";

    [Fact]
    [Trait("Category", "LiveModelSmoke")]
    public async Task LiveSmoke_AllLocalStickerAndUpscaleModelsCanBootstrapDownloadAndRun()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveSmokeEnvVar), "1", StringComparison.Ordinal))
            return;

        ResetRuntimeCaches();

        foreach (StickerExecutionProvider provider in Enum.GetValues<StickerExecutionProvider>())
        {
            await RembgRuntimeService.EnsureInstalledAsync(provider);

            foreach (LocalStickerEngine engine in Enum.GetValues<LocalStickerEngine>())
            {
                var install = await LocalStickerEngineService.DownloadModelAsync(engine, provider);
                Assert.True(install.Success, $"Sticker install failed for {provider}/{engine}: {install.Message}");
                Assert.True(File.Exists(RembgRuntimeService.GetModelPath(engine)), $"Sticker model missing for {provider}/{engine}");

                using var input = CreateTinyBitmap();
                using var output = await RembgRuntimeService.RemoveBackgroundAsync(input, engine, provider);
                Assert.NotNull(output);
                Assert.True(output.Width > 0 && output.Height > 0, $"Sticker output invalid for {provider}/{engine}");
            }
        }

        foreach (UpscaleExecutionProvider provider in Enum.GetValues<UpscaleExecutionProvider>())
        {
            await UpscaleRuntimeService.EnsureInstalledAsync(provider);

            foreach (LocalUpscaleEngine engine in Enum.GetValues<LocalUpscaleEngine>())
            {
                var install = await LocalUpscaleEngineService.DownloadModelAsync(engine, provider);
                Assert.True(install.Success, $"Upscale install failed for {provider}/{engine}: {install.Message}");
                Assert.True(File.Exists(UpscaleRuntimeService.GetModelPath(engine)), $"Upscale model missing for {provider}/{engine}");

                using var input = CreateTinyBitmap();
                using var output = await Task.Run(() => LocalUpscaleEngineService.Process(input, engine, provider, 2));
                Assert.NotNull(output);
                Assert.True(output.Width >= input.Width * 2 && output.Height >= input.Height * 2, $"Upscale output invalid for {provider}/{engine}");
            }
        }
    }

    private static Bitmap CreateTinyBitmap()
    {
        var bitmap = new Bitmap(8, 8);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? Color.White : Color.Black);
        }

        return bitmap;
    }

    private static void ResetRuntimeCaches()
    {
        TryDeleteDirectory(RembgRuntimeService.RootDirectory);
        TryDeleteDirectory(UpscaleRuntimeService.RootDirectory);
        TryDeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".u2net"));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
