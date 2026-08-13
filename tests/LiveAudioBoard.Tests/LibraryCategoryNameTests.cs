using LiveAudioBoard.Core.Library;

namespace LiveAudioBoard.Tests;

public sealed class LibraryCategoryNameTests
{
    [Theory]
    [InlineData(null, "未分类")]
    [InlineData("", "未分类")]
    [InlineData("  游戏   音效  ", "游戏 音效")]
    [InlineData("直播\t常用\r\n", "直播 常用")]
    public void NormalizeProducesSafeUserCategory(string? value, string expected)
    {
        Assert.Equal(expected, LibraryCategoryName.Normalize(value));
    }

    [Fact]
    public void ResolveReusesExistingCategoryCasing()
    {
        var resolved = LibraryCategoryName.Resolve(
            "  DOWNLOADS ",
            ["音乐", "Downloads", "未分类"]);

        Assert.Equal("Downloads", resolved);
    }

    [Fact]
    public void NormalizeLimitsCategoryLength()
    {
        var resolved = LibraryCategoryName.Normalize(
            new string('分', LibraryCategoryName.MaximumLength + 20));

        Assert.Equal(LibraryCategoryName.MaximumLength, resolved.Length);
    }
}
