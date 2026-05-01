using System.Xml.Linq;
using Xunit;

namespace OddSnap.Tests;

public sealed class WindowsCompatibilityTests
{
    [Fact]
    public void ShippingAppTargetsWindows10FloorAndSupportedArchitectures()
    {
        var project = XDocument.Load(RepoPath("src", "OddSnap", "OddSnap.csproj"));
        var properties = project.Descendants("PropertyGroup").Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);

        Assert.Equal("net9.0-windows10.0.19041.0", properties["TargetFramework"]);
        Assert.Contains("win-x64", properties["RuntimeIdentifiers"]);
        Assert.Contains("win-x86", properties["RuntimeIdentifiers"]);
        Assert.Contains("win-arm64", properties["RuntimeIdentifiers"]);
        Assert.Equal("PerMonitorV2", properties["ApplicationHighDpiMode"]);
    }

    [Fact]
    public void AppManifestDeclaresWindows10AndWindows11CompatibilityGuid()
    {
        var manifest = File.ReadAllText(RepoPath("src", "OddSnap", "app.manifest"));

        Assert.Contains("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a", manifest);
    }

    [Fact]
    public void BuildWorkflowCoversEveryShippingRid()
    {
        var build = File.ReadAllText(RepoPath(".github", "workflows", "build.yml"));
        var release = File.ReadAllText(RepoPath(".github", "workflows", "release.yml"));

        foreach (var rid in new[] { "win-x64", "win-x86", "win-arm64" })
        {
            Assert.Contains($"rid: {rid}", build);
            Assert.Contains($"rid: {rid}", release);
        }

        Assert.Contains("App-OddSnap-Setup-x64.exe", release);
        Assert.Contains("App-OddSnap-Setup-x86.exe", release);
        Assert.Contains("App-OddSnap-Setup-arm64.exe", release);
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
