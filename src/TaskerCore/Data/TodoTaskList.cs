namespace TaskerCore.Data;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerCore.Results;
using TaskerCore.Undo.Commands;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class TodoTaskList
{
    private readonly TaskerServices _services;
    private readonly TaskerDb _db;
    private readonly string? listNameFilter;

    public TodoTaskList(TaskerServices services, string? listName = null)
    {
        _services = services;
        _db = services.Db;
        listNameFilter = listName;
    }

    /// <summary>
    /// Creates a TodoTaskList using the default services.
    /// </summary>
    public TodoTaskList(string? listName = null) : this(TaskerServices.Default, listName)
    {
    }

    // --- SQL → TodoTask mapping ---

    private static TodoTask ReadTask(SqliteDataReader reader)
    {
        var tagsJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        string[]? tags = null;
        if (tagsJson != null)
        {
            try { tags = JsonSerializer.Deserialize<string[]>(tagsJson); }
            catch { /* ignore malformed tags */ }
        }

        var dueDateStr = reader.IsDBNull(5) ? null : reader.GetString(5);
        DateOnly? dueDate = null;
        if (dueDateStr != null && DateOnly.TryParse(dueDateStr, out var parsed))
            dueDate = parsed;

        var priorityVal = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
        Priority? priority = priorityVal.HasValue ? (Priority)priorityVal.Value : null;

        var completedAtStr = reader.IsDBNull(8) ? null : reader.GetString(8);
        DateTime? completedAt = completedAtStr != null ? DateTime.Parse(completedAtStr) : null;

        return new TodoTask(
            Id: reader.GetString(0),
            Description: reader.GetString(1),
            Status: (TaskStatus)reader.GetInt32(2),
            CreatedAt: DateTime.Parse(reader.GetString(3)),
            ListName: reader.GetString(4),
            DueDate: dueDate,
            Priority: priority,
            Tags: tags is { Length: > 0 } ? tags : null,
            CompletedAt: completedAt
        );
    }

    private static string TaskSelectColumns =>
        "id, description, status, created_at, list_name, due_date, priority, tags, completed_at";

    // --- Query helpers ---

    private List<TodoTask> QueryTasks(string whereClause, params (string name, object? value)[] parameters)
    {
        return _db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks {whereClause}",
            ReadTask, parameters);
    }

    private TodoTask? QuerySingleTask(string whereClause, params (string name, object? value)[] parameters)
    {
        return _db.QuerySingle(
            $"SELECT {TaskSelectColumns} FROM tasks {whereClause}",
            ReadTask, parameters);
    }

    // --- Filter helpers ---

    private List<TodoTask> GetFilteredTasks()
    {
        if (listNameFilter == null)
            return QueryTasks("WHERE is_trashed = 0 ORDER BY sort_order DESC");
        return QueryTasks("WHERE is_trashed = 0 AND list_name = @list ORDER BY sort_order DESC", ("@list", listNameFilter));
    }

    private List<TodoTask> GetFilteredTrash()
    {
        if (listNameFilter == null)
            return QueryTasks("WHERE is_trashed = 1 ORDER BY sort_order DESC");
        return QueryTasks("WHERE is_trashed = 1 AND list_name = @list ORDER BY sort_order DESC", ("@list", listNameFilter));
    }

    // Public accessor for TUI and GUI
    public List<TodoTask> GetAllTasks() => GetFilteredTasks();

    /// <summary>
    /// Gets tasks sorted for display: in-progress first, then pending, then done.
    /// Within each group: by priority, then due date.
    /// </summary>
    public List<TodoTask> GetSortedTasks(TaskStatus? filterStatus = null, bool? filterChecked = null, Priority? filterPriority = null, bool? filterOverdue = null)
    {
        var tasks = GetFilteredTasks();
        var today = DateOnly.FromDateTime(DateTime.Today);

        IEnumerable<TodoTask> filteredTasks = tasks;

        // Explicit status filter
        if (filterStatus.HasValue)
        {
            filteredTasks = filteredTasks.Where(td => td.Status == filterStatus.Value);
        }
        // Backward compat: -c = done, -u = pending + in-progress
        else if (filterChecked.HasValue)
        {
            filteredTasks = filterChecked.Value
                ? filteredTasks.Where(td => td.Status == TaskStatus.Done)
                : filteredTasks.Where(td => td.Status != TaskStatus.Done);
        }

        if (filterPriority.HasValue)
            filteredTasks = filteredTasks.Where(t => t.Priority == filterPriority.Value);

        if (filterOverdue == true)
            filteredTasks = filteredTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value < today);

        // Active tasks: priority → due date → created_at
        var active = filteredTasks
            .Where(t => t.Status != TaskStatus.Done)
            .OrderBy(t => StatusSortOrder(t.Status))
            .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
            .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        // Done tasks: purely by completed_at DESC (NULL sorts last)
        var done = filteredTasks
            .Where(t => t.Status == TaskStatus.Done)
            .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
            .ToList();

        return [..active, ..done];
    }

    /// <summary>
    /// Sort order for status: in-progress (0) first, pending (1), done (2) last.
    /// </summary>
    private static int StatusSortOrder(TaskStatus status) => status switch
    {
        TaskStatus.InProgress => 0,
        TaskStatus.Pending => 1,
        TaskStatus.Done => 2,
        _ => 1
    };

    private static int GetDueDateSortOrder(DateOnly? dueDate, DateOnly today)
    {
        if (!dueDate.HasValue) return 99;
        var days = dueDate.Value.DayNumber - today.DayNumber;
        return days < 0 ? 0 : days;
    }

    // --- Write helpers ---

    private void InsertTask(TodoTask task, bool isTrashed = false)
    {
        // Get the current max sort_order for the list to insert at top
        var maxOrder = _db.ExecuteScalar<long?>(
            "SELECT MAX(sort_order) FROM tasks WHERE list_name = @list AND is_trashed = @trashed",
            ("@list", task.ListName), ("@trashed", isTrashed ? 1 : 0)) ?? -1;

        var tagsJson = task.Tags is { Length: > 0 }
            ? JsonSerializer.Serialize(task.Tags)
            : null;

        _db.Execute("""
            INSERT INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at)
            VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, @trashed, @order, @completed)
            """,
            ("@id", task.Id),
            ("@desc", task.Description),
            ("@status", (int)task.Status),
            ("@created", task.CreatedAt.ToString("o")),
            ("@list", task.ListName),
            ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
            ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
            ("@tags", (object?)tagsJson ?? DBNull.Value),
            ("@trashed", isTrashed ? 1 : 0),
            ("@order", maxOrder + 1),
            ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value));
    }

    private void UpdateTask(TodoTask task)
    {
        var tagsJson = task.Tags is { Length: > 0 }
            ? JsonSerializer.Serialize(task.Tags)
            : null;

        _db.Execute("""
            UPDATE tasks SET description = @desc, status = @status, list_name = @list,
                due_date = @due, priority = @priority, tags = @tags, completed_at = @completed
            WHERE id = @id
            """,
            ("@id", task.Id),
            ("@desc", task.Description),
            ("@status", (int)task.Status),
            ("@list", task.ListName),
            ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
            ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
            ("@tags", (object?)tagsJson ?? DBNull.Value),
            ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value));
    }

    private void DeleteTaskById(string taskId)
    {
        _db.Execute("DELETE FROM tasks WHERE id = @id", ("@id", taskId));
    }

    private void CreateBackup()
    {
        try { _services.Backup.CreateBackup(); }
        catch { /* Ignore backup failures */ }
    }

    // --- Public operations ---

    public void AddTodoTask(TodoTask todoTask, bool recordUndo = true)
    {
        if (recordUndo)
        {
            var cmd = new AddTaskCommand { Task = todoTask };
            _services.Undo.RecordCommand(cmd);
        }

        // Ensure the list exists
        EnsureListExists(todoTask.ListName);

        CreateBackup();
        InsertTask(todoTask);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }
    }

    private void EnsureListExists(string listName)
    {
        _db.Execute(
            "INSERT OR IGNORE INTO lists (name, sort_order) VALUES (@name, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM lists))",
            ("@name", listName));
    }

    public TodoTask? GetTodoTaskById(string taskId)
    {
        // Always search globally by ID (not trashed)
        return QuerySingleTask("WHERE id = @id AND is_trashed = 0", ("@id", taskId));
    }

    public TaskResult SetStatus(string taskId, TaskStatus status, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (todoTask.Status == status)
        {
            return new TaskResult.NoChange($"Task {taskId} is already {StatusLabel(status)}");
        }

        if (recordUndo)
        {
            var cmd = new SetStatusCommand
            {
                TaskId = taskId,
                OldStatus = todoTask.Status,
                NewStatus = status
            };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        var updatedTask = todoTask.WithStatus(status);
        UpdateTask(updatedTask);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Set {taskId} to {StatusLabel(status)}");
    }

    /// <summary>
    /// Backward compat: check = set to Done.
    /// </summary>
    public TaskResult CheckTask(string taskId, bool recordUndo = true) =>
        SetStatus(taskId, TaskStatus.Done, recordUndo);

    /// <summary>
    /// Backward compat: uncheck = set to Pending.
    /// </summary>
    public TaskResult UncheckTask(string taskId, bool recordUndo = true) =>
        SetStatus(taskId, TaskStatus.Pending, recordUndo);

    private static string StatusLabel(TaskStatus status) => status switch
    {
        TaskStatus.Pending => "pending",
        TaskStatus.InProgress => "in-progress",
        TaskStatus.Done => "done",
        _ => status.ToString()
    };

    /// <summary>
    /// Moves a task to the top of its list's sort order.
    /// </summary>
    private void BumpSortOrder(string taskId, string listName)
    {
        var maxOrder = _db.ExecuteScalar<long?>(
            "SELECT MAX(sort_order) FROM tasks WHERE list_name = @list AND is_trashed = 0",
            ("@list", listName)) ?? 0;
        _db.Execute("UPDATE tasks SET sort_order = @order WHERE id = @id",
            ("@order", maxOrder + 1), ("@id", taskId));
    }

    public TaskResult DeleteTask(string taskId, bool save = true, bool moveToTrash = true, bool recordUndo = true)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo && moveToTrash)
        {
            var cmd = new DeleteTaskCommand { DeletedTask = task };
            _services.Undo.RecordCommand(cmd);
        }

        if (save)
            CreateBackup();

        if (moveToTrash)
        {
            // Set is_trashed flag
            _db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = @id", ("@id", taskId));
        }
        else
        {
            // Actually delete (used internally by check/uncheck re-insertion — not needed with SQLite update approach)
            DeleteTaskById(taskId);
        }

        if (save && recordUndo && moveToTrash)
        {
            _services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Deleted task: {taskId}");
    }

    public BatchTaskResult DeleteTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            _services.Undo.BeginBatch($"Delete {taskIds.Length} tasks");
        }

        CreateBackup();
        var results = new List<TaskResult>();

        using var transaction = _db.BeginTransaction();
        try
        {
            foreach (var taskId in taskIds)
            {
                var task = GetTodoTaskById(taskId);
                if (task == null)
                {
                    results.Add(new TaskResult.NotFound(taskId));
                    continue;
                }

                if (recordUndo)
                {
                    var cmd = new DeleteTaskCommand { DeletedTask = task };
                    _services.Undo.RecordCommand(cmd);
                }

                _db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = @id", ("@id", taskId));
                results.Add(new TaskResult.Success($"Deleted task: {taskId}"));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        if (recordUndo)
        {
            _services.Undo.EndBatch();
            _services.Undo.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public BatchTaskResult SetStatuses(string[] taskIds, TaskStatus status, bool recordUndo = true)
    {
        if (recordUndo)
        {
            _services.Undo.BeginBatch($"Set {taskIds.Length} tasks to {StatusLabel(status)}");
        }

        CreateBackup();
        var results = new List<TaskResult>();

        using var transaction = _db.BeginTransaction();
        try
        {
            foreach (var taskId in taskIds)
            {
                var todoTask = GetTodoTaskById(taskId);
                if (todoTask == null)
                {
                    results.Add(new TaskResult.NotFound(taskId));
                    continue;
                }

                if (todoTask.Status == status)
                {
                    results.Add(new TaskResult.NoChange($"Task {taskId} is already {StatusLabel(status)}"));
                    continue;
                }

                if (recordUndo)
                {
                    var cmd = new SetStatusCommand
                    {
                        TaskId = taskId,
                        OldStatus = todoTask.Status,
                        NewStatus = status
                    };
                    _services.Undo.RecordCommand(cmd);
                }

                var updatedTask = todoTask.WithStatus(status);
                UpdateTask(updatedTask);
                results.Add(new TaskResult.Success($"Set {taskId} to {StatusLabel(status)}"));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        if (recordUndo)
        {
            _services.Undo.EndBatch();
            _services.Undo.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    /// <summary>
    /// Backward compat: batch check = set to Done.
    /// </summary>
    public BatchTaskResult CheckTasks(string[] taskIds, bool recordUndo = true) =>
        SetStatuses(taskIds, TaskStatus.Done, recordUndo);

    /// <summary>
    /// Backward compat: batch uncheck = set to Pending.
    /// </summary>
    public BatchTaskResult UncheckTasks(string[] taskIds, bool recordUndo = true) =>
        SetStatuses(taskIds, TaskStatus.Pending, recordUndo);

    public int ClearTasks(bool recordUndo = true)
    {
        var tasksToMove = GetFilteredTasks();

        if (recordUndo && tasksToMove.Count > 0)
        {
            var cmd = new ClearTasksCommand { ListName = listNameFilter, ClearedTasks = tasksToMove.ToArray() };
            _services.Undo.RecordCommand(cmd);
        }

        if (tasksToMove.Count == 0)
            return 0;

        CreateBackup();

        using var transaction = _db.BeginTransaction();
        try
        {
            foreach (var task in tasksToMove)
            {
                _db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = @id", ("@id", task.Id));
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        if (recordUndo && tasksToMove.Count > 0)
        {
            _services.Undo.SaveHistory();
        }

        return tasksToMove.Count;
    }

    public TaskResult RenameTask(string taskId, string newDescription, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo)
        {
            var cmd = new RenameTaskCommand
            {
                TaskId = taskId,
                OldDescription = todoTask.Description,
                NewDescription = newDescription
            };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        var renamedTask = todoTask.Rename(newDescription);
        UpdateTask(renamedTask);
        BumpSortOrder(taskId, renamedTask.ListName);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Renamed task: {taskId}");
    }

    public TaskResult MoveTask(string taskId, string targetList, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (todoTask.ListName == targetList)
        {
            return new TaskResult.NoChange($"Task is already in '{targetList}'");
        }

        var sourceList = todoTask.ListName;

        if (recordUndo)
        {
            var cmd = new MoveTaskCommand
            {
                TaskId = taskId,
                SourceList = sourceList,
                TargetList = targetList
            };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        EnsureListExists(targetList);
        var movedTask = todoTask.MoveToList(targetList);
        UpdateTask(movedTask);
        BumpSortOrder(taskId, targetList);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Moved task {taskId} from '{sourceList}' to '{targetList}'");
    }

    public TaskResult SetTaskDueDate(string taskId, DateOnly? dueDate, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo)
        {
            var cmd = new TaskMetadataChangedCommand
            {
                TaskId = taskId,
                OldDueDate = todoTask.DueDate,
                NewDueDate = dueDate,
                OldPriority = todoTask.Priority,
                NewPriority = todoTask.Priority
            };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        var updatedTask = dueDate.HasValue ? todoTask.SetDueDate(dueDate.Value) : todoTask.ClearDueDate();

        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags);
        updatedTask = updatedTask.Rename(syncedDescription);

        UpdateTask(updatedTask);
        BumpSortOrder(taskId, updatedTask.ListName);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        var message = dueDate.HasValue
            ? $"Set due date for {taskId}: {dueDate:MMM d}"
            : $"Cleared due date for {taskId}";
        return new TaskResult.Success(message);
    }

    public TaskResult SetTaskPriority(string taskId, Priority? priority, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo)
        {
            var cmd = new TaskMetadataChangedCommand
            {
                TaskId = taskId,
                OldDueDate = todoTask.DueDate,
                NewDueDate = todoTask.DueDate,
                OldPriority = todoTask.Priority,
                NewPriority = priority
            };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        var updatedTask = priority.HasValue ? todoTask.SetPriority(priority.Value) : todoTask.ClearPriority();

        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags);
        updatedTask = updatedTask.Rename(syncedDescription);

        UpdateTask(updatedTask);
        BumpSortOrder(taskId, updatedTask.ListName);

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        var message = priority.HasValue
            ? $"Set priority for {taskId}: {priority}"
            : $"Cleared priority for {taskId}";
        return new TaskResult.Success(message);
    }

    // Trash methods

    public List<TodoTask> GetTrash()
    {
        return GetFilteredTrash();
    }

    public TaskResult RestoreFromTrash(string taskId)
    {
        var task = QuerySingleTask("WHERE id = @id AND is_trashed = 1", ("@id", taskId));
        if (task == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        CreateBackup();
        _db.Execute("UPDATE tasks SET is_trashed = 0 WHERE id = @id", ("@id", taskId));

        return new TaskResult.Success($"Restored task: {taskId}");
    }

    public int ClearTrash()
    {
        var trashToClear = GetFilteredTrash();
        var count = trashToClear.Count;

        if (count == 0) return 0;

        CreateBackup();

        if (listNameFilter == null)
        {
            _db.Execute("DELETE FROM tasks WHERE is_trashed = 1");
        }
        else
        {
            _db.Execute("DELETE FROM tasks WHERE is_trashed = 1 AND list_name = @list",
                ("@list", listNameFilter));
        }

        return count;
    }

    public TaskStats GetStats()
    {
        var tasks = GetFilteredTasks();
        var trash = GetFilteredTrash();
        return new TaskStats
        {
            Total = tasks.Count,
            Pending = tasks.Count(t => t.Status == TaskStatus.Pending),
            InProgress = tasks.Count(t => t.Status == TaskStatus.InProgress),
            Done = tasks.Count(t => t.Status == TaskStatus.Done),
            Trash = trash.Count
        };
    }

    // --- Static methods for list management ---

    public static string[] GetAllListNames(TaskerServices services)
    {
        var names = services.Db.Query(
            "SELECT name FROM lists ORDER BY sort_order",
            reader => reader.GetString(0));

        if (names.Count == 0 || !names.Contains(ListManager.DefaultListName))
        {
            return [ListManager.DefaultListName, .. names.Where(n => n != ListManager.DefaultListName)];
        }

        return names.ToArray();
    }

    public static string[] GetAllListNames() => GetAllListNames(TaskerServices.Default);

    public static bool ListHasTasks(TaskerServices services, string listName)
    {
        var count = services.Db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM tasks WHERE list_name = @list AND is_trashed = 0",
            ("@list", listName));
        return count > 0;
    }

    public static bool ListHasTasks(string listName) => ListHasTasks(TaskerServices.Default, listName);

    public static bool ListExists(TaskerServices services, string listName)
    {
        if (listName == ListManager.DefaultListName) return true;

        var count = services.Db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM lists WHERE name = @name",
            ("@name", listName));
        return count > 0;
    }

    public static bool ListExists(string listName) => ListExists(TaskerServices.Default, listName);

    public static void CreateList(TaskerServices services, string listName)
    {
        var maxOrder = services.Db.ExecuteScalar<long?>(
            "SELECT MAX(sort_order) FROM lists") ?? -1;

        services.Db.Execute(
            "INSERT INTO lists (name, sort_order) VALUES (@name, @order)",
            ("@name", listName), ("@order", maxOrder + 1));
    }

    public static void CreateList(string listName) => CreateList(TaskerServices.Default, listName);

    public static void DeleteList(TaskerServices services, string listName)
    {
        // CASCADE will delete all tasks in the list
        services.Db.Execute("DELETE FROM lists WHERE name = @name", ("@name", listName));
    }

    public static void DeleteList(string listName) => DeleteList(TaskerServices.Default, listName);

    public static TaskList GetListByName(TaskerServices services, string listName)
    {
        var exists = services.Db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM lists WHERE name = @name",
            ("@name", listName));

        if (exists == 0)
            throw new Exceptions.ListNotFoundException(listName);

        var isCollapsed = services.Db.ExecuteScalar<long>(
            "SELECT is_collapsed FROM lists WHERE name = @name",
            ("@name", listName));

        var tasks = services.Db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks WHERE list_name = @list AND is_trashed = 0",
            ReadTask, ("@list", listName));

        return new TaskList(listName, tasks.ToArray(), isCollapsed != 0);
    }

    public static TaskList GetListByName(string listName) => GetListByName(TaskerServices.Default, listName);

    public static TaskList? GetTrashedListByName(TaskerServices services, string listName)
    {
        var tasks = services.Db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks WHERE list_name = @list AND is_trashed = 1",
            ReadTask, ("@list", listName));

        return tasks.Count > 0 ? new TaskList(listName, tasks.ToArray()) : null;
    }

    public static TaskList? GetTrashedListByName(string listName) => GetTrashedListByName(TaskerServices.Default, listName);

    public static int GetListIndex(TaskerServices services, string listName)
    {
        var names = GetAllListNames(services);
        return Array.IndexOf(names, listName);
    }

    public static int GetListIndex(string listName) => GetListIndex(TaskerServices.Default, listName);

    public static void RestoreList(TaskerServices services, TaskList activeList, TaskList? trashedList, int originalIndex)
    {
        var db = services.Db;

        using var transaction = db.BeginTransaction();
        try
        {
            // Determine sort_order for the restored list
            var allNames = GetAllListNames(services);
            var sortOrder = originalIndex;

            // Insert the list
            db.Execute("INSERT OR IGNORE INTO lists (name, is_collapsed, sort_order) VALUES (@name, @collapsed, @order)",
                ("@name", activeList.ListName),
                ("@collapsed", activeList.IsCollapsed ? 1 : 0),
                ("@order", sortOrder));

            // Insert active tasks
            foreach (var task in activeList.Tasks)
            {
                var tagsJson = task.Tags is { Length: > 0 } ? JsonSerializer.Serialize(task.Tags) : null;
                db.Execute("""
                    INSERT OR IGNORE INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at)
                    VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, 0, @order, @completed)
                    """,
                    ("@id", task.Id),
                    ("@desc", task.Description),
                    ("@status", (int)task.Status),
                    ("@created", task.CreatedAt.ToString("o")),
                    ("@list", task.ListName),
                    ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
                    ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
                    ("@tags", (object?)tagsJson ?? DBNull.Value),
                    ("@order", 0),
                    ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value));
            }

            // Insert trashed tasks
            if (trashedList != null)
            {
                foreach (var task in trashedList.Tasks)
                {
                    var tagsJson = task.Tags is { Length: > 0 } ? JsonSerializer.Serialize(task.Tags) : null;
                    db.Execute("""
                        INSERT OR IGNORE INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at)
                        VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, 1, @order, @completed)
                        """,
                        ("@id", task.Id),
                        ("@desc", task.Description),
                        ("@status", (int)task.Status),
                        ("@created", task.CreatedAt.ToString("o")),
                        ("@list", task.ListName),
                        ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
                        ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
                        ("@tags", (object?)tagsJson ?? DBNull.Value),
                        ("@order", 0),
                        ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value));
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static void RestoreList(TaskList activeList, TaskList? trashedList, int originalIndex) =>
        RestoreList(TaskerServices.Default, activeList, trashedList, originalIndex);

    public static void RenameList(TaskerServices services, string oldName, string newName)
    {
        // ON UPDATE CASCADE handles updating list_name on tasks
        services.Db.Execute("UPDATE lists SET name = @new WHERE name = @old",
            ("@new", newName), ("@old", oldName));
    }

    public static void RenameList(string oldName, string newName) => RenameList(TaskerServices.Default, oldName, newName);

    public static bool IsListCollapsed(TaskerServices services, string listName)
    {
        var result = services.Db.ExecuteScalar<long?>(
            "SELECT is_collapsed FROM lists WHERE name = @name",
            ("@name", listName));
        return result == 1;
    }

    public static bool IsListCollapsed(string listName) => IsListCollapsed(TaskerServices.Default, listName);

    public static void SetListCollapsed(TaskerServices services, string listName, bool collapsed)
    {
        services.Db.Execute("UPDATE lists SET is_collapsed = @collapsed WHERE name = @name",
            ("@collapsed", collapsed ? 1 : 0), ("@name", listName));
    }

    public static void SetListCollapsed(string listName, bool collapsed) =>
        SetListCollapsed(TaskerServices.Default, listName, collapsed);

    public static void ReorderTask(TaskerServices services, string taskId, int newIndex, bool recordUndo = true)
    {
        var db = services.Db;

        // Find the task and its list
        var task = db.QuerySingle(
            "SELECT id, list_name, sort_order FROM tasks WHERE id = @id AND is_trashed = 0",
            reader => new { Id = reader.GetString(0), ListName = reader.GetString(1), SortOrder = reader.GetInt32(2) },
            ("@id", taskId));

        if (task == null) return;

        // Get all tasks in this list ordered by sort_order DESC (matching display order)
        var tasksInList = db.Query(
            "SELECT id FROM tasks WHERE list_name = @list AND is_trashed = 0 ORDER BY sort_order DESC",
            reader => reader.GetString(0),
            ("@list", task.ListName));

        var currentIndex = tasksInList.IndexOf(taskId);
        if (currentIndex < 0) return;

        var clampedNewIndex = Math.Max(0, Math.Min(newIndex, tasksInList.Count - 1));
        if (currentIndex == clampedNewIndex) return;

        if (recordUndo)
        {
            services.Undo.RecordCommand(new Undo.Commands.ReorderTaskCommand
            {
                TaskId = taskId,
                ListName = task.ListName,
                OldIndex = currentIndex,
                NewIndex = clampedNewIndex
            });
        }

        // Reorder: remove from old position, insert at new
        tasksInList.RemoveAt(currentIndex);
        tasksInList.Insert(clampedNewIndex, taskId);

        // Update sort_order: index 0 (first in display) gets highest sort_order
        using var transaction = db.BeginTransaction();
        try
        {
            for (var i = 0; i < tasksInList.Count; i++)
            {
                db.Execute("UPDATE tasks SET sort_order = @order WHERE id = @id",
                    ("@order", tasksInList.Count - 1 - i), ("@id", tasksInList[i]));
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        if (recordUndo)
        {
            services.Undo.SaveHistory();
        }
    }

    public static void ReorderTask(string taskId, int newIndex, bool recordUndo = true) =>
        ReorderTask(TaskerServices.Default, taskId, newIndex, recordUndo);

    public static void ReorderList(TaskerServices services, string listName, int newIndex, bool recordUndo = true)
    {
        var db = services.Db;

        var allNames = GetAllListNames(services).ToList();
        var currentIndex = allNames.IndexOf(listName);
        if (currentIndex < 0) return;

        var clampedNewIndex = Math.Max(0, Math.Min(newIndex, allNames.Count - 1));
        if (currentIndex == clampedNewIndex) return;

        if (recordUndo)
        {
            services.Undo.RecordCommand(new Undo.Commands.ReorderListCommand
            {
                ListName = listName,
                OldIndex = currentIndex,
                NewIndex = clampedNewIndex
            });
        }

        // Reorder
        allNames.RemoveAt(currentIndex);
        allNames.Insert(clampedNewIndex, listName);

        // Update sort_order for all lists
        using var transaction = db.BeginTransaction();
        try
        {
            for (var i = 0; i < allNames.Count; i++)
            {
                db.Execute("UPDATE lists SET sort_order = @order WHERE name = @name",
                    ("@order", i), ("@name", allNames[i]));
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        if (recordUndo)
        {
            services.Undo.SaveHistory();
        }
    }

    public static void ReorderList(string listName, int newIndex, bool recordUndo = true) =>
        ReorderList(TaskerServices.Default, listName, newIndex, recordUndo);
}
