namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo.Commands;

[Collection("IsolatedTests")]
public class ReorderTaskCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public ReorderTaskCommandTests()
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

    private TodoTask CreateTask(string description, string listName = "tasks")
    {
        var taskList = new TodoTaskList(_services, listName);
        var task = TodoTask.CreateTodoTask(description, listName);
        taskList.AddTodoTask(task, recordUndo: false);
        return task;
    }

    [Fact]
    public void ReorderTask_RecordsUndoCommand()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        var task3 = CreateTask("Task 3");
        _services.Undo.ClearHistory();

        // Act - move task3 (index 0) to index 2
        TodoTaskList.ReorderTask(_services, task3.Id, 2);

        // Assert
        Assert.True(_services.Undo.CanUndo);
    }

    [Fact]
    public void ReorderTask_Undo_RestoresOriginalPosition()
    {
        // Arrange - create 3 tasks (newest first: task3, task2, task1)
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        var task3 = CreateTask("Task 3");
        _services.Undo.ClearHistory();

        // Act - move task3 from index 0 to index 2
        TodoTaskList.ReorderTask(_services, task3.Id, 2);
        _services.Undo.Undo();

        // Assert - task3 should be back at index 0
        var taskList = new TodoTaskList(_services, "tasks");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(task3.Id, tasks[0].Id);
    }

    [Fact]
    public void ReorderTask_Redo_ReappliesReorder()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        var task3 = CreateTask("Task 3");
        _services.Undo.ClearHistory();

        // Act
        TodoTaskList.ReorderTask(_services, task3.Id, 2);
        _services.Undo.Undo();
        _services.Undo.Redo();

        // Assert - task3 should be at index 2 again
        var taskList = new TodoTaskList(_services, "tasks");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(task3.Id, tasks[2].Id);
    }

    [Fact]
    public void ReorderTask_NoChange_DoesNotRecordUndo()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        _services.Undo.ClearHistory();

        // Act - try to move task to same position (index 0)
        TodoTaskList.ReorderTask(_services, task1.Id, 0);

        // Assert - no undo recorded for no-op
        Assert.False(_services.Undo.CanUndo);
    }

    [Fact]
    public void ReorderTask_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        _services.Undo.ClearHistory();

        // Act
        TodoTaskList.ReorderTask(_services, task2.Id, 1, recordUndo: false);

        // Assert
        Assert.False(_services.Undo.CanUndo);
    }

    [Fact]
    public void ReorderTaskCommand_Description_FormattedCorrectly()
    {
        // Arrange
        var cmd = new ReorderTaskCommand
        {
            TaskId = "abc",
            ListName = "work",
            OldIndex = 0,
            NewIndex = 2
        };

        // Assert
        Assert.Equal("Reorder task in work", cmd.Description);
    }

    [Fact]
    public void ReorderTask_MultipleReorders_CanUndoAll()
    {
        // Arrange - create 4 tasks
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        var task3 = CreateTask("Task 3");
        var task4 = CreateTask("Task 4");
        _services.Undo.ClearHistory();

        // Verify initial order: task4, task3, task2, task1
        var initialList = new TodoTaskList(_services, "tasks");
        var initialTasks = initialList.GetAllTasks();
        Assert.Equal(task4.Id, initialTasks[0].Id);
        Assert.Equal(task3.Id, initialTasks[1].Id);

        // Act - do multiple reorders
        TodoTaskList.ReorderTask(_services, task4.Id, 3); // move task4 to end
        TodoTaskList.ReorderTask(_services, task3.Id, 2); // move task3 down

        // Undo both
        _services.Undo.Undo();
        _services.Undo.Undo();

        // Assert - should be back to initial order
        var taskList = new TodoTaskList(_services, "tasks");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(task4.Id, tasks[0].Id);
        Assert.Equal(task3.Id, tasks[1].Id);
        Assert.Equal(task2.Id, tasks[2].Id);
        Assert.Equal(task1.Id, tasks[3].Id);
    }
}
