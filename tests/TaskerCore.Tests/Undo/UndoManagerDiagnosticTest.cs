namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Results;
using TaskerCore.Undo;
using TaskStatus = TaskerCore.Models.TaskStatus;

[Collection("IsolatedTests")]
public class UndoManagerDiagnosticTest : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public UndoManagerDiagnosticTest()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-diag-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
        _services.Undo.ClearHistory();
    }

    public void Dispose()
    {
        _services.Undo.ClearHistory();
        _services.Dispose();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddTask_ThenDelete_BothRecordedInHistory()
    {
        // This mimics exactly what CLI does

        // Step 1: Add a task (like `tasker add "test" -l tasks`)
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);

        // Verify add was recorded
        Assert.True(_services.Undo.CanUndo, "After add: should be able to undo");
        Assert.Equal(1, _services.Undo.UndoCount);

        // Step 2: Delete the task (like `tasker delete <id>`)
        var taskList2 = new TodoTaskList(_services);
        taskList2.DeleteTask(task.Id);

        // Verify delete was also recorded
        Assert.True(_services.Undo.CanUndo, "After delete: should be able to undo");
        Assert.Equal(2, _services.Undo.UndoCount);

        // Step 3: Check history command returns the operations
        var histories = _services.Undo.UndoHistory;
        Assert.Equal(2, histories.Count);
    }

    [Fact]
    public void UndoDelete_RestoresTaskFromTrash()
    {
        // Arrange: add then delete a task
        var task = TodoTask.CreateTodoTask("undo delete test", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);
        taskList.DeleteTask(task.Id);

        // Task should be in trash, not in active list
        Assert.Null(taskList.GetTodoTaskById(task.Id));

        // Act: undo the delete
        _services.Undo.Undo();

        // Assert: task is restored
        var restored = taskList.GetTodoTaskById(task.Id);
        Assert.NotNull(restored);
        Assert.Equal("undo delete test", restored.Description);
    }

    [Fact]
    public void UndoDelete_AfterTrashCleared_ReInsertsTask()
    {
        // Arrange: add, delete, then clear trash
        var task = TodoTask.CreateTodoTask("undo after clear", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);
        taskList.DeleteTask(task.Id);
        taskList.ClearTrash();

        // Task is gone entirely
        Assert.Null(taskList.GetTodoTaskById(task.Id));

        // Act: undo the delete — should re-insert from captured state
        _services.Undo.Undo();

        // Assert: task is restored
        var restored = taskList.GetTodoTaskById(task.Id);
        Assert.NotNull(restored);
        Assert.Equal("undo after clear", restored.Description);
    }

    [Fact]
    public void UndoCheck_RestoresOriginalStatus()
    {
        // Arrange: add a task, then check it
        var task = TodoTask.CreateTodoTask("undo check test", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);

        // Check the task (same path as `tasker check <id>`)
        var checkResult = taskList.CheckTasks([task.Id]);
        Assert.Single(checkResult.Results);
        Assert.IsType<TaskResult.Success>(checkResult.Results[0]);

        // Verify task is now Done
        var checkedTask = taskList.GetTodoTaskById(task.Id);
        Assert.Equal(TaskStatus.Done, checkedTask!.Status);

        // Act: undo the check
        var undoDesc = _services.Undo.Undo();
        Assert.NotNull(undoDesc);

        // Assert: task is back to Pending
        var restored = taskList.GetTodoTaskById(task.Id);
        Assert.NotNull(restored);
        Assert.Equal(TaskStatus.Pending, restored.Status);
    }

    [Fact]
    public void UndoCheck_PersistsAcrossProcesses()
    {
        // Simulates: tasker check <id> in one process, tasker undo in another
        var task = TodoTask.CreateTodoTask("cross-process undo", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);
        taskList.CheckTasks([task.Id]);

        // Simulate new process: reload undo history from SQLite
        _services.Undo.ReloadHistory();

        // Act: undo should revert the check, not the add
        _services.Undo.Undo();

        // Assert: task still exists with original status
        var restored = taskList.GetTodoTaskById(task.Id);
        Assert.NotNull(restored);
        Assert.Equal(TaskStatus.Pending, restored.Status);
    }

    [Fact]
    public void HistoryPersistsAcrossUndoManagerReloads()
    {
        // Add a task
        var task = TodoTask.CreateTodoTask("persist test", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task);

        // Verify it was recorded
        Assert.Equal(1, _services.Undo.UndoCount);

        // Verify history persists in SQLite by reloading
        _services.Undo.ReloadHistory();
        Assert.Equal(1, _services.Undo.UndoCount);

        // Verify the command type is preserved
        var history = _services.Undo.UndoHistory;
        Assert.Contains("add", history[0].Description.ToLower());
    }
}
