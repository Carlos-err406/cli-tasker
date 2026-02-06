namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class TaskTimestampTests : IDisposable
{
    private readonly TaskerServices _services;

    public TaskTimestampTests()
    {
        _services = TaskerServices.CreateInMemory();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SetStatus_ToDone_SetsCompletedAt()
    {
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task, recordUndo: false);

        Assert.Null(taskList.GetTodoTaskById(task.Id)!.CompletedAt);

        taskList.SetStatus(task.Id, TaskStatus.Done, recordUndo: false);

        var updated = taskList.GetTodoTaskById(task.Id)!;
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public void SetStatus_FromDone_ClearsCompletedAt()
    {
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task, recordUndo: false);

        taskList.SetStatus(task.Id, TaskStatus.Done, recordUndo: false);
        Assert.NotNull(taskList.GetTodoTaskById(task.Id)!.CompletedAt);

        taskList.SetStatus(task.Id, TaskStatus.Pending, recordUndo: false);
        Assert.Null(taskList.GetTodoTaskById(task.Id)!.CompletedAt);
    }

    [Fact]
    public void SetStatus_FromDone_ToInProgress_ClearsCompletedAt()
    {
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task, recordUndo: false);

        taskList.SetStatus(task.Id, TaskStatus.Done, recordUndo: false);
        Assert.NotNull(taskList.GetTodoTaskById(task.Id)!.CompletedAt);

        taskList.SetStatus(task.Id, TaskStatus.InProgress, recordUndo: false);
        Assert.Null(taskList.GetTodoTaskById(task.Id)!.CompletedAt);
    }

    [Fact]
    public void SetStatus_ReCheck_GetsNewCompletedAt()
    {
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task, recordUndo: false);

        taskList.SetStatus(task.Id, TaskStatus.Done, recordUndo: false);
        var firstCompleted = taskList.GetTodoTaskById(task.Id)!.CompletedAt;

        // Small delay so timestamps differ
        Thread.Sleep(10);

        taskList.SetStatus(task.Id, TaskStatus.Pending, recordUndo: false);
        taskList.SetStatus(task.Id, TaskStatus.Done, recordUndo: false);
        var secondCompleted = taskList.GetTodoTaskById(task.Id)!.CompletedAt;

        Assert.NotNull(firstCompleted);
        Assert.NotNull(secondCompleted);
        Assert.True(secondCompleted > firstCompleted);
    }

    [Fact]
    public void GetSortedTasks_DoneGroupSortsByCompletedAtDesc()
    {
        var taskList = new TodoTaskList(_services, "tasks");

        var task1 = TodoTask.CreateTodoTask("first done", "tasks");
        var task2 = TodoTask.CreateTodoTask("second done", "tasks");
        var task3 = TodoTask.CreateTodoTask("third done", "tasks");

        taskList.AddTodoTask(task1, recordUndo: false);
        taskList.AddTodoTask(task2, recordUndo: false);
        taskList.AddTodoTask(task3, recordUndo: false);

        // Check in order: task1, then task2, then task3
        taskList.SetStatus(task1.Id, TaskStatus.Done, recordUndo: false);
        Thread.Sleep(10);
        taskList.SetStatus(task2.Id, TaskStatus.Done, recordUndo: false);
        Thread.Sleep(10);
        taskList.SetStatus(task3.Id, TaskStatus.Done, recordUndo: false);

        var sorted = taskList.GetSortedTasks();

        // All done, most recently completed first
        Assert.Equal(task3.Id, sorted[0].Id);
        Assert.Equal(task2.Id, sorted[1].Id);
        Assert.Equal(task1.Id, sorted[2].Id);
    }

    [Fact]
    public void GetSortedTasks_NullCompletedAt_SortsLast()
    {
        var taskList = new TodoTaskList(_services, "tasks");

        // Create a done task with CompletedAt set
        var taskWithTimestamp = TodoTask.CreateTodoTask("has timestamp", "tasks");
        taskList.AddTodoTask(taskWithTimestamp, recordUndo: false);
        taskList.SetStatus(taskWithTimestamp.Id, TaskStatus.Done, recordUndo: false);

        // Create a done task with NULL CompletedAt (simulate legacy by raw SQL)
        var legacyTask = TodoTask.CreateTodoTask("legacy done", "tasks");
        taskList.AddTodoTask(legacyTask, recordUndo: false);
        _services.Db.Execute(
            "UPDATE tasks SET status = 2, completed_at = NULL WHERE id = @id",
            ("@id", legacyTask.Id));

        var sorted = taskList.GetSortedTasks();

        // Task with timestamp first, legacy (NULL) last
        Assert.Equal(taskWithTimestamp.Id, sorted[0].Id);
        Assert.Equal(legacyTask.Id, sorted[1].Id);
    }

    [Fact]
    public void Migration_AddsColumn_BackfillsData()
    {
        // Use a file-based DB to test migration
        var testDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            var dbPath = Path.Combine(testDir, "tasker.db");

            // Create DB with old schema (no completed_at column)
            using (var oldDb = new TaskerDb(dbPath))
            {
                oldDb.Execute("""
                    CREATE TABLE IF NOT EXISTS lists (
                        name TEXT PRIMARY KEY,
                        is_collapsed INTEGER DEFAULT 0,
                        sort_order INTEGER DEFAULT 0
                    )
                    """);
                oldDb.Execute("INSERT INTO lists (name) VALUES ('tasks')");
                oldDb.Execute("""
                    CREATE TABLE IF NOT EXISTS tasks (
                        id TEXT PRIMARY KEY,
                        description TEXT NOT NULL,
                        status INTEGER DEFAULT 0,
                        created_at TEXT NOT NULL,
                        list_name TEXT NOT NULL,
                        due_date TEXT,
                        priority INTEGER,
                        tags TEXT,
                        is_trashed INTEGER DEFAULT 0,
                        sort_order INTEGER DEFAULT 0
                    )
                    """);
                oldDb.Execute("""
                    CREATE TABLE IF NOT EXISTS config (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    )
                    """);
                oldDb.Execute("""
                    CREATE TABLE IF NOT EXISTS undo_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        stack_type TEXT NOT NULL,
                        command_json TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    )
                    """);

                // Insert a pending task and a done task
                var now = DateTime.UtcNow.ToString("o");
                oldDb.Execute(
                    "INSERT INTO tasks (id, description, status, created_at, list_name) VALUES ('aaa', 'pending task', 0, @now, 'tasks')",
                    ("@now", now));
                oldDb.Execute(
                    "INSERT INTO tasks (id, description, status, created_at, list_name) VALUES ('bbb', 'done task', 2, @now, 'tasks')",
                    ("@now", now));

                // Add undo history to verify it gets cleared
                oldDb.Execute(
                    "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES ('undo', '{}', @now)",
                    ("@now", now));
            }

            // Reopen — EnsureCreated should run migration
            using var newDb = new TaskerDb(dbPath);
            newDb.EnsureCreated();

            // Verify column exists
            var columns = newDb.Query("PRAGMA table_info(tasks)", r => r.GetString(1), []);
            Assert.Contains("completed_at", columns);

            // Verify backfill: done task has completed_at = created_at
            var doneCompleted = newDb.ExecuteScalar<string>(
                "SELECT completed_at FROM tasks WHERE id = 'bbb'");
            var doneCreated = newDb.ExecuteScalar<string>(
                "SELECT created_at FROM tasks WHERE id = 'bbb'");
            Assert.Equal(doneCreated, doneCompleted);

            // Verify pending task has NULL completed_at
            var pendingCompleted = newDb.ExecuteScalar<string?>(
                "SELECT completed_at FROM tasks WHERE id = 'aaa'");
            Assert.Null(pendingCompleted);

            // Verify undo history was cleared
            var undoCount = newDb.ExecuteScalar<long>("SELECT COUNT(*) FROM undo_history");
            Assert.Equal(0, undoCount);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
