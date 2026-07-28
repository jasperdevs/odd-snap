using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public class CaptureSourceAppTests
{
    [Theory]
    [InlineData("Discord", "Discord")]
    [InlineData("Task Manager", "Task-Manager")]
    [InlineData("Visual Studio: Code", "Visual-Studio-Code")]
    [InlineData("  Notepad  ", "Notepad")]
    [InlineData("Foo/Bar\\Baz", "FooBarBaz")]
    public void SanitizeForFileName_ProducesFileSafeNames(string input, string expected)
    {
        Assert.Equal(expected, CaptureSourceApp.SanitizeForFileName(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    public void SanitizeForFileName_ReturnsNullWhenNothingUsableRemains(string? input)
    {
        Assert.Null(CaptureSourceApp.SanitizeForFileName(input));
    }
}
