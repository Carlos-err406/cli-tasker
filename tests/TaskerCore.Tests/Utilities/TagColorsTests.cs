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

        Assert.NotEqual(lower, upper);
        Assert.NotEqual(upper, allCaps);
    }

    [Fact]
    public void GetHexColor_ReturnsColorFromPalette()
    {
        var palette = new[]
        {
            "#2563EB", "#059669", "#B45309", "#DC2626", "#7C3AED",
            "#DB2777", "#0891B2", "#4D7C0F", "#EA580C", "#4F46E5"
        };

        var tags = new[] { "feature", "bug", "ui", "chore", "test" };
        foreach (var tag in tags)
        {
            var color = TagColors.GetHexColor(tag);
            Assert.Contains(color, palette);
        }
    }

    [Fact]
    public void AllPaletteColors_AreDarkEnoughForWhiteText()
    {
        var palette = new[]
        {
            "#2563EB", "#059669", "#B45309", "#DC2626", "#7C3AED",
            "#DB2777", "#0891B2", "#4D7C0F", "#EA580C", "#4F46E5"
        };

        foreach (var hex in palette)
        {
            var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            Assert.True(luminance <= 0.5,
                $"Color {hex} has luminance {luminance:F2} - too light for white text");
        }
    }
}
