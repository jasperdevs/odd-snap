using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public class HistoryUploadMenuTests
{
    [Fact]
    public async Task UploadAction_RefreshesMenuAfterSuccess()
    {
        var refreshCount = 0;

        await SettingsWindow.RunHistoryUploadActionAsync(
            () => Task.CompletedTask,
            () => refreshCount++);

        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task UploadAction_RefreshesMenuAfterFailure()
    {
        var refreshCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SettingsWindow.RunHistoryUploadActionAsync(
                () => Task.FromException(new InvalidOperationException("upload failed")),
                () => refreshCount++));

        Assert.Equal(1, refreshCount);
    }
}
