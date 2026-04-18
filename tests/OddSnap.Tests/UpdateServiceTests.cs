using System.Reflection;
using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)]
    [InlineData("", null)]
    public void TryExtractSha256Hex_ParsesOnlyValidSha256Digests(string digest, string? expected)
    {
        var method = typeof(UpdateService).GetMethod("TryExtractSha256Hex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = (string?)method!.Invoke(null, new object?[] { digest });

        Assert.Equal(expected, actual);
    }
}
