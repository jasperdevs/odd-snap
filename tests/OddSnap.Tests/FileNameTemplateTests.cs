using System.Text.RegularExpressions;
using Xunit;
using OddSnap.Helpers;

namespace OddSnap.Tests;

public sealed class FileNameTemplateTests
{
    [Theory]
    [InlineData("rand")]
    [InlineData("(rand)")]
    [InlineData("[rand]")]
    [InlineData("oddsnap_rand")]
    [InlineData("capture_rand_name")]
    public void Format_ReplacesLooseRandPlaceholders(string template)
    {
        var value = FileNameTemplate.Format(template);

        Assert.DoesNotContain("rand", value, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^[A-Za-z0-9_.-]+$", value);
    }

    [Fact]
    public void Format_UsesFallbackForBlankTemplates()
    {
        var value = FileNameTemplate.Format("   ");

        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2} [a-f0-9]{4}", value);
    }

    [Fact]
    public void Format_PreservesCustomPrefix()
    {
        var value = FileNameTemplate.Format("Screenshot_{day}-{month}-{year}_{hour}-{min}");

        Assert.StartsWith("Screenshot_", value, StringComparison.Ordinal);
        Assert.False(value.StartsWith("oddsnap_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FormatExample_UsesSameSanitizationAsFormat()
    {
        var value = FileNameTemplate.FormatExample("Screenshot {day}/{month}/{year} {hour}:{min}");

        Assert.Equal("Screenshot 05_04_2026 14_30", value);
    }
}
