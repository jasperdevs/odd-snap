using System.Text.RegularExpressions;
using Xunit;
using Yoink.Helpers;

namespace Yoink.Tests;

public sealed class FileNameTemplateTests
{
    [Theory]
    [InlineData("rand")]
    [InlineData("(rand)")]
    [InlineData("[rand]")]
    [InlineData("yoink_rand")]
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

        Assert.StartsWith("yoink_", value, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"yoink_\d{8}_\d{6}_[a-f0-9]{4}", value);
    }
}
