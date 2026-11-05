using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

public class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, 0, null)]
    [InlineData("V1.2.3", 1, 2, 3, 0, null)]
    [InlineData("1.2.3", 1, 2, 3, 0, null)]
    [InlineData("1.2", 1, 2, 0, 0, null)]
    [InlineData("1.2.3.4", 1, 2, 3, 4, null)]
    [InlineData("1.2.3+build.5", 1, 2, 3, 0, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, 0, "beta.1")]
    [InlineData(" v1.0.1 ", 1, 0, 1, 0, null)]
    public void TryParse_ValidText_ReturnsParts(string text, int major, int minor, int patch, int revision, string? pre)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch, revision, pre), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.x.3")]
    [InlineData("release-2026")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.2.3")]
    public void TryParse_Junk_ReturnsFalse(string? text)
        => Assert.False(AppVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.10.0", "1.9.9")]
    [InlineData("2.0", "1.99.99")]
    [InlineData("1.0.1", "1.0.1-beta.1")]
    [InlineData("1.0.1.1", "1.0.1")]
    [InlineData("1.0.1-beta.2", "1.0.1-beta.1")]
    public void Compare_Newer_IsGreater(string newer, string older)
    {
        Assert.True(Parse(newer) > Parse(older));
        Assert.True(Parse(older) < Parse(newer));
    }

    [Fact]
    public void Compare_FourthPartZero_EqualsThreeParts()
    {
        Assert.Equal(0, Parse("1.0.1.0").CompareTo(Parse("1.0.1")));
        Assert.True(Parse("1.0.1.0") >= Parse("v1.0.1"));
    }

    [Theory]
    [InlineData("1.0.1.0", "1.0.1")]
    [InlineData("v2.3", "2.3.0")]
    [InlineData("1.0.1.4", "1.0.1.4")]
    [InlineData("1.0.1-beta.1", "1.0.1-beta.1")]
    public void ToString_IsCanonical(string text, string expected)
        => Assert.Equal(expected, Parse(text).ToString());

    [Fact]
    public void FromSystemVersion_UsesAssemblyParts()
        => Assert.Equal("1.0.1", AppVersion.FromSystemVersion(new Version(1, 0, 1, 0)).ToString());

    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var v), text);
        return v;
    }
}
