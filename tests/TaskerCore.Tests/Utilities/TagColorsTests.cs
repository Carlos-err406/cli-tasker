namespace TaskerCore.Tests.Utilities;

using TaskerCore.Utilities;

public class TagColorsTests
{
    [Fact]
    public void GetHexColor_SameTag_ReturnsSameColor()
    {
        var color1 = TagColors.GetHexColor("feature");
        var color2 = TagColors.GetHexColor("feature");
        Assert.Equal(color1, color2);
    }

    [Fact]
    public void GetHexColor_DifferentTags_ReturnsDifferentColors()
    {
        var color1 = TagColors.GetHexColor("feature");
        var color2 = TagColors.GetHexColor("bug");
        // These specific tags should hash to different colors
        Assert.NotEqual(color1, color2);
    }

    [Fact]
    public void GetHexColor_ReturnsValidHexFormat()
    {
        var color = TagColors.GetHexColor("test");
        Assert.Matches(@"^#[0-9A-Fa-f]{6}$", color);
    }

    [Fact]
    public void GetSpectreMarkup_ReturnsValidMarkup()
    {
        var markup = TagColors.GetSpectreMarkup("feature");
        Assert.StartsWith("[#", markup);
        Assert.EndsWith("]", markup);
    }

    [Theory]
    [InlineData("feature")]
    [InlineData("bug")]
    [InlineData("ui")]
    [InlineData("chore")]
    [InlineData("test-tag")]
    [InlineData("UPPERCASE")]
    [InlineData("123")]
    [InlineData("emoji🎉")]
    public void GetHexColor_IsDeterministic_ForVariousTags(string tag)
    {
        // Run multiple times to ensure determinism
        var firstResult = TagColors.GetHexColor(tag);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(firstResult, TagColors.GetHexColor(tag));
        }
    }

    [Fact]
    public void GetHexColor_CaseSensitive()
    {
        var lower = TagColors.GetHexColor("bug");
        var upper = TagColors.GetHexColor("Bug");
        var allCaps = TagColors.GetHexColor("BUG");

        // Different cases should produce different colors
        Assert.NotEqual(lower, upper);
        Assert.NotEqual(upper, allCaps);
    }

    [Fact]
    public void GetHexColor_ReturnsColorFromPalette()
    {
        // Known palette colors
        var palette = new[]
        {
            "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
            "#EC4899", "#06B6D4", "#84CC16", "#F97316", "#6366F1"
        };

        // Test several tags - all should return palette colors
        var tags = new[] { "feature", "bug", "ui", "chore", "test" };
        foreach (var tag in tags)
        {
            var color = TagColors.GetHexColor(tag);
            Assert.Contains(color, palette);
        }
    }
}
