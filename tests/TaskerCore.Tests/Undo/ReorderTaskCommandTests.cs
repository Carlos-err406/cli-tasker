namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

[Collection("UndoTests")]
public class ReorderTaskCommandTests : IDisposable
{
    private readonly string _testDir;

    public ReorderTaskCommandTests()
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

    private TodoTask CreateTask(string description, string listName = "tasks")
    {
        var taskList = new TodoTaskList(listName);
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
        UndoManager.Instance.ClearHistory();

        // Act - move task3 (index 0) to index 2
        TodoTaskList.ReorderTask(task3.Id, 2);

        // Assert
        Assert.True(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void ReorderTask_Undo_RestoresOriginalPosition()
    {
        // Arrange - create 3 tasks (newest first: task3, task2, task1)
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        var task3 = CreateTask("Task 3");
        UndoManager.Instance.ClearHistory();

        // Act - move task3 from index 0 to index 2
        TodoTaskList.ReorderTask(task3.Id, 2);
        UndoManager.Instance.Undo();

        // Assert - task3 should be back at index 0
        var taskList = new TodoTaskList("tasks");
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
        UndoManager.Instance.ClearHistory();

        // Act
        TodoTaskList.ReorderTask(task3.Id, 2);
        UndoManager.Instance.Undo();
        UndoManager.Instance.Redo();

        // Assert - task3 should be at index 2 again
        var taskList = new TodoTaskList("tasks");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(task3.Id, tasks[2].Id);
    }

    [Fact]
    public void ReorderTask_NoChange_DoesNotRecordUndo()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        UndoManager.Instance.ClearHistory();

        // Act - try to move task to same position (index 0)
        TodoTaskList.ReorderTask(task1.Id, 0);

        // Assert - no undo recorded for no-op
        Assert.False(UndoManager.Instance.CanUndo);
    }

    [Fact]
    public void ReorderTask_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        var task1 = CreateTask("Task 1");
        var task2 = CreateTask("Task 2");
        UndoManager.Instance.ClearHistory();

        // Act
        TodoTaskList.ReorderTask(task2.Id, 1, recordUndo: false);

        // Assert
        Assert.False(UndoManager.Instance.CanUndo);
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
        UndoManager.Instance.ClearHistory();

        // Verify initial order: task4, task3, task2, task1
        var initialList = new TodoTaskList("tasks");
        var initialTasks = initialList.GetAllTasks();
        Assert.Equal(task4.Id, initialTasks[0].Id);
        Assert.Equal(task3.Id, initialTasks[1].Id);

        // Act - do multiple reorders
        TodoTaskList.ReorderTask(task4.Id, 3); // move task4 to end
        TodoTaskList.ReorderTask(task3.Id, 2); // move task3 down

        // Undo both
        UndoManager.Instance.Undo();
        UndoManager.Instance.Undo();

        // Assert - should be back to initial order
        var taskList = new TodoTaskList("tasks");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(task4.Id, tasks[0].Id);
        Assert.Equal(task3.Id, tasks[1].Id);
        Assert.Equal(task2.Id, tasks[2].Id);
        Assert.Equal(task1.Id, tasks[3].Id);
    }
}
