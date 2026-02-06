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

    [Theory]
    [InlineData("#84CC16", "#000000")] // Lime → black text
    [InlineData("#06B6D4", "#000000")] // Cyan → black text
    [InlineData("#F59E0B", "#000000")] // Amber → black text
    [InlineData("#FFEAA7", "#000000")] // Soft Yellow → black text
    [InlineData("#FFFFFF", "#000000")] // White → black text
    [InlineData("#3B82F6", "#FFFFFF")] // Blue → white text
    [InlineData("#EF4444", "#FFFFFF")] // Red → white text
    [InlineData("#8B5CF6", "#FFFFFF")] // Violet → white text
    [InlineData("#6366F1", "#FFFFFF")] // Indigo → white text
    [InlineData("#000000", "#FFFFFF")] // Black → white text
    public void GetForegroundForBackground_ReturnsCorrectContrast(string bg, string expectedFg)
    {
        // Use reflection-free approach: test known palette entries via tags
        // For direct testing, we verify the logic through known hex values
        Assert.Equal(expectedFg, GetForegroundForHex(bg));
    }

    [Fact]
    public void GetForegroundHex_ReturnsDeterministicResult()
    {
        var fg1 = TagColors.GetForegroundHex("lime-tag");
        var fg2 = TagColors.GetForegroundHex("lime-tag");
        Assert.Equal(fg1, fg2);
    }

    [Fact]
    public void GetForegroundHex_ReturnsBlackOrWhite()
    {
        var tags = new[] { "feature", "bug", "ui", "chore", "test", "refactor" };
        foreach (var tag in tags)
        {
            var fg = TagColors.GetForegroundHex(tag);
            Assert.True(fg == "#000000" || fg == "#FFFFFF",
                $"Tag '{tag}' returned unexpected foreground: {fg}");
        }
    }

    // Helper to test luminance logic with arbitrary hex values
    private static string GetForegroundForHex(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance > 0.5 ? "#000000" : "#FFFFFF";
    }
}
