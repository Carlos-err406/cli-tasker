namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Undo.Commands;

[Collection("IsolatedTests")]
public class RenameListCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;
    private readonly List<string> _createdLists = new();

    public RenameListCommandTests()
    {
        // Each test gets its own isolated storage
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-undo-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
        _services.Undo.ClearHistory();
    }

    public void Dispose()
    {
        _services.Undo.ClearHistory();
                if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private void CreateTestList(string name)
    {
        ListManager.CreateList(_services, name);
        _createdLists.Add(name);
    }

    [Fact]
    public void RenameList_RecordsUndoCommand()
    {
        // Arrange
        CreateTestList("testlist");

        // Act
        ListManager.RenameList(_services, "testlist", "renamed");

        // Assert
        Assert.True(_services.Undo.CanUndo);
    }

    [Fact]
    public void RenameList_Undo_RestoresOriginalName()
    {
        // Arrange
        CreateTestList("original");

        // Act
        ListManager.RenameList(_services, "original", "newname");
        _services.Undo.Undo();

        // Assert
        Assert.True(ListManager.ListExists(_services, "original"));
        Assert.False(ListManager.ListExists(_services, "newname"));
    }

    [Fact]
    public void RenameList_Redo_ReappliesRename()
    {
        // Arrange
        CreateTestList("original");

        // Act
        ListManager.RenameList(_services, "original", "newname");
        _services.Undo.Undo();
        _services.Undo.Redo();

        // Assert
        Assert.False(ListManager.ListExists(_services, "original"));
        Assert.True(ListManager.ListExists(_services, "newname"));
    }

    [Fact]
    public void RenameList_WithDefaultList_UndoRestoresDefault()
    {
        // Arrange
        CreateTestList("mydefault");
        _services.Config.SetDefaultList("mydefault");

        // Act
        ListManager.RenameList(_services, "mydefault", "renamed");

        // Verify default was updated
        Assert.Equal("renamed", _services.Config.GetDefaultList());

        // Undo
        _services.Undo.Undo();

        // Assert - default should be restored
        Assert.Equal("mydefault", _services.Config.GetDefaultList());
        Assert.True(ListManager.ListExists(_services, "mydefault"));
    }

    [Fact]
    public void RenameList_TasksPreservedAfterUndoRedo()
    {
        // Arrange
        CreateTestList("withTasks");
        var taskList = new TodoTaskList(_services, "withTasks");
        var task = TaskerCore.Models.TodoTask.CreateTodoTask("test task", "withTasks");
        taskList.AddTodoTask(task, recordUndo: false);

        // Act - rename and undo
        ListManager.RenameList(_services, "withTasks", "renamedList");
        _services.Undo.Undo();

        // Assert - task should still exist under original list name
        var restoredList = new TodoTaskList(_services, "withTasks");
        var tasks = restoredList.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal("test task", tasks[0].Description);
    }

    [Fact]
    public void RenameListCommand_Description_FormattedCorrectly()
    {
        // Arrange
        var cmd = new RenameListCommand
        {
            OldName = "old",
            NewName = "new",
            WasDefaultList = false
        };

        // Assert
        Assert.Equal("Rename list: old to new", cmd.Description);
    }

    [Fact]
    public void RenameList_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        CreateTestList("noundo");
        _services.Undo.ClearHistory();

        // Act
        ListManager.RenameList(_services, "noundo", "renamed", recordUndo: false);

        // Assert
        Assert.False(_services.Undo.CanUndo);
    }
}
