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

    [Fact]
    public void RoundedChromeHasWindows10FallbackInsteadOfWindows11OnlyDwmCorners()
    {
        var chrome = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OddSnapWindowChrome.cs"));
        var gdi = File.ReadAllText(RepoPath("src", "OddSnap", "Native", "Gdi32.cs"));
        var user32 = File.ReadAllText(RepoPath("src", "OddSnap", "Native", "User32.cs"));

        Assert.Contains("Dwm.TrySetWindowCornerPreference", chrome);
        Assert.Contains("OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)", chrome);
        Assert.Contains("SetRoundedWindowRegion", chrome);
        Assert.Contains("CreateRoundRectRgn", gdi);
        Assert.Contains("SetWindowRgn", user32);
        Assert.Contains("Gdi32.DeleteObject(region)", chrome);
    }

    [Fact]
    public void PopupAndMenuSurfacesApplyExplicitRoundedRegions()
    {
        var menu = File.ReadAllText(RepoPath("src", "OddSnap", "Helpers", "WindowsMenuRenderer.cs"));
        var preview = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));
        var toast = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));
        var setup = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SetupWizard.xaml.cs"));

        Assert.Contains("ApplyRoundedRegion(strip)", menu);
        Assert.Contains("menu.SizeChanged += (_, _) => ApplyRoundedRegion(menu);", menu);
        Assert.Contains("menu.Region = new Region(path);", menu);
        Assert.Contains("OddSnapWindowChrome.ApplyRoundedCorners(this, 18);", preview);
        Assert.Contains("OddSnapWindowChrome.ApplyRoundedCorners(this, 12);", toast);
        Assert.Contains("OddSnapWindowChrome.ApplyRoundedCorners(this, 12);", setup);
    }

    [Fact]
    public void CaptureMenuUsesMeasuredWidthForItemsAndPlacement()
    {
        var menu = File.ReadAllText(RepoPath("src", "OddSnap", "Helpers", "WindowsMenuRenderer.cs"));
        var moreTools = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "RegionOverlayForm.MoreToolsMenu.cs"));

        Assert.Contains("public const int DefaultWidth = 340;", menu);
        Assert.Contains("public static int NormalizeItemWidths", menu);
        Assert.Contains("text + shortcut + (menu.ShowImageMargin ? 124 : 76)", menu);
        Assert.Contains("menu.Width = width;", menu);
        Assert.Contains("return width;", menu);
        Assert.Contains("public static void SetMenuWidth", menu);
        Assert.Contains("_moreToolsMenu?.Width", moreTools);
        Assert.Contains("GetToolbarAnchorClientBounds()", moreTools);
        Assert.Contains("clampBounds.Right - width - 8", moreTools);
        Assert.Contains("WindowsMenuRenderer.SetMenuWidth(_moreToolsMenu, width);", moreTools);
        Assert.DoesNotContain("const int width = WindowsMenuRenderer.DefaultWidth;", moreTools);
        Assert.DoesNotContain("ClientSize.Width - width - 8", moreTools);
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
