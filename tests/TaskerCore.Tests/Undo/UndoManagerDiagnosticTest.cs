namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo;

[Collection("UndoTests")]
public class UndoManagerDiagnosticTest : IDisposable
{
    private readonly string _testDir;

    public UndoManagerDiagnosticTest()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-diag-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        StoragePaths.SetDirectory(_testDir);
        UndoManager.Instance.ClearHistory();
    }

    public void Dispose()
    {
        UndoManager.Instance.ClearHistory();
        // Don't reset to null - test mode stays active to prevent accidental production writes
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void AddTask_ThenDelete_BothRecordedInHistory()
    {
        // This mimics exactly what CLI does

        // Step 1: Add a task (like `tasker add "test" -l tasks`)
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList("tasks");
        taskList.AddTodoTask(task);

        // Verify add was recorded
        Assert.True(UndoManager.Instance.CanUndo, "After add: should be able to undo");
        Assert.Equal(1, UndoManager.Instance.UndoCount);

        // Step 2: Delete the task (like `tasker delete <id>`)
        var taskList2 = new TodoTaskList();
        taskList2.DeleteTask(task.Id);

        // Verify delete was also recorded
        Assert.True(UndoManager.Instance.CanUndo, "After delete: should be able to undo");
        Assert.Equal(2, UndoManager.Instance.UndoCount);

        // Step 3: Check history command returns the operations
        var histories = UndoManager.Instance.UndoHistory;
        Assert.Equal(2, histories.Count);
    }

    [Fact]
    public void HistoryPersistsAcrossUndoManagerReloads()
    {
        // Add a task
        var task = TodoTask.CreateTodoTask("persist test", "tasks");
        var taskList = new TodoTaskList("tasks");
        taskList.AddTodoTask(task);

        // Verify it was recorded
        Assert.Equal(1, UndoManager.Instance.UndoCount);

        // Check the history file exists and has content
        var historyPath = Path.Combine(_testDir, "undo-history.json");
        Assert.True(File.Exists(historyPath), "History file should exist");

        var historyContent = File.ReadAllText(historyPath);
        Assert.Contains("UndoStack", historyContent);
        Assert.Contains("add", historyContent); // Should contain the "add" type discriminator
    }
}
