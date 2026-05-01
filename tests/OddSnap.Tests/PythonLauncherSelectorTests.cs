using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class PythonLauncherSelectorTests
{
    [Fact]
    public void ParseLauncherListOutput_ParsesInstalledVersions()
    {
        const string output = """
             -V:3.13 *        C:\Users\bunny\AppData\Local\Microsoft\WindowsApps\PythonSoftwareFoundation.Python.3.13_qbz5n2kfra8p0\python.exe
             -V:3.10          C:\Users\bunny\AppData\Local\Programs\Python\Python310\python.exe
            """;

        var entries = PythonLauncherSelector.ParseLauncherListOutput(output);

        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal("-3.13", entry.LauncherArgument);
                Assert.Equal("3.13", entry.Version);
                Assert.True(entry.IsDefault);
            },
            entry =>
            {
                Assert.Equal("-3.10", entry.LauncherArgument);
                Assert.Equal("3.10", entry.Version);
                Assert.False(entry.IsDefault);
            });
    }

    [Fact]
    public void SelectOnnxRuntimeLauncherArgument_PrefersHighestCompatibleVersion()
    {
        var entries = PythonLauncherSelector.ParseLauncherListOutput("""
             -V:3.13 *        C:\Python313\python.exe
             -V:3.10          C:\Python310\python.exe
             -V:3.12          C:\Python312\python.exe
            """);

        var selected = PythonLauncherSelector.SelectOnnxRuntimeLauncherArgument(entries);

        Assert.Equal("-3.13", selected);
    }

    [Fact]
    public void SelectOnnxRuntimeLauncherArgument_ReturnsNullWhenOnlyUnsupportedVersionsExist()
    {
        var entries = PythonLauncherSelector.ParseLauncherListOutput("""
             -V:3.10 *        C:\Python310\python.exe
             -V:3.9           C:\Python39\python.exe
            """);

        var selected = PythonLauncherSelector.SelectOnnxRuntimeLauncherArgument(entries);

        Assert.Null(selected);
    }

    [Theory]
    [InlineData("Python 3.10.11", false)]
    [InlineData("Python 3.11.9", true)]
    [InlineData("Python 3.12.4", true)]
    [InlineData("Python 3.13.1", true)]
    [InlineData("Python 3.14.0", true)]
    [InlineData("Python 3.9.13", false)]
    public void IsSupportedOnnxRuntimeVersion_MatchesExpectedVersions(string versionText, bool expected)
    {
        var actual = PythonLauncherSelector.IsSupportedOnnxRuntimeVersion(versionText);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildOnnxRuntimeMissingVersionMessage_IncludesDiscoveredVersions()
    {
        var entries = PythonLauncherSelector.ParseLauncherListOutput("""
             -V:3.10 *        C:\Python310\python.exe
             -V:3.9           C:\Python39\python.exe
            """);

        var message = PythonLauncherSelector.BuildOnnxRuntimeMissingVersionMessage(entries);

        Assert.Contains("3.11, 3.12, 3.13, or 3.14", message, StringComparison.Ordinal);
        Assert.Contains("3.10", message, StringComparison.Ordinal);
        Assert.Contains("3.9", message, StringComparison.Ordinal);
    }
}
