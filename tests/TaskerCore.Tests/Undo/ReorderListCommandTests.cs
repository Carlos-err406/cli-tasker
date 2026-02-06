namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

[Collection("UndoTests")]
public class ReorderListCommandTests : IDisposable
{
    private readonly string _testDir;

    public ReorderListCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-undo-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        StoragePaths.SetDirectory(_testDir);
        UndoManager.Instance.ClearHistory();
    }

    public void Dispose()
    {
        UndoManager.Instance.ClearHistory();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private void CreateList(string name)
    {
        ListManager.CreateList(name);
    }

    private string[] GetListOrder()
    {
        return TodoTaskList.GetAllListNames().ToArray();
    }

    [Fact]
    public void ReorderList_RecordsUndoCommand()
    {
        // Arrange - create lists (default "tasks" + 2 more)
        CreateList("listA");
        CreateList("listB");
        UndoManager.Instance.ClearHistory();

        // Act - move listB to index 0
        TodoTaskList.ReorderList("listB", 0);

        // Assert
        Assert.True(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void ReorderList_Undo_RestoresOriginalPosition()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        var initialOrder = GetListOrder();
        UndoManager.Instance.ClearHistory();

        // Act - move listB to index 0
        TodoTaskList.ReorderList("listB", 0);
        UndoManager.Instance.Undo();

        // Assert
        var currentOrder = GetListOrder();
        Assert.Equal(initialOrder, currentOrder);
    }

    [Fact]
    public void ReorderList_Redo_ReappliesReorder()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        UndoManager.Instance.ClearHistory();

        // Act
        TodoTaskList.ReorderList("listB", 0);
        var orderAfterReorder = GetListOrder();

        UndoManager.Instance.Undo();
        UndoManager.Instance.Redo();

        // Assert
        var currentOrder = GetListOrder();
        Assert.Equal(orderAfterReorder, currentOrder);
    }

    [Fact]
    public void ReorderList_NoChange_DoesNotRecordUndo()
    {
        // Arrange
        CreateList("listA");
        var lists = GetListOrder();
        var listAIndex = Array.IndexOf(lists, "listA");
        UndoManager.Instance.ClearHistory();

        // Act - try to move list to same position
        TodoTaskList.ReorderList("listA", listAIndex);

        // Assert - no undo recorded for no-op
        Assert.False(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void ReorderList_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        UndoManager.Instance.ClearHistory();

        // Act
        TodoTaskList.ReorderList("listB", 0, recordUndo: false);

        // Assert
        Assert.False(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void ReorderListCommand_Description_FormattedCorrectly()
    {
        // Arrange
        var cmd = new ReorderListCommand
        {
            ListName = "work",
            OldIndex = 2,
            NewIndex = 0
        };

        // Assert
        Assert.Equal("Reorder work list", cmd.Description);
    }

    [Fact]
    public void ReorderList_MultipleReorders_CanUndoAll()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        CreateList("listC");
        var initialOrder = GetListOrder();
        UndoManager.Instance.ClearHistory();

        // Act - do multiple reorders
        TodoTaskList.ReorderList("listC", 0);
        TodoTaskList.ReorderList("listA", 0);

        // Undo both
        UndoManager.Instance.Undo();
        UndoManager.Instance.Undo();

        // Assert - should be back to initial order
        var currentOrder = GetListOrder();
        Assert.Equal(initialOrder, currentOrder);
    }

    [Fact]
    public void ReorderList_TasksPreservedAfterUndoRedo()
    {
        // Arrange - create list with task
        CreateList("withTasks");
        var taskList = new TodoTaskList("withTasks");
        var task = TaskerCore.Models.TodoTask.CreateTodoTask("test task", "withTasks");
        taskList.AddTodoTask(task, recordUndo: false);
        UndoManager.Instance.ClearHistory();

        // Act - reorder and undo
        TodoTaskList.ReorderList("withTasks", 0);
        UndoManager.Instance.Undo();

        // Assert - task should still exist
        var restoredList = new TodoTaskList("withTasks");
        var tasks = restoredList.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal("test task", tasks[0].Description);
    }
}
