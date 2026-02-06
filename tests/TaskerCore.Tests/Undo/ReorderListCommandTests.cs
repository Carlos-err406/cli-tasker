namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Undo.Commands;

[Collection("IsolatedTests")]
public class ReorderListCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public ReorderListCommandTests()
    {
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

    private void CreateList(string name)
    {
        ListManager.CreateList(_services, name);
    }

    private string[] GetListOrder()
    {
        return TodoTaskList.GetAllListNames(_services).ToArray();
    }

    [Fact]
    public void ReorderList_RecordsUndoCommand()
    {
        // Arrange - create lists (default "tasks" + 2 more)
        CreateList("listA");
        CreateList("listB");
        _services.Undo.ClearHistory();

        // Act - move listB to index 0
        TodoTaskList.ReorderList(_services, "listB", 0);

        // Assert
        Assert.True(_services.Undo.CanUndo);
    }

    [Fact]
    public void ReorderList_Undo_RestoresOriginalPosition()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        var initialOrder = GetListOrder();
        _services.Undo.ClearHistory();

        // Act - move listB to index 0
        TodoTaskList.ReorderList(_services, "listB", 0);
        _services.Undo.Undo();

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
        _services.Undo.ClearHistory();

        // Act
        TodoTaskList.ReorderList(_services, "listB", 0);
        var orderAfterReorder = GetListOrder();

        _services.Undo.Undo();
        _services.Undo.Redo();

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
        _services.Undo.ClearHistory();

        // Act - try to move list to same position
        TodoTaskList.ReorderList(_services, "listA", listAIndex);

        // Assert - no undo recorded for no-op
        Assert.False(_services.Undo.CanUndo);
    }

    [Fact]
    public void ReorderList_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        CreateList("listA");
        CreateList("listB");
        _services.Undo.ClearHistory();

        // Act
        TodoTaskList.ReorderList(_services, "listB", 0, recordUndo: false);

        // Assert
        Assert.False(_services.Undo.CanUndo);
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
        _services.Undo.ClearHistory();

        // Act - do multiple reorders
        TodoTaskList.ReorderList(_services, "listC", 0);
        TodoTaskList.ReorderList(_services, "listA", 0);

        // Undo both
        _services.Undo.Undo();
        _services.Undo.Undo();

        // Assert - should be back to initial order
        var currentOrder = GetListOrder();
        Assert.Equal(initialOrder, currentOrder);
    }

    [Fact]
    public void ReorderList_TasksPreservedAfterUndoRedo()
    {
        // Arrange - create list with task
        CreateList("withTasks");
        var taskList = new TodoTaskList(_services, "withTasks");
        var task = TaskerCore.Models.TodoTask.CreateTodoTask("test task", "withTasks");
        taskList.AddTodoTask(task, recordUndo: false);
        _services.Undo.ClearHistory();

        // Act - reorder and undo
        TodoTaskList.ReorderList(_services, "withTasks", 0);
        _services.Undo.Undo();

        // Assert - task should still exist
        var restoredList = new TodoTaskList(_services, "withTasks");
        var tasks = restoredList.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal("test task", tasks[0].Description);
    }
}
