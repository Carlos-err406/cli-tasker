namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;

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
