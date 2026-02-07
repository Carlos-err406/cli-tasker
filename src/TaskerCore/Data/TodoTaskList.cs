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

        var parentId = reader.IsDBNull(9) ? null : reader.GetString(9);

        return new TodoTask(
            Id: reader.GetString(0),
            Description: reader.GetString(1),
            Status: (TaskStatus)reader.GetInt32(2),
            CreatedAt: DateTime.Parse(reader.GetString(3)),
            ListName: reader.GetString(4),
            DueDate: dueDate,
            Priority: priority,
            Tags: tags is { Length: > 0 } ? tags : null,
            CompletedAt: completedAt,
            ParentId: parentId
        );
    }

    private static string TaskSelectColumns =>
        "id, description, status, created_at, list_name, due_date, priority, tags, completed_at, parent_id";

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
            INSERT INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at, parent_id)
            VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, @trashed, @order, @completed, @parent)
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
            ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value),
            ("@parent", (object?)task.ParentId ?? DBNull.Value));
    }

    private void UpdateTask(TodoTask task)
    {
        var tagsJson = task.Tags is { Length: > 0 }
            ? JsonSerializer.Serialize(task.Tags)
            : null;

        _db.Execute("""
            UPDATE tasks SET description = @desc, status = @status, list_name = @list,
                due_date = @due, priority = @priority, tags = @tags, completed_at = @completed,
                parent_id = @parent
            WHERE id = @id
            """,
            ("@id", task.Id),
            ("@desc", task.Description),
            ("@status", (int)task.Status),
            ("@list", task.ListName),
            ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
            ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
            ("@tags", (object?)tagsJson ?? DBNull.Value),
            ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value),
            ("@parent", (object?)task.ParentId ?? DBNull.Value));
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

    public record AddResult(TodoTask Task, List<string> Warnings);

    public AddResult AddTodoTask(TodoTask todoTask, bool recordUndo = true)
    {
        var warnings = new List<string>();
        var parsed = TaskDescriptionParser.Parse(todoTask.Description);
        var task = todoTask;

        // Validate parent reference
        if (task.ParentId != null)
        {
            var parent = GetTodoTaskById(task.ParentId);
            if (parent == null)
            {
                warnings.Add($"Parent task ({task.ParentId}) not found, created as top-level task");
                task = task.ClearParent();
            }
            else if (task.ListName != parent.ListName)
            {
                // Override list to match parent
                warnings.Add($"Subtask moved to list '{parent.ListName}' to match parent ({task.ParentId})");
                task = task with { ListName = parent.ListName };
            }
        }

        if (recordUndo)
        {
            var cmd = new AddTaskCommand { Task = task };
            _services.Undo.RecordCommand(cmd);
        }

        // Ensure the list exists
        EnsureListExists(task.ListName);

        CreateBackup();
        InsertTask(task);

        // Process blocking references
        if (parsed.BlocksIds is { Length: > 0 })
        {
            foreach (var blockedId in parsed.BlocksIds)
            {
                var blocked = GetTodoTaskById(blockedId);
                if (blocked == null)
                {
                    warnings.Add($"Blocked task ({blockedId}) not found, skipping blocker relationship");
                    continue;
                }
                if (blockedId == task.Id)
                {
                    warnings.Add("A task cannot block itself, skipping");
                    continue;
                }
                if (HasCircularBlocking(task.Id, blockedId))
                {
                    warnings.Add($"Circular dependency with ({blockedId}), skipping blocker relationship");
                    continue;
                }
                _db.Execute("INSERT INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
                    ("@blocker", task.Id), ("@blocked", blockedId));
                // Sync inverse marker on the blocked task
                AddInverseMarker(blockedId, task.Id, isSubtask: false);
            }
        }

        // Sync inverse marker on the parent (if child has ^parent)
        if (task.ParentId != null)
        {
            AddInverseMarker(task.ParentId, task.Id, isSubtask: true);
        }

        // Process inverse parent markers: -^abc means "abc is my subtask"
        if (parsed.HasSubtaskIds is { Length: > 0 })
        {
            foreach (var subtaskId in parsed.HasSubtaskIds)
            {
                var subtask = GetTodoTaskById(subtaskId);
                if (subtask == null)
                {
                    warnings.Add($"Subtask ({subtaskId}) not found, skipping inverse parent relationship");
                    continue;
                }
                if (subtaskId == task.Id)
                {
                    warnings.Add("A task cannot be its own subtask, skipping");
                    continue;
                }
                if (subtask.ListName != task.ListName)
                {
                    warnings.Add($"Subtask ({subtaskId}) is in a different list, skipping inverse parent relationship");
                    continue;
                }
                var descendants = GetAllDescendantIds(subtaskId);
                if (descendants.Contains(task.Id))
                {
                    warnings.Add($"Circular reference with ({subtaskId}), skipping inverse parent relationship");
                    continue;
                }
                // Set the subtask's parent to this task
                _db.Execute("UPDATE tasks SET parent_id = @parent WHERE id = @id",
                    ("@parent", task.Id), ("@id", subtaskId));
                // Add ^thisTask to the subtask's metadata
                var subParsed = TaskDescriptionParser.Parse(subtask.Description);
                var subSynced = TaskDescriptionParser.SyncMetadataToDescription(
                    subtask.Description, subtask.Priority, subtask.DueDate, subtask.Tags,
                    task.Id, subParsed.BlocksIds, subParsed.HasSubtaskIds, subParsed.BlockedByIds);
                if (subSynced != subtask.Description)
                    _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                        ("@desc", subSynced), ("@id", subtaskId));
            }
        }

        // Process inverse blocker markers: -!abc means "abc blocks me"
        if (parsed.BlockedByIds is { Length: > 0 })
        {
            foreach (var blockerId in parsed.BlockedByIds)
            {
                var blocker = GetTodoTaskById(blockerId);
                if (blocker == null)
                {
                    warnings.Add($"Blocker task ({blockerId}) not found, skipping inverse blocker relationship");
                    continue;
                }
                if (blockerId == task.Id)
                {
                    warnings.Add("A task cannot block itself, skipping");
                    continue;
                }
                if (HasCircularBlocking(blockerId, task.Id))
                {
                    warnings.Add($"Circular dependency with ({blockerId}), skipping inverse blocker relationship");
                    continue;
                }
                // Insert dependency: blocker blocks this task
                _db.Execute("INSERT OR IGNORE INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
                    ("@blocker", blockerId), ("@blocked", task.Id));
                // Add !thisTask to the blocker's metadata
                var blockerParsed = TaskDescriptionParser.Parse(blocker.Description);
                var blockerBlocksIds = blockerParsed.BlocksIds?.ToList() ?? [];
                if (!blockerBlocksIds.Contains(task.Id))
                {
                    blockerBlocksIds.Add(task.Id);
                    var blockerSynced = TaskDescriptionParser.SyncMetadataToDescription(
                        blocker.Description, blocker.Priority, blocker.DueDate, blocker.Tags,
                        blockerParsed.ParentId, blockerBlocksIds.ToArray(),
                        blockerParsed.HasSubtaskIds, blockerParsed.BlockedByIds);
                    if (blockerSynced != blocker.Description)
                        _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                            ("@desc", blockerSynced), ("@id", blockerId));
                }
            }
        }

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new AddResult(task, warnings);
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

        // Cascade-check: when marking a parent as Done, also mark all non-Done descendants
        var cascadeIds = new List<string>();
        if (status == TaskStatus.Done)
        {
            var descendantIds = GetAllDescendantIds(taskId);
            foreach (var descId in descendantIds)
            {
                var desc = GetTodoTaskById(descId);
                if (desc != null && desc.Status != TaskStatus.Done)
                    cascadeIds.Add(descId);
            }
        }

        var hasCascade = cascadeIds.Count > 0;

        if (recordUndo)
        {
            if (hasCascade) _services.Undo.BeginBatch($"Set {taskId} and {cascadeIds.Count} subtask(s) to {StatusLabel(status)}");

            _services.Undo.RecordCommand(new SetStatusCommand
            {
                TaskId = taskId,
                OldStatus = todoTask.Status,
                NewStatus = status
            });

            foreach (var descId in cascadeIds)
            {
                var desc = GetTodoTaskById(descId)!;
                _services.Undo.RecordCommand(new SetStatusCommand
                {
                    TaskId = descId,
                    OldStatus = desc.Status,
                    NewStatus = status
                });
            }
        }

        CreateBackup();
        var updatedTask = todoTask.WithStatus(status);
        UpdateTask(updatedTask);

        // Apply cascade
        foreach (var descId in cascadeIds)
        {
            var desc = GetTodoTaskById(descId)!;
            UpdateTask(desc.WithStatus(status));
        }

        if (recordUndo)
        {
            if (hasCascade) _services.Undo.EndBatch();
            _services.Undo.SaveHistory();
        }

        var msg = hasCascade
            ? $"Set {taskId} and {cascadeIds.Count} subtask(s) to {StatusLabel(status)}"
            : $"Set {taskId} to {StatusLabel(status)}";
        return new TaskResult.Success(msg);
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

        // Collect descendants for cascade trash
        var descendantIds = moveToTrash ? GetAllDescendantIds(taskId) : [];
        var hasCascade = descendantIds.Count > 0;

        if (recordUndo && moveToTrash)
        {
            if (hasCascade) _services.Undo.BeginBatch($"Delete ({taskId}) and {descendantIds.Count} subtask(s)");
            var cmd = new DeleteTaskCommand { DeletedTask = task };
            _services.Undo.RecordCommand(cmd);

            // Record each descendant for undo
            foreach (var descId in descendantIds)
            {
                var descTask = GetTodoTaskById(descId);
                if (descTask != null)
                    _services.Undo.RecordCommand(new DeleteTaskCommand { DeletedTask = descTask });
            }
        }

        if (save)
            CreateBackup();

        if (moveToTrash)
        {
            // Set is_trashed flag on task and all descendants
            _db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = @id", ("@id", taskId));
            foreach (var descId in descendantIds)
                _db.Execute("UPDATE tasks SET is_trashed = 1 WHERE id = @id", ("@id", descId));
        }
        else
        {
            DeleteTaskById(taskId);
        }

        if (save && recordUndo && moveToTrash)
        {
            if (hasCascade) _services.Undo.EndBatch();
            _services.Undo.SaveHistory();
        }

        var msg = hasCascade
            ? $"Deleted task ({taskId}) and {descendantIds.Count} subtask(s)"
            : $"Deleted task: {taskId}";
        return new TaskResult.Success(msg);
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

        var oldParsed = TaskDescriptionParser.Parse(todoTask.Description);
        var newParsed = TaskDescriptionParser.Parse(newDescription.Trim());

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

        // Validate new parent if metadata line changed it
        if (newParsed.LastLineIsMetadataOnly && newParsed.ParentId != null)
        {
            var parent = GetTodoTaskById(newParsed.ParentId);
            if (parent == null || parent.Id == taskId)
            {
                renamedTask = renamedTask.ClearParent();
            }
        }

        UpdateTask(renamedTask);
        BumpSortOrder(taskId, renamedTask.ListName);

        if (newParsed.LastLineIsMetadataOnly)
        {
            // Sync blocking relationships (forward: !abc)
            var currentBlocksIds = GetBlocksIds(taskId).ToArray();
            SyncBlockingRelationships(taskId, currentBlocksIds, newParsed.BlocksIds);

            // Sync inverse markers on blocked tasks for forward blocker changes
            var oldForwardBlocks = new HashSet<string>(oldParsed.BlocksIds ?? []);
            var newForwardBlocks = new HashSet<string>(newParsed.BlocksIds ?? []);
            foreach (var addedBlockedId in newForwardBlocks.Except(oldForwardBlocks))
                AddInverseMarker(addedBlockedId, taskId, isSubtask: false);
            foreach (var removedBlockedId in oldForwardBlocks.Except(newForwardBlocks))
                RemoveInverseMarker(removedBlockedId, taskId, isSubtask: false);

            // Sync parent change: update inverse markers on old/new parent
            var oldParentId = oldParsed.ParentId;
            var newParentId = renamedTask.ParentId; // Use the validated parent
            if (oldParentId != newParentId)
            {
                if (oldParentId != null)
                    RemoveInverseMarker(oldParentId, taskId, isSubtask: true);
                if (newParentId != null)
                    AddInverseMarker(newParentId, taskId, isSubtask: true);
            }

            // Sync inverse parent markers (-^abc): diff old vs new
            var oldHasSubtaskIds = new HashSet<string>(oldParsed.HasSubtaskIds ?? []);
            var newHasSubtaskIds = new HashSet<string>(newParsed.HasSubtaskIds ?? []);
            foreach (var addedSubtaskId in newHasSubtaskIds.Except(oldHasSubtaskIds))
            {
                var subtask = GetTodoTaskById(addedSubtaskId);
                if (subtask == null || addedSubtaskId == taskId) continue;
                if (subtask.ListName != renamedTask.ListName) continue;
                var descendants = GetAllDescendantIds(addedSubtaskId);
                if (descendants.Contains(taskId)) continue;
                // Set parent on the subtask
                _db.Execute("UPDATE tasks SET parent_id = @parent WHERE id = @id",
                    ("@parent", taskId), ("@id", addedSubtaskId));
                // Add ^taskId to subtask's metadata
                var subParsed = TaskDescriptionParser.Parse(subtask.Description);
                var subSynced = TaskDescriptionParser.SyncMetadataToDescription(
                    subtask.Description, subtask.Priority, subtask.DueDate, subtask.Tags,
                    taskId, subParsed.BlocksIds, subParsed.HasSubtaskIds, subParsed.BlockedByIds);
                if (subSynced != subtask.Description)
                    _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                        ("@desc", subSynced), ("@id", addedSubtaskId));
            }
            foreach (var removedSubtaskId in oldHasSubtaskIds.Except(newHasSubtaskIds))
            {
                var subtask = GetTodoTaskById(removedSubtaskId);
                if (subtask == null || subtask.ParentId != taskId) continue;
                // Clear parent on the subtask
                _db.Execute("UPDATE tasks SET parent_id = NULL WHERE id = @id", ("@id", removedSubtaskId));
                // Remove ^taskId from subtask's metadata
                var subParsed = TaskDescriptionParser.Parse(subtask.Description);
                var subSynced = TaskDescriptionParser.SyncMetadataToDescription(
                    subtask.Description, subtask.Priority, subtask.DueDate, subtask.Tags,
                    null, subParsed.BlocksIds, subParsed.HasSubtaskIds, subParsed.BlockedByIds);
                if (subSynced != subtask.Description)
                    _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                        ("@desc", subSynced), ("@id", removedSubtaskId));
            }

            // Sync inverse blocker markers (-!abc): diff old vs new
            var oldBlockedByIds = new HashSet<string>(oldParsed.BlockedByIds ?? []);
            var newBlockedByIds = new HashSet<string>(newParsed.BlockedByIds ?? []);
            foreach (var addedBlockerId in newBlockedByIds.Except(oldBlockedByIds))
            {
                var blocker = GetTodoTaskById(addedBlockerId);
                if (blocker == null || addedBlockerId == taskId) continue;
                if (HasCircularBlocking(addedBlockerId, taskId)) continue;
                // Insert dependency
                _db.Execute("INSERT OR IGNORE INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
                    ("@blocker", addedBlockerId), ("@blocked", taskId));
                // Add !taskId to blocker's metadata
                var blockerParsed = TaskDescriptionParser.Parse(blocker.Description);
                var blockerBlocksIds = blockerParsed.BlocksIds?.ToList() ?? [];
                if (!blockerBlocksIds.Contains(taskId))
                {
                    blockerBlocksIds.Add(taskId);
                    var blockerSynced = TaskDescriptionParser.SyncMetadataToDescription(
                        blocker.Description, blocker.Priority, blocker.DueDate, blocker.Tags,
                        blockerParsed.ParentId, blockerBlocksIds.ToArray(),
                        blockerParsed.HasSubtaskIds, blockerParsed.BlockedByIds);
                    if (blockerSynced != blocker.Description)
                        _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                            ("@desc", blockerSynced), ("@id", addedBlockerId));
                }
            }
            foreach (var removedBlockerId in oldBlockedByIds.Except(newBlockedByIds))
            {
                // Remove dependency
                _db.Execute("DELETE FROM task_dependencies WHERE task_id = @blocker AND blocks_task_id = @blocked",
                    ("@blocker", removedBlockerId), ("@blocked", taskId));
                // Remove !taskId from blocker's metadata
                var blocker = GetTodoTaskById(removedBlockerId);
                if (blocker == null) continue;
                var blockerParsed = TaskDescriptionParser.Parse(blocker.Description);
                var blockerBlocksIds = blockerParsed.BlocksIds?.ToList() ?? [];
                if (blockerBlocksIds.Remove(taskId))
                {
                    var blockerSynced = TaskDescriptionParser.SyncMetadataToDescription(
                        blocker.Description, blocker.Priority, blocker.DueDate, blocker.Tags,
                        blockerParsed.ParentId, blockerBlocksIds.Count > 0 ? blockerBlocksIds.ToArray() : null,
                        blockerParsed.HasSubtaskIds, blockerParsed.BlockedByIds);
                    if (blockerSynced != blocker.Description)
                        _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                            ("@desc", blockerSynced), ("@id", removedBlockerId));
                }
            }
        }

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Renamed task: {taskId}");
    }

    private void SyncBlockingRelationships(string taskId, string[]? oldBlocksIds, string[]? newBlocksIds)
    {
        var oldBlocks = new HashSet<string>(oldBlocksIds ?? []);
        var newBlocks = new HashSet<string>(newBlocksIds ?? []);

        // Remove blockers no longer in metadata
        foreach (var removedId in oldBlocks.Except(newBlocks))
        {
            _db.Execute("DELETE FROM task_dependencies WHERE task_id = @blocker AND blocks_task_id = @blocked",
                ("@blocker", taskId), ("@blocked", removedId));
        }

        // Add new blockers from metadata
        foreach (var addedId in newBlocks.Except(oldBlocks))
        {
            var blocked = GetTodoTaskById(addedId);
            if (blocked != null && addedId != taskId && !HasCircularBlocking(taskId, addedId))
            {
                _db.Execute("INSERT OR IGNORE INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
                    ("@blocker", taskId), ("@blocked", addedId));
            }
        }
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

        // Block moving a subtask independently to a different list
        if (todoTask.ParentId != null)
        {
            return new TaskResult.Error(
                $"Cannot move subtask ({taskId}) to a different list. Use `tasker deps unset-parent {taskId}` first, or move its parent.");
        }

        var sourceList = todoTask.ListName;

        // Collect descendants for cascade move
        var descendantIds = GetAllDescendantIds(taskId);
        var hasCascade = descendantIds.Count > 0;

        if (recordUndo)
        {
            if (hasCascade) _services.Undo.BeginBatch($"Move ({taskId}) and {descendantIds.Count} subtask(s) to '{targetList}'");

            _services.Undo.RecordCommand(new MoveTaskCommand
            {
                TaskId = taskId,
                SourceList = sourceList,
                TargetList = targetList
            });

            foreach (var descId in descendantIds)
            {
                _services.Undo.RecordCommand(new MoveTaskCommand
                {
                    TaskId = descId,
                    SourceList = sourceList,
                    TargetList = targetList
                });
            }
        }

        CreateBackup();
        EnsureListExists(targetList);
        var movedTask = todoTask.MoveToList(targetList);
        UpdateTask(movedTask);
        BumpSortOrder(taskId, targetList);

        // Cascade move descendants
        foreach (var descId in descendantIds)
        {
            _db.Execute("UPDATE tasks SET list_name = @list WHERE id = @id",
                ("@list", targetList), ("@id", descId));
        }

        if (recordUndo)
        {
            if (hasCascade) _services.Undo.EndBatch();
            _services.Undo.SaveHistory();
        }

        var msg = hasCascade
            ? $"Moved ({taskId}) and {descendantIds.Count} subtask(s) from '{sourceList}' to '{targetList}'"
            : $"Moved task {taskId} from '{sourceList}' to '{targetList}'";
        return new TaskResult.Success(msg);
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

        var parsedForSync = TaskDescriptionParser.Parse(updatedTask.Description);
        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags,
            parsedForSync.ParentId, parsedForSync.BlocksIds,
            parsedForSync.HasSubtaskIds, parsedForSync.BlockedByIds);
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

        var parsedForSync = TaskDescriptionParser.Parse(updatedTask.Description);
        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags,
            parsedForSync.ParentId, parsedForSync.BlocksIds,
            parsedForSync.HasSubtaskIds, parsedForSync.BlockedByIds);
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

        // Also find trashed descendants to cascade-restore
        var trashedDescendantIds = _db.Query("""
            WITH RECURSIVE desc AS (
                SELECT id FROM tasks WHERE parent_id = @id AND is_trashed = 1
                UNION ALL
                SELECT t.id FROM tasks t JOIN desc d ON t.parent_id = d.id WHERE t.is_trashed = 1
            )
            SELECT id FROM desc
            """,
            reader => reader.GetString(0),
            ("@id", taskId));

        CreateBackup();
        _db.Execute("UPDATE tasks SET is_trashed = 0 WHERE id = @id", ("@id", taskId));
        foreach (var descId in trashedDescendantIds)
            _db.Execute("UPDATE tasks SET is_trashed = 0 WHERE id = @id", ("@id", descId));

        var msg = trashedDescendantIds.Count > 0
            ? $"Restored ({taskId}) and {trashedDescendantIds.Count} subtask(s)"
            : $"Restored task: {taskId}";
        return new TaskResult.Success(msg);
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
                    INSERT OR IGNORE INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at, parent_id)
                    VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, 0, @order, @completed, @parent)
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
                    ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value),
                    ("@parent", (object?)task.ParentId ?? DBNull.Value));
            }

            // Insert trashed tasks
            if (trashedList != null)
            {
                foreach (var task in trashedList.Tasks)
                {
                    var tagsJson = task.Tags is { Length: > 0 } ? JsonSerializer.Serialize(task.Tags) : null;
                    db.Execute("""
                        INSERT OR IGNORE INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at, parent_id)
                        VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, 1, @order, @completed, @parent)
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
                        ("@completed", (object?)task.CompletedAt?.ToString("o") ?? DBNull.Value),
                        ("@parent", (object?)task.ParentId ?? DBNull.Value));
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

    // --- Dependency operations ---

    /// <summary>
    /// Adds an inverse marker to a task's metadata line (e.g., -^childId on parent, -!blockerId on blocked).
    /// </summary>
    private void AddInverseMarker(string taskId, string refId, bool isSubtask)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null) return;

        var parsed = TaskDescriptionParser.Parse(task.Description);
        var currentIds = isSubtask
            ? parsed.HasSubtaskIds?.ToList() ?? []
            : parsed.BlockedByIds?.ToList() ?? [];

        if (currentIds.Contains(refId)) return; // Already present
        currentIds.Add(refId);

        var synced = TaskDescriptionParser.SyncMetadataToDescription(
            task.Description, task.Priority, task.DueDate, task.Tags,
            parsed.ParentId, parsed.BlocksIds,
            isSubtask ? currentIds.ToArray() : parsed.HasSubtaskIds,
            isSubtask ? parsed.BlockedByIds : currentIds.ToArray());
        if (synced != task.Description)
            _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                ("@desc", synced), ("@id", taskId));
    }

    /// <summary>
    /// Removes an inverse marker from a task's metadata line.
    /// </summary>
    private void RemoveInverseMarker(string taskId, string refId, bool isSubtask)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null) return;

        var parsed = TaskDescriptionParser.Parse(task.Description);
        var currentIds = isSubtask
            ? parsed.HasSubtaskIds?.ToList() ?? []
            : parsed.BlockedByIds?.ToList() ?? [];

        if (!currentIds.Remove(refId)) return; // Not present

        var synced = TaskDescriptionParser.SyncMetadataToDescription(
            task.Description, task.Priority, task.DueDate, task.Tags,
            parsed.ParentId, parsed.BlocksIds,
            isSubtask ? (currentIds.Count > 0 ? currentIds.ToArray() : null) : parsed.HasSubtaskIds,
            isSubtask ? parsed.BlockedByIds : (currentIds.Count > 0 ? currentIds.ToArray() : null));
        if (synced != task.Description)
            _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                ("@desc", synced), ("@id", taskId));
    }

    public TaskResult SetParent(string taskId, string parentId, bool recordUndo = true)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null) return new TaskResult.NotFound(taskId);

        var parent = GetTodoTaskById(parentId);
        if (parent == null) return new TaskResult.Error($"Parent task not found: {parentId}");

        if (task.Id == parentId) return new TaskResult.Error("A task cannot be its own parent");

        if (task.ListName != parent.ListName)
            return new TaskResult.Error($"Cannot set parent: task ({taskId}) is in '{task.ListName}' but parent ({parentId}) is in '{parent.ListName}'. Subtasks must be in the same list.");

        // Check for circular reference (parent is a descendant of task)
        var descendants = GetAllDescendantIds(taskId);
        if (descendants.Contains(parentId))
            return new TaskResult.Error($"Circular reference: ({parentId}) is already a descendant of ({taskId})");

        if (recordUndo)
        {
            var cmd = new SetParentCommand { TaskId = taskId, OldParentId = task.ParentId, NewParentId = parentId };
            _services.Undo.RecordCommand(cmd);
        }

        var oldParentId = task.ParentId;

        CreateBackup();
        _db.Execute("UPDATE tasks SET parent_id = @parent WHERE id = @id",
            ("@parent", parentId), ("@id", taskId));

        // Sync metadata line on the child
        var parsed = TaskDescriptionParser.Parse(task.Description);
        var synced = TaskDescriptionParser.SyncMetadataToDescription(
            task.Description, task.Priority, task.DueDate, task.Tags, parentId,
            parsed.BlocksIds, parsed.HasSubtaskIds, parsed.BlockedByIds);
        if (synced != task.Description)
            _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                ("@desc", synced), ("@id", taskId));

        // Sync inverse marker: add -^child on new parent, remove from old parent
        if (oldParentId != null && oldParentId != parentId)
            RemoveInverseMarker(oldParentId, taskId, isSubtask: true);
        AddInverseMarker(parentId, taskId, isSubtask: true);

        if (recordUndo) _services.Undo.SaveHistory();
        return new TaskResult.Success($"Set ({taskId}) as subtask of ({parentId})");
    }

    public TaskResult UnsetParent(string taskId, bool recordUndo = true)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null) return new TaskResult.NotFound(taskId);

        if (task.ParentId == null)
            return new TaskResult.NoChange($"Task ({taskId}) has no parent");

        var oldParentId = task.ParentId;

        if (recordUndo)
        {
            var cmd = new SetParentCommand { TaskId = taskId, OldParentId = oldParentId, NewParentId = null };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        _db.Execute("UPDATE tasks SET parent_id = NULL WHERE id = @id", ("@id", taskId));

        // Sync metadata line on the child
        var parsed = TaskDescriptionParser.Parse(task.Description);
        var synced = TaskDescriptionParser.SyncMetadataToDescription(
            task.Description, task.Priority, task.DueDate, task.Tags, null,
            parsed.BlocksIds, parsed.HasSubtaskIds, parsed.BlockedByIds);
        if (synced != task.Description)
            _db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                ("@desc", synced), ("@id", taskId));

        // Sync inverse marker: remove -^child from former parent
        RemoveInverseMarker(oldParentId, taskId, isSubtask: true);

        if (recordUndo) _services.Undo.SaveHistory();
        return new TaskResult.Success($"Removed parent from ({taskId})");
    }

    public TaskResult AddBlocker(string blockerId, string blockedId, bool recordUndo = true)
    {
        if (blockerId == blockedId)
            return new TaskResult.Error("A task cannot block itself");

        var blocker = GetTodoTaskById(blockerId);
        if (blocker == null) return new TaskResult.NotFound(blockerId);

        var blocked = GetTodoTaskById(blockedId);
        if (blocked == null) return new TaskResult.Error($"Blocked task not found: {blockedId}");

        // Check for circular blocking
        if (HasCircularBlocking(blockerId, blockedId))
            return new TaskResult.Error($"Circular dependency: ({blockedId}) already blocks ({blockerId}) directly or transitively");

        // Check if already exists
        var exists = _db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM task_dependencies WHERE task_id = @blocker AND blocks_task_id = @blocked",
            ("@blocker", blockerId), ("@blocked", blockedId));
        if (exists > 0)
            return new TaskResult.NoChange($"({blockerId}) already blocks ({blockedId})");

        if (recordUndo)
        {
            var cmd = new AddBlockerCommand { BlockerId = blockerId, BlockedId = blockedId };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        _db.Execute("INSERT INTO task_dependencies (task_id, blocks_task_id) VALUES (@blocker, @blocked)",
            ("@blocker", blockerId), ("@blocked", blockedId));

        // Sync inverse marker: add -!blocker on blocked task
        AddInverseMarker(blockedId, blockerId, isSubtask: false);

        if (recordUndo) _services.Undo.SaveHistory();
        return new TaskResult.Success($"({blockerId}) now blocks ({blockedId})");
    }

    public TaskResult RemoveBlocker(string blockerId, string blockedId, bool recordUndo = true)
    {
        var exists = _db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM task_dependencies WHERE task_id = @blocker AND blocks_task_id = @blocked",
            ("@blocker", blockerId), ("@blocked", blockedId));
        if (exists == 0)
            return new TaskResult.NoChange($"({blockerId}) does not block ({blockedId})");

        if (recordUndo)
        {
            var cmd = new RemoveBlockerCommand { BlockerId = blockerId, BlockedId = blockedId };
            _services.Undo.RecordCommand(cmd);
        }

        CreateBackup();
        _db.Execute("DELETE FROM task_dependencies WHERE task_id = @blocker AND blocks_task_id = @blocked",
            ("@blocker", blockerId), ("@blocked", blockedId));

        // Sync inverse marker: remove -!blocker from blocked task
        RemoveInverseMarker(blockedId, blockerId, isSubtask: false);

        if (recordUndo) _services.Undo.SaveHistory();
        return new TaskResult.Success($"({blockerId}) no longer blocks ({blockedId})");
    }

    public List<TodoTask> GetSubtasks(string parentId)
    {
        return QueryTasks("WHERE parent_id = @id AND is_trashed = 0", ("@id", parentId));
    }

    public List<string> GetAllDescendantIds(string parentId)
    {
        return _db.Query("""
            WITH RECURSIVE desc AS (
                SELECT id FROM tasks WHERE parent_id = @id AND is_trashed = 0
                UNION ALL
                SELECT t.id FROM tasks t JOIN desc d ON t.parent_id = d.id WHERE t.is_trashed = 0
            )
            SELECT id FROM desc
            """,
            reader => reader.GetString(0),
            ("@id", parentId));
    }

    public List<TodoTask> GetBlockedBy(string taskId)
    {
        return _db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks t JOIN task_dependencies td ON td.task_id = t.id WHERE td.blocks_task_id = @id AND t.is_trashed = 0",
            ReadTask,
            ("@id", taskId));
    }

    public List<TodoTask> GetBlocks(string taskId)
    {
        return _db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks t JOIN task_dependencies td ON td.blocks_task_id = t.id WHERE td.task_id = @id AND t.is_trashed = 0",
            ReadTask,
            ("@id", taskId));
    }

    public bool HasCircularBlocking(string blockerId, string blockedId)
    {
        // Check if blockedId already blocks blockerId (directly or transitively)
        var reachable = _db.Query("""
            WITH RECURSIVE chain AS (
                SELECT blocks_task_id AS target FROM task_dependencies WHERE task_id = @start
                UNION ALL
                SELECT td.blocks_task_id FROM task_dependencies td JOIN chain c ON td.task_id = c.target
            )
            SELECT target FROM chain
            """,
            reader => reader.GetString(0),
            ("@start", blockedId));
        return reachable.Contains(blockerId);
    }

    /// <summary>
    /// Gets blocking relationship IDs for a task (what this task blocks).
    /// </summary>
    public List<string> GetBlocksIds(string taskId)
    {
        return _db.Query(
            "SELECT blocks_task_id FROM task_dependencies WHERE task_id = @id",
            reader => reader.GetString(0),
            ("@id", taskId));
    }

    /// <summary>
    /// Gets blocked-by relationship IDs for a task (what blocks this task).
    /// </summary>
    public List<string> GetBlockedByIds(string taskId)
    {
        return _db.Query(
            "SELECT task_id FROM task_dependencies WHERE blocks_task_id = @id",
            reader => reader.GetString(0),
            ("@id", taskId));
    }

    // --- Search ---

    public static List<TodoTask> SearchTasks(TaskerServices services, string query)
    {
        var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var tasks = services.Db.Query(
            $"SELECT {TaskSelectColumns} FROM tasks WHERE is_trashed = 0 AND description LIKE @search ESCAPE '\\' COLLATE NOCASE ORDER BY sort_order DESC",
            ReadTask,
            ("@search", $"%{escaped}%"));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var active = tasks
            .Where(t => t.Status != TaskStatus.Done)
            .OrderBy(t => StatusSortOrder(t.Status))
            .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
            .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
        var done = tasks
            .Where(t => t.Status == TaskStatus.Done)
            .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
            .ToList();
        return [..active, ..done];
    }

    public static List<TodoTask> SearchTasks(string query) => SearchTasks(TaskerServices.Default, query);
}
