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

    // --- Parent reference (^abc) tests ---

    [Fact]
    public void Parse_ParentRef_ExtractsParentId()
    {
        var result = TaskDescriptionParser.Parse("task\n^abc");
        Assert.Equal("abc", result.ParentId);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_ParentRef_NotParsedFromNonMetadataLine()
    {
        var result = TaskDescriptionParser.Parse("task with ^abc in text");
        Assert.Null(result.ParentId);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    // --- Blocks reference (!abc) tests ---

    [Fact]
    public void Parse_BlocksRef_ExtractsBlockedId()
    {
        var result = TaskDescriptionParser.Parse("task\n!h67");
        Assert.NotNull(result.BlocksIds);
        Assert.Single(result.BlocksIds);
        Assert.Equal("h67", result.BlocksIds[0]);
    }

    [Fact]
    public void Parse_MultipleBlocksRefs_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("task\n!h67 !j89");
        Assert.NotNull(result.BlocksIds);
        Assert.Equal(2, result.BlocksIds.Length);
        Assert.Contains("h67", result.BlocksIds);
        Assert.Contains("j89", result.BlocksIds);
    }

    [Fact]
    public void Parse_BlocksRef_NotParsedFromNonMetadataLine()
    {
        var result = TaskDescriptionParser.Parse("fix bug! important");
        Assert.Null(result.BlocksIds);
        Assert.False(result.LastLineIsMetadataOnly);
    }

    // --- Combined dependency + metadata tests ---

    [Fact]
    public void Parse_CombinedDependenciesAndMetadata_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("build API\n^abc !h67 #feature p1");
        Assert.Equal("abc", result.ParentId);
        Assert.NotNull(result.BlocksIds);
        Assert.Single(result.BlocksIds);
        Assert.Equal("h67", result.BlocksIds[0]);
        Assert.Equal(Priority.High, result.Priority);
        Assert.Contains("feature", result.Tags);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    // --- GetDisplayDescription hides dependency tokens ---

    [Fact]
    public void GetDisplayDescription_HidesDependencyTokens()
    {
        var display = TaskDescriptionParser.GetDisplayDescription("My task\n^abc !h67 p1");
        Assert.Equal("My task", display);
    }

    // --- SyncMetadataToDescription with dependencies ---

    [Fact]
    public void SyncMetadataToDescription_IncludesParentAndBlocks()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task", Priority.High, null, null, parentId: "abc", blocksIds: ["h67"]);
        Assert.Equal("task\n^abc !h67 p1", result);
    }

    [Fact]
    public void SyncMetadataToDescription_UpdatesExistingDependencyTokens()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task\n^abc p1", null, null, null, parentId: "def", blocksIds: null);
        Assert.Equal("task\n^def", result);
    }

    // --- TodoTask.Rename() metadata handling ---

    [Fact]
    public void TodoTask_Rename_WithParentToken_SetsParentId()
    {
        var task = TodoTask.CreateTodoTask("original task", "tasks");
        var renamed = task.Rename("renamed task\n^abc");

        Assert.Equal("abc", renamed.ParentId);
    }

    [Fact]
    public void TodoTask_Rename_RemovingParentToken_ClearsParentId()
    {
        var task = TodoTask.CreateTodoTask("child task\n^abc", "tasks");
        Assert.Equal("abc", task.ParentId);

        // Rename with metadata line that doesn't have ^parent
        var renamed = task.Rename("child task\n#tag");

        Assert.Null(renamed.ParentId);
    }

    [Fact]
    public void TodoTask_Rename_NoMetadataLine_PreservesParentId()
    {
        var task = TodoTask.CreateTodoTask("child task\n^abc", "tasks");
        Assert.Equal("abc", task.ParentId);

        // Rename with no metadata line — parent preserved
        var renamed = task.Rename("renamed child task");

        Assert.Equal("abc", renamed.ParentId);
    }

    [Fact]
    public void TodoTask_Rename_SwapParentToBlockerToken_ClearsParent()
    {
        var task = TodoTask.CreateTodoTask("my task\n^abc", "tasks");
        Assert.Equal("abc", task.ParentId);

        // Change from ^abc to !abc
        var renamed = task.Rename("my task\n!abc");

        Assert.Null(renamed.ParentId);
    }

    [Fact]
    public void TodoTask_Rename_ChangingParentToken_UpdatesParentId()
    {
        var task = TodoTask.CreateTodoTask("child task\n^abc", "tasks");
        Assert.Equal("abc", task.ParentId);

        var renamed = task.Rename("child task\n^def");

        Assert.Equal("def", renamed.ParentId);
    }

    [Fact]
    public void TodoTask_Rename_EmptyMetadataLine_ClearsAllMetadata()
    {
        var task = TodoTask.CreateTodoTask("my task\np1 ^abc #tag", "tasks");
        Assert.Equal("abc", task.ParentId);
        Assert.Equal(Priority.High, task.Priority);

        // Rename to a different metadata line with nothing
        var renamed = task.Rename("my task\n#newtag");

        Assert.Null(renamed.ParentId);
        Assert.Null(renamed.Priority);
        Assert.Equal(["newtag"], renamed.Tags);
    }

    // --- Related reference (~abc) tests ---

    [Fact]
    public void Parse_RelatedRef_ExtractsRelatedId()
    {
        var result = TaskDescriptionParser.Parse("task\n~abc");
        Assert.NotNull(result.RelatedIds);
        Assert.Single(result.RelatedIds);
        Assert.Equal("abc", result.RelatedIds[0]);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void Parse_MultipleRelatedRefs_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("task\n~abc ~def");
        Assert.NotNull(result.RelatedIds);
        Assert.Equal(2, result.RelatedIds.Length);
        Assert.Contains("abc", result.RelatedIds);
        Assert.Contains("def", result.RelatedIds);
    }

    [Fact]
    public void Parse_MixedRelatedAndOtherMetadata_ExtractsAll()
    {
        var result = TaskDescriptionParser.Parse("build API\n~abc !h67 p1 #tag");
        Assert.NotNull(result.RelatedIds);
        Assert.Single(result.RelatedIds);
        Assert.Equal("abc", result.RelatedIds[0]);
        Assert.NotNull(result.BlocksIds);
        Assert.Equal("h67", result.BlocksIds[0]);
        Assert.Equal(Priority.High, result.Priority);
        Assert.Contains("tag", result.Tags);
        Assert.True(result.LastLineIsMetadataOnly);
    }

    [Fact]
    public void GetDisplayDescription_HidesRelatedTokens()
    {
        var display = TaskDescriptionParser.GetDisplayDescription("My task\n~abc ~def");
        Assert.Equal("My task", display);
    }

    [Fact]
    public void SyncMetadataToDescription_IncludesRelatedIds()
    {
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task", null, null, null, relatedIds: ["abc", "def"]);
        Assert.Equal("task\n~abc ~def", result);
    }

    [Fact]
    public void SyncMetadataToDescription_RelatedIds_CorrectOrder()
    {
        // Order: ^parent !blocks ~related p1 @date #tags
        var result = TaskDescriptionParser.SyncMetadataToDescription(
            "task", Priority.High, null, ["tag"], parentId: "abc", blocksIds: ["h67"], relatedIds: ["xyz"]);
        Assert.Equal("task\n^abc !h67 ~xyz p1 #tag", result);
    }
}
