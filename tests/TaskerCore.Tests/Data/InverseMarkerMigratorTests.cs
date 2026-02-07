namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class InverseMarkerMigratorTests : IDisposable
{
    private readonly TaskerServices _services;

    public InverseMarkerMigratorTests()
    {
        _services = TaskerServices.CreateInMemory();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InsertTask(string id, string description, string list = "tasks",
        string? parentId = null, Priority? priority = null, DateOnly? dueDate = null,
        string[]? tags = null)
    {
        // Ensure list exists
        _services.Db.Execute(
            "INSERT OR IGNORE INTO lists (name, sort_order) VALUES (@name, 0)",
            ("@name", list));

        var tagsJson = tags is { Length: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(tags)
            : null;

        _services.Db.Execute("""
            INSERT INTO tasks (id, description, status, created_at, list_name, parent_id, priority, due_date, tags, sort_order)
            VALUES (@id, @desc, 0, @created, @list, @parent, @priority, @due, @tags, 0)
            """,
            ("@id", id),
            ("@desc", description),
            ("@created", DateTime.UtcNow.ToString("o")),
            ("@list", list),
            ("@parent", (object?)parentId ?? DBNull.Value),
            ("@priority", (object?)(priority.HasValue ? (int)priority.Value : null) ?? DBNull.Value),
            ("@due", (object?)dueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
            ("@tags", (object?)tagsJson ?? DBNull.Value));
    }

    private void InsertDependency(string blockerId, string blockedId)
    {
        _services.Db.Execute(
            "INSERT INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
            ("@blocker", blockerId),
            ("@blocked", blockedId));
    }

    private string GetDescription(string taskId)
    {
        return _services.Db.ExecuteScalar<string>(
            "SELECT description FROM tasks WHERE id = @id",
            ("@id", taskId))!;
    }

    [Fact]
    public void ParentChild_AddsInverseSubtaskMarkerOnParent()
    {
        InsertTask("aaa", "Parent task");
        InsertTask("bbb", "Child task\n^aaa", parentId: "aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var parentDesc = GetDescription("aaa");
        var parsed = TaskDescriptionParser.Parse(parentDesc);
        Assert.NotNull(parsed.HasSubtaskIds);
        Assert.Contains("bbb", parsed.HasSubtaskIds);
    }

    [Fact]
    public void MultipleChildren_AllAddedToParent()
    {
        InsertTask("aaa", "Parent task");
        InsertTask("bbb", "Child 1\n^aaa", parentId: "aaa");
        InsertTask("ccc", "Child 2\n^aaa", parentId: "aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var parentDesc = GetDescription("aaa");
        var parsed = TaskDescriptionParser.Parse(parentDesc);
        Assert.NotNull(parsed.HasSubtaskIds);
        Assert.Contains("bbb", parsed.HasSubtaskIds);
        Assert.Contains("ccc", parsed.HasSubtaskIds);
    }

    [Fact]
    public void BlockerRelationship_AddsInverseBlockedByMarker()
    {
        InsertTask("aaa", "Blocker task\n!bbb"); // aaa blocks bbb
        InsertTask("bbb", "Blocked task");
        InsertDependency("aaa", "bbb");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var blockedDesc = GetDescription("bbb");
        var parsed = TaskDescriptionParser.Parse(blockedDesc);
        Assert.NotNull(parsed.BlockedByIds);
        Assert.Contains("aaa", parsed.BlockedByIds);
    }

    [Fact]
    public void MultipleBlockers_AllAddedToBlockedTask()
    {
        InsertTask("aaa", "Blocker 1\n!ccc");
        InsertTask("bbb", "Blocker 2\n!ccc");
        InsertTask("ccc", "Blocked by two");
        InsertDependency("aaa", "ccc");
        InsertDependency("bbb", "ccc");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var blockedDesc = GetDescription("ccc");
        var parsed = TaskDescriptionParser.Parse(blockedDesc);
        Assert.NotNull(parsed.BlockedByIds);
        Assert.Contains("aaa", parsed.BlockedByIds);
        Assert.Contains("bbb", parsed.BlockedByIds);
    }

    [Fact]
    public void ExistingInverseMarkers_NotDuplicated()
    {
        InsertTask("aaa", "Parent task\n-^bbb");
        InsertTask("bbb", "Child task\n^aaa", parentId: "aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var parentDesc = GetDescription("aaa");
        var parsed = TaskDescriptionParser.Parse(parentDesc);
        Assert.NotNull(parsed.HasSubtaskIds);
        Assert.Single(parsed.HasSubtaskIds); // Only one -^bbb, not duplicated
        Assert.Contains("bbb", parsed.HasSubtaskIds);
    }

    [Fact]
    public void Idempotent_SecondCallDoesNothing()
    {
        InsertTask("aaa", "Parent task");
        InsertTask("bbb", "Child task\n^aaa", parentId: "aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);
        var descAfterFirst = GetDescription("aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);
        var descAfterSecond = GetDescription("aaa");

        Assert.Equal(descAfterFirst, descAfterSecond);
    }

    [Fact]
    public void ConfigFlagSet_AfterMigration()
    {
        InsertTask("aaa", "Simple task");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var flag = _services.Db.ExecuteScalar<string>(
            "SELECT value FROM config WHERE key = 'inverse_markers_migrated'");
        Assert.Equal("true", flag);
    }

    [Fact]
    public void NoTasks_SetsConfigFlag()
    {
        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var flag = _services.Db.ExecuteScalar<string>(
            "SELECT value FROM config WHERE key = 'inverse_markers_migrated'");
        Assert.Equal("true", flag);
    }

    [Fact]
    public void PreservesExistingMetadata_WhenAddingInverseMarkers()
    {
        InsertTask("aaa", "Parent task with meta\np1 @2026-03-15 #work", priority: Priority.High, dueDate: new DateOnly(2026, 3, 15), tags: ["work"]);
        InsertTask("bbb", "Child task\n^aaa", parentId: "aaa");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var parentDesc = GetDescription("aaa");
        var parsed = TaskDescriptionParser.Parse(parentDesc);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(new DateOnly(2026, 3, 15), parsed.DueDate);
        Assert.Contains("work", parsed.Tags);
        Assert.NotNull(parsed.HasSubtaskIds);
        Assert.Contains("bbb", parsed.HasSubtaskIds);
    }

    [Fact]
    public void MixedRelationships_AllMigrated()
    {
        // aaa is parent of bbb, and aaa blocks ccc
        InsertTask("aaa", "Main task\n!ccc");
        InsertTask("bbb", "Child of aaa\n^aaa", parentId: "aaa");
        InsertTask("ccc", "Blocked by aaa");
        InsertDependency("aaa", "ccc");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        // aaa should have -^bbb (subtask marker)
        var aaaParsed = TaskDescriptionParser.Parse(GetDescription("aaa"));
        Assert.NotNull(aaaParsed.HasSubtaskIds);
        Assert.Contains("bbb", aaaParsed.HasSubtaskIds);

        // ccc should have -!aaa (blocked by marker)
        var cccParsed = TaskDescriptionParser.Parse(GetDescription("ccc"));
        Assert.NotNull(cccParsed.BlockedByIds);
        Assert.Contains("aaa", cccParsed.BlockedByIds);
    }

    [Fact]
    public void TrashedTasks_NotMigrated()
    {
        InsertTask("aaa", "Parent task");
        InsertTask("bbb", "Child task\n^aaa", parentId: "aaa");

        // Trash the child — parent shouldn't get inverse marker for trashed child
        _services.Db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = 'bbb'");

        InverseMarkerMigrator.MigrateIfNeeded(_services.Db);

        var parentDesc = GetDescription("aaa");
        var parsed = TaskDescriptionParser.Parse(parentDesc);
        Assert.Null(parsed.HasSubtaskIds);
    }
}
