using OddSnap.Helpers;
using Xunit;

namespace OddSnap.Tests;

public class CaptureSavePathTests : IDisposable
{
    private readonly string _tempDir;

    public CaptureSavePathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OddSnapTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void BuildPath_WithoutMonthlyFolder_JoinsRootAndFile()
    {
        Assert.Equal(
            Path.Combine(@"C:\Shots", "a.png"),
            CaptureSavePath.BuildPath(@"C:\Shots", "a.png", useMonthlyFolder: false));
    }

    [Fact]
    public void BuildPath_WithMonthlyFolder_UsesYearDashMonth()
    {
        var capturedAt = new DateTime(2026, 7, 4);
        Assert.Equal(
            Path.Combine(@"C:\Shots", "2026-07", "a.png"),
            CaptureSavePath.BuildPath(@"C:\Shots", "a.png", useMonthlyFolder: true, capturedAt));
    }

    [Fact]
    public void GetMonthDirectory_ZeroPadsMonth()
    {
        Assert.Equal(
            Path.Combine(@"C:\Shots", "2025-01"),
            CaptureSavePath.GetMonthDirectory(@"C:\Shots", new DateTime(2025, 1, 31)));
    }

    [Fact]
    public void BuildMonthlyPath_MatchesBuildPathWithMonthlyFlag()
    {
        var capturedAt = new DateTime(2026, 12, 25);
        Assert.Equal(
            CaptureSavePath.BuildPath(@"C:\Shots", "a.png", useMonthlyFolder: true, capturedAt),
            CaptureSavePath.BuildMonthlyPath(@"C:\Shots", "a.png", capturedAt));
    }

    [Fact]
    public void GetAvailablePath_NonExistingFile_ReturnsSamePath()
    {
        var path = Path.Combine(_tempDir, "shot.png");
        Assert.Equal(path, CaptureSavePath.GetAvailablePath(path));
    }

    [Fact]
    public void GetAvailablePath_ExistingFile_AppendsIndexTwo()
    {
        var path = Path.Combine(_tempDir, "shot.png");
        File.WriteAllText(path, "x");
        Assert.Equal(Path.Combine(_tempDir, "shot (2).png"), CaptureSavePath.GetAvailablePath(path));
    }

    [Fact]
    public void GetAvailablePath_SkipsAllTakenIndexes()
    {
        var path = Path.Combine(_tempDir, "shot.png");
        File.WriteAllText(path, "x");
        File.WriteAllText(Path.Combine(_tempDir, "shot (2).png"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "shot (3).png"), "x");
        Assert.Equal(Path.Combine(_tempDir, "shot (4).png"), CaptureSavePath.GetAvailablePath(path));
    }

    [Fact]
    public void BuildAvailablePath_CombinesMonthlyFolderAndCollisionAvoidance()
    {
        var capturedAt = new DateTime(2026, 7, 4);
        var monthDir = Path.Combine(_tempDir, "2026-07");
        Directory.CreateDirectory(monthDir);
        File.WriteAllText(Path.Combine(monthDir, "shot.png"), "x");

        var result = CaptureSavePath.BuildAvailablePath(_tempDir, "shot.png", useMonthlyFolder: true, capturedAt);
        Assert.Equal(Path.Combine(monthDir, "shot (2).png"), result);
    }
}
