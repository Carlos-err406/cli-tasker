namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Results;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

public class TodoTaskList
{
    private static readonly object SaveLock = new();

    private TaskList[] TaskLists { get; set; } = [TaskList.Create(ListManager.DefaultListName)];
    private TaskList[] TrashLists { get; set; } = [];
    private readonly string? listNameFilter;

    public TodoTaskList(string? listName = null)
    {
        listNameFilter = listName;
        Load();
    }

    private void Load()
    {
        StoragePaths.EnsureDirectory();

        // Load all tasks
        if (File.Exists(StoragePaths.AllTasksPath))
        {
            try
            {
                var raw = File.ReadAllText(StoragePaths.AllTasksPath);
                TaskLists = DeserializeWithMigration(raw);
            }
            catch (JsonException)
            {
                TaskLists = [TaskList.Create(ListManager.DefaultListName)];
            }
        }

        // Load all trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            try
            {
                var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
                TrashLists = DeserializeWithMigration(trashRaw);
            }
            catch (JsonException)
            {
                TrashLists = [];
            }
        }

        EnsureDefaultListExists();
    }

    /// <summary>
    /// Deserializes JSON, automatically detecting and migrating from old format if needed.
    /// </summary>
    private static TaskList[] DeserializeWithMigration(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        // Try to detect format by checking if it's an array of objects with ListName and Tasks properties
        // or an array of TodoTask objects directly
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return [];
        }

        var firstElement = root[0];

        // New format: objects with "ListName" and "Tasks" properties (note: JSON uses PascalCase by default)
        if (firstElement.TryGetProperty("ListName", out _) && firstElement.TryGetProperty("Tasks", out _))
        {
            return JsonSerializer.Deserialize<TaskList[]>(json) ?? [];
        }

        // Old format: flat array of TodoTask objects with "ListName" on each task
        // Check for task-specific properties
        if (firstElement.TryGetProperty("Id", out _) && firstElement.TryGetProperty("Description", out _))
        {
            var oldTasks = JsonSerializer.Deserialize<TodoTask[]>(json) ?? [];
            return MigrateFromFlatTasks(oldTasks);
        }

        return [];
    }

    /// <summary>
    /// Converts flat TodoTask[] to TaskList[] grouped by ListName.
    /// </summary>
    private static TaskList[] MigrateFromFlatTasks(TodoTask[] tasks)
    {
        return tasks
            .GroupBy(t => t.ListName)
            .Select(g => new TaskList(g.Key, g.ToArray()))
            .ToArray();
    }

    private void EnsureDefaultListExists()
    {
        if (!TaskLists.Any(l => l.ListName == ListManager.DefaultListName))
        {
            TaskLists = [TaskList.Create(ListManager.DefaultListName), ..TaskLists];
        }
    }

    // Flat task accessors (for operations that need to work across all tasks)
    private IEnumerable<TodoTask> AllTasks => TaskLists.SelectMany(l => l.Tasks);
    private IEnumerable<TodoTask> AllTrash => TrashLists.SelectMany(l => l.Tasks);

    // Filter helpers
    private TodoTask[] GetFilteredTasks() =>
        listNameFilter == null
            ? AllTasks.ToArray()
            : TaskLists.FirstOrDefault(l => l.ListName == listNameFilter)?.Tasks ?? [];

    private TodoTask[] GetFilteredTrash() =>
        listNameFilter == null
            ? AllTrash.ToArray()
            : TrashLists.FirstOrDefault(l => l.ListName == listNameFilter)?.Tasks ?? [];

    // Public accessor for TUI and GUI
    public List<TodoTask> GetAllTasks() => GetFilteredTasks().ToList();

    /// <summary>
    /// Gets tasks sorted for display: unchecked first (by priority, then due date), then checked.
    /// </summary>
    public List<TodoTask> GetSortedTasks(bool? filterChecked = null, Priority? filterPriority = null, bool? filterOverdue = null)
    {
        var tasks = GetFilteredTasks();
        var today = DateOnly.FromDateTime(DateTime.Today);

        IEnumerable<TodoTask> filteredTasks = filterChecked switch
        {
            true => tasks.Where(td => td.IsChecked),
            false => tasks.Where(td => !td.IsChecked),
            null => tasks
        };

        if (filterPriority.HasValue)
            filteredTasks = filteredTasks.Where(t => t.Priority == filterPriority.Value);

        if (filterOverdue == true)
            filteredTasks = filteredTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value < today);

        return filteredTasks
            .OrderBy(td => td.IsChecked)
            .ThenBy(td => td.Priority.HasValue ? (int)td.Priority : 99)
            .ThenBy(td => GetDueDateSortOrder(td.DueDate, today))
            .ThenByDescending(td => td.CreatedAt)
            .ToList();
    }

    private static int GetDueDateSortOrder(DateOnly? dueDate, DateOnly today)
    {
        if (!dueDate.HasValue) return 99;
        var days = dueDate.Value.DayNumber - today.DayNumber;
        return days < 0 ? 0 : days;
    }

    public void AddTodoTask(TodoTask todoTask, bool recordUndo = true)
    {
        if (recordUndo)
        {
            var cmd = new AddTaskCommand { Task = todoTask };
            UndoManager.Instance.RecordCommand(cmd);
        }

        AddTaskToList(todoTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }
    }

    private void AddTaskToList(TodoTask task)
    {
        var listName = task.ListName;
        var existingList = TaskLists.FirstOrDefault(l => l.ListName == listName);

        if (existingList != null)
        {
            TaskLists = TaskLists
                .Select(l => l.ListName == listName ? l.AddTask(task) : l)
                .ToArray();
        }
        else
        {
            // Create new list with the task
            TaskLists = [..TaskLists, new TaskList(listName, [task])];
        }
    }

    private void RemoveTaskFromTaskLists(string taskId)
    {
        TaskLists = TaskLists
            .Select(l => l.RemoveTask(taskId))
            .ToArray();
    }

    private void RemoveTaskFromTrashLists(string taskId)
    {
        TrashLists = TrashLists
            .Select(l => l.RemoveTask(taskId))
            .ToArray();
    }

    public TodoTask? GetTodoTaskById(string taskId)
    {
        // Always search globally by ID
        return AllTasks.FirstOrDefault(task => task.Id == taskId);
    }

    public TaskResult CheckTask(string taskId, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo)
        {
            var cmd = new CheckTaskCommand { TaskId = taskId, WasChecked = todoTask.IsChecked };
            UndoManager.Instance.RecordCommand(cmd);
        }

        DeleteTask(taskId, save: false, moveToTrash: false, recordUndo: false);
        var checkedTask = todoTask.Check();
        AddTaskToList(checkedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new TaskResult.Success($"Checked task: {taskId}");
    }

    public TaskResult UncheckTask(string taskId, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        if (recordUndo)
        {
            var cmd = new UncheckTaskCommand { TaskId = taskId, WasChecked = todoTask.IsChecked };
            UndoManager.Instance.RecordCommand(cmd);
        }

        DeleteTask(taskId, save: false, moveToTrash: false, recordUndo: false);
        var uncheckedTask = todoTask.UnCheck();
        AddTaskToList(uncheckedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new TaskResult.Success($"Unchecked task: {taskId}");
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
            UndoManager.Instance.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);

        if (moveToTrash)
        {
            AddTaskToTrash(task);
        }

        if (save)
        {
            Save();
            if (recordUndo && moveToTrash)
            {
                UndoManager.Instance.SaveHistory();
            }
        }

        return new TaskResult.Success($"Deleted task: {taskId}");
    }

    private void AddTaskToTrash(TodoTask task)
    {
        var listName = task.ListName;
        var existingList = TrashLists.FirstOrDefault(l => l.ListName == listName);

        if (existingList != null)
        {
            TrashLists = TrashLists
                .Select(l => l.ListName == listName ? l.AddTask(task) : l)
                .ToArray();
        }
        else
        {
            TrashLists = [..TrashLists, new TaskList(listName, [task])];
        }
    }

    public BatchTaskResult DeleteTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Delete {taskIds.Length} tasks");
        }

        var results = new List<TaskResult>();

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
                UndoManager.Instance.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            AddTaskToTrash(task);
            results.Add(new TaskResult.Success($"Deleted task: {taskId}"));
        }

        if (recordUndo)
        {
            UndoManager.Instance.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public BatchTaskResult CheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Check {taskIds.Length} tasks");
        }

        var results = new List<TaskResult>();

        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                results.Add(new TaskResult.NotFound(taskId));
                continue;
            }

            if (recordUndo)
            {
                var cmd = new CheckTaskCommand { TaskId = taskId, WasChecked = todoTask.IsChecked };
                UndoManager.Instance.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            var checkedTask = todoTask.Check();
            AddTaskToList(checkedTask);
            results.Add(new TaskResult.Success($"Checked task: {taskId}"));
        }

        if (recordUndo)
        {
            UndoManager.Instance.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public BatchTaskResult UncheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Uncheck {taskIds.Length} tasks");
        }

        var results = new List<TaskResult>();

        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                results.Add(new TaskResult.NotFound(taskId));
                continue;
            }

            if (recordUndo)
            {
                var cmd = new UncheckTaskCommand { TaskId = taskId, WasChecked = todoTask.IsChecked };
                UndoManager.Instance.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            var uncheckedTask = todoTask.UnCheck();
            AddTaskToList(uncheckedTask);
            results.Add(new TaskResult.Success($"Unchecked task: {taskId}"));
        }

        if (recordUndo)
        {
            UndoManager.Instance.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public int ClearTasks(bool recordUndo = true)
    {
        // Only clear tasks matching the filter
        var tasksToMove = GetFilteredTasks();

        if (recordUndo && tasksToMove.Length > 0)
        {
            var cmd = new ClearTasksCommand { ListName = listNameFilter, ClearedTasks = tasksToMove };
            UndoManager.Instance.RecordCommand(cmd);
        }

        foreach (var task in tasksToMove)
        {
            RemoveTaskFromTaskLists(task.Id);
            AddTaskToTrash(task);
        }

        Save();

        if (recordUndo && tasksToMove.Length > 0)
        {
            UndoManager.Instance.SaveHistory();
        }

        return tasksToMove.Length;
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
            UndoManager.Instance.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);
        var renamedTask = todoTask.Rename(newDescription);
        AddTaskToList(renamedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
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
            UndoManager.Instance.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);
        var movedTask = todoTask.MoveToList(targetList);
        AddTaskToList(movedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
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
            UndoManager.Instance.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);
        var updatedTask = dueDate.HasValue ? todoTask.SetDueDate(dueDate.Value) : todoTask.ClearDueDate();
        AddTaskToList(updatedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
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
            UndoManager.Instance.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);
        var updatedTask = priority.HasValue ? todoTask.SetPriority(priority.Value) : todoTask.ClearPriority();
        AddTaskToList(updatedTask);
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        var message = priority.HasValue
            ? $"Set priority for {taskId}: {priority}"
            : $"Cleared priority for {taskId}";
        return new TaskResult.Success(message);
    }

    // Trash methods

    public List<TodoTask> GetTrash()
    {
        return GetFilteredTrash().ToList();
    }

    public TaskResult RestoreFromTrash(string taskId)
    {
        var task = AllTrash.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        RemoveTaskFromTrashLists(taskId);
        AddTaskToList(task);
        Save();

        return new TaskResult.Success($"Restored task: {taskId}");
    }

    public int ClearTrash()
    {
        var trashToClear = GetFilteredTrash();
        var count = trashToClear.Length;

        foreach (var task in trashToClear)
        {
            RemoveTaskFromTrashLists(task.Id);
        }

        // Clean up empty trash lists
        TrashLists = TrashLists.Where(l => l.Tasks.Length > 0).ToArray();

        Save();
        return count;
    }

    public TaskStats GetStats()
    {
        var tasks = GetFilteredTasks();
        var trash = GetFilteredTrash();
        return new TaskStats
        {
            Total = tasks.Length,
            Checked = tasks.Count(t => t.IsChecked),
            Unchecked = tasks.Count(t => !t.IsChecked),
            Trash = trash.Length
        };
    }

    // Static methods for list management

    public static string[] GetAllListNames()
    {
        StoragePaths.EnsureDirectory();
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return [ListManager.DefaultListName];
        }

        try
        {
            var raw = File.ReadAllText(StoragePaths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);

            var listNames = taskLists
                .Select(l => l.ListName)
                .Distinct()
                .OrderBy(name => name != ListManager.DefaultListName)
                .ThenBy(name => name)
                .ToArray();

            if (listNames.Length == 0 || !listNames.Contains(ListManager.DefaultListName))
            {
                return [ListManager.DefaultListName, .. listNames.Where(n => n != ListManager.DefaultListName)];
            }

            return listNames;
        }
        catch (JsonException)
        {
            return [ListManager.DefaultListName];
        }
    }

    public static bool ListHasTasks(string listName)
    {
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(StoragePaths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            return taskLists.Any(l => l.ListName == listName && l.Tasks.Length > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a list exists (with or without tasks).
    /// </summary>
    public static bool ListExists(string listName)
    {
        if (listName == ListManager.DefaultListName)
        {
            return true;
        }

        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(StoragePaths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            return taskLists.Any(l => l.ListName == listName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates an empty list.
    /// </summary>
    public static void CreateList(string listName)
    {
        StoragePaths.EnsureDirectory();

        TaskList[] taskLists;
        if (File.Exists(StoragePaths.AllTasksPath))
        {
            var raw = File.ReadAllText(StoragePaths.AllTasksPath);
            taskLists = DeserializeWithMigration(raw);
        }
        else
        {
            taskLists = [TaskList.Create(ListManager.DefaultListName)];
        }

        // Add the new empty list
        taskLists = [..taskLists, TaskList.Create(listName)];

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(taskLists));
        }
    }

    public static void DeleteList(string listName)
    {
        StoragePaths.EnsureDirectory();
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(StoragePaths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var remainingLists = taskLists.Where(l => l.ListName != listName).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(remainingLists));
        }

        // Also remove from trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
            var trashLists = DeserializeWithMigration(trashRaw);
            var remainingTrash = trashLists.Where(l => l.ListName != listName).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(StoragePaths.AllTrashPath, JsonSerializer.Serialize(remainingTrash));
            }
        }
    }

    public static void RenameList(string oldName, string newName)
    {
        StoragePaths.EnsureDirectory();
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(StoragePaths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var updatedLists = taskLists.Select(l =>
            l.ListName == oldName ? l with { ListName = newName } : l
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(updatedLists));
        }

        // Also update in trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
            var trashLists = DeserializeWithMigration(trashRaw);
            var updatedTrash = trashLists.Select(l =>
                l.ListName == oldName ? l with { ListName = newName } : l
            ).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(StoragePaths.AllTrashPath, JsonSerializer.Serialize(updatedTrash));
            }
        }
    }

    /// <summary>
    /// Gets the collapse state for a list. Returns false if list doesn't exist.
    /// Used by TaskerTray for UI state; CLI versions ignore this.
    /// </summary>
    public static bool IsListCollapsed(string listName)
    {
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(StoragePaths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            var list = taskLists.FirstOrDefault(l => l.ListName == listName);
            return list?.IsCollapsed ?? false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the collapse state for a list.
    /// Used by TaskerTray for UI state; CLI versions ignore this.
    /// </summary>
    public static void SetListCollapsed(string listName, bool collapsed)
    {
        StoragePaths.EnsureDirectory();
        if (!File.Exists(StoragePaths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(StoragePaths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var updatedLists = taskLists.Select(l =>
            l.ListName == listName ? l.SetCollapsed(collapsed) : l
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(updatedLists));
        }
    }

    private void Save()
    {
        StoragePaths.EnsureDirectory();
        lock (SaveLock)
        {
            var tasksJson = JsonSerializer.Serialize(TaskLists);
            File.WriteAllText(StoragePaths.AllTasksPath, tasksJson);

            var trashJson = JsonSerializer.Serialize(TrashLists);
            File.WriteAllText(StoragePaths.AllTrashPath, trashJson);
        }
    }
}
