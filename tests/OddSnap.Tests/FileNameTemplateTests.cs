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

        Assert.StartsWith("oddsnap_", value, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"oddsnap_\d{8}_\d{6}_[a-f0-9]{4}", value);
    }
}
