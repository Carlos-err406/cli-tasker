namespace TaskerCore.Tests.Parsing;

using TaskerCore.Models;
using TaskerCore.Parsing;

public class TaskDescriptionParserTests
{
    [Theory]
    [InlineData("task\np1", Priority.High)]
    [InlineData("task\np2", Priority.Medium)]
    [InlineData("task\np3", Priority.Low)]
    [InlineData("task\nP1", Priority.High)] // Case insensitive
    public void Parse_Priority_ExtractsCorrectly(string input, Priority expected)
    {
        var result = TaskDescriptionParser.Parse(input);
        Assert.Equal(expected, result.Priority);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_NoPriority_ReturnsNull()
    {
        var result = TaskDescriptionParser.Parse("just a task");
        Assert.Null(result.Priority);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_PriorityInContent_NotParsed()
    {
        // p1 is not on a metadata-only line
        var result = TaskDescriptionParser.Parse("task with p1 in text");
        Assert.Null(result.Priority);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    [Theory]
    [InlineData("#simple", "simple")]
    [InlineData("#with-hyphen", "with-hyphen")]
    [InlineData("#multi_underscore", "multi_underscore")]
    [InlineData("#CamelCase", "CamelCase")]
    public void Parse_Tags_SupportsVariousFormats(string tag, string expected)
    {
        var result = TaskDescriptionParser.Parse($"task\n{tag}");
        Assert.Contains(expected, result.Tags);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_MultipleTags_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("task\n#tag1 #tag2 #cli-only");
        Assert.Equal(3, result.Tags.Length);
        Assert.Contains("tag1", result.Tags);
        Assert.Contains("tag2", result.Tags);
        Assert.Contains("cli-only", result.Tags);
    }

    [Fact]
    public void Parse_DueDate_Today()
    {
        var result = TaskDescriptionParser.Parse("task\n@today");
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), result.DueDate);
    }

    [Fact]
    public void Parse_DueDate_Tomorrow()
    {
        var result = TaskDescriptionParser.Parse("task\n@tomorrow");
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today.AddDays(1)), result.DueDate);
    }

    [Fact]
    public void Parse_CombinedMetadata_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("task\np1 @today #urgent #work");
        Assert.Equal(Priority.High, result.Priority);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), result.DueDate);
        Assert.Equal(2, result.Tags.Length);
        Assert.Contains("urgent", result.Tags);
        Assert.Contains("work", result.Tags);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_MixedContentAndMetadata_NotParsed()
    {
        // Last line has non-metadata content
        var result = TaskDescriptionParser.Parse("task\nsome text p1");
        Assert.Null(result.Priority);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResult()
    {
        var result = TaskDescriptionParser.Parse("");
        Assert.Null(result.Priority);
        Assert.Null(result.DueDate);
        Assert.Empty(result.Tags);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void GetDisplayDescription_HidesMetadataOnlyLastLine()
    {
        var display = TaskDescriptionParser.GetDisplayDescription("My task\np1 #urgent");
        Assert.Equal("My task", display);
    }

    [Fact]
    public void GetDisplayDescription_KeepsNonMetadataLines()
    {
        var display = TaskDescriptionParser.GetDisplayDescription("My task\nsome details");
        Assert.Equal("My task\nsome details", display);
    }

    [Fact]
    public void GetDisplayDescription_SingleMetadataLine_StillShows()
    {
        // Single line that's only metadata should still show (otherwise task would be empty)
        var display = TaskDescriptionParser.GetDisplayDescription("p1 #urgent");
        Assert.Equal("p1 #urgent", display);
    }

    [Fact]
    public void SyncMetadataToDescription_AddsMetadataLine()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task", Priority.High, null, null);
        Assert.Equal("task\np1", result);
    }

    [Fact]
    public void SyncMetadataToDescription_UpdatesExistingMetadataLine()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task\np3", Priority.High, null, null);
        Assert.Equal("task\np1", result);
    }

    [Fact]
    public void SyncMetadataToDescription_RemovesMetadataLine_WhenEmpty()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task\np1", null, null, null);
        Assert.Equal("task", result);
    }
}
