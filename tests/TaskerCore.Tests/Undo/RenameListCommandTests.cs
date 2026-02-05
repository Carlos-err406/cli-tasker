namespace TaskerCore.Tests.Undo;

using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

[Collection("UndoTests")]
public class RenameListCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly List<string> _createdLists = new();

    public RenameListCommandTests()
    {
        // Each test gets its own isolated storage
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-undo-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        StoragePaths.SetDirectory(_testDir);
        UndoManager.Instance.ClearHistory();
    }

    public void Dispose()
    {
        UndoManager.Instance.ClearHistory();
        StoragePaths.SetDirectory(null);
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private void CreateTestList(string name)
    {
        ListManager.CreateList(name);
        _createdLists.Add(name);
    }

    [Fact]
    public void RenameList_RecordsUndoCommand()
    {
        // Arrange
        CreateTestList("testlist");

        // Act
        ListManager.RenameList("testlist", "renamed");

        // Assert
        Assert.True(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void RenameList_Undo_RestoresOriginalName()
    {
        // Arrange
        CreateTestList("original");

        // Act
        ListManager.RenameList("original", "newname");
        UndoManager.Instance.Undo();

        // Assert
        Assert.True(ListManager.ListExists("original"));
        Assert.False(ListManager.ListExists("newname"));
    }

    [Fact]
    public void RenameList_Redo_ReappliesRename()
    {
        // Arrange
        CreateTestList("original");

        // Act
        ListManager.RenameList("original", "newname");
        UndoManager.Instance.Undo();
        UndoManager.Instance.Redo();

        // Assert
        Assert.False(ListManager.ListExists("original"));
        Assert.True(ListManager.ListExists("newname"));
    }

    [Fact]
    public void RenameList_WithDefaultList_UndoRestoresDefault()
    {
        // Arrange
        CreateTestList("mydefault");
        AppConfig.SetDefaultList("mydefault");

        // Act
        ListManager.RenameList("mydefault", "renamed");

        // Verify default was updated
        Assert.Equal("renamed", AppConfig.GetDefaultList());

        // Undo
        UndoManager.Instance.Undo();

        // Assert - default should be restored
        Assert.Equal("mydefault", AppConfig.GetDefaultList());
        Assert.True(ListManager.ListExists("mydefault"));
    }

    [Fact]
    public void RenameList_TasksPreservedAfterUndoRedo()
    {
        // Arrange
        CreateTestList("withTasks");
        var taskList = new TodoTaskList("withTasks");
        var task = TaskerCore.Models.TodoTask.CreateTodoTask("test task", "withTasks");
        taskList.AddTodoTask(task, recordUndo: false);

        // Act - rename and undo
        ListManager.RenameList("withTasks", "renamedList");
        UndoManager.Instance.Undo();

        // Assert - task should still exist under original list name
        var restoredList = new TodoTaskList("withTasks");
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
        UndoManager.Instance.ClearHistory();

        // Act
        ListManager.RenameList("noundo", "renamed", recordUndo: false);

        // Assert
        Assert.False(UndoManager.Instance.CanUndo);
    }
}
