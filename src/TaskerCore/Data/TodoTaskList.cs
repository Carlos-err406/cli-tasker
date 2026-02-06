namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerCore.Results;
using TaskerCore.Undo.Commands;

public class TodoTaskList
{
    private static readonly object SaveLock = new();

    private readonly TaskerServices _services;
    private TaskList[] TaskLists { get; set; } = [TaskList.Create(ListManager.DefaultListName)];
    private TaskList[] TrashLists { get; set; } = [];
    private readonly string? listNameFilter;

    public TodoTaskList(TaskerServices services, string? listName = null)
    {
        _services = services;
        listNameFilter = listName;
        Load();
    }

    /// <summary>
    /// Creates a TodoTaskList using the default services.
    /// </summary>
    public TodoTaskList(string? listName = null) : this(TaskerServices.Default, listName)
    {
    }

    private void Load()
    {
        _services.Paths.EnsureDirectory();

        // Load all tasks
        if (File.Exists(_services.Paths.AllTasksPath))
        {
            try
            {
                var raw = File.ReadAllText(_services.Paths.AllTasksPath);
                TaskLists = DeserializeWithMigration(raw);
            }
            catch (JsonException)
            {
                TaskLists = [TaskList.Create(ListManager.DefaultListName)];
            }
        }

        // Load all trash
        if (File.Exists(_services.Paths.AllTrashPath))
        {
            try
            {
                var trashRaw = File.ReadAllText(_services.Paths.AllTrashPath);
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
            _services.Undo.RecordCommand(cmd);
        }

        AddTaskToList(todoTask);
        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
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
            _services.Undo.RecordCommand(cmd);
        }

        DeleteTask(taskId, save: false, moveToTrash: false, recordUndo: false);
        var checkedTask = todoTask.Check();
        AddTaskToList(checkedTask);
        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
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
            _services.Undo.RecordCommand(cmd);
        }

        DeleteTask(taskId, save: false, moveToTrash: false, recordUndo: false);
        var uncheckedTask = todoTask.UnCheck();
        AddTaskToList(uncheckedTask);
        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
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
            _services.Undo.RecordCommand(cmd);
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
                _services.Undo.SaveHistory();
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
            _services.Undo.BeginBatch($"Delete {taskIds.Length} tasks");
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
                _services.Undo.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            AddTaskToTrash(task);
            results.Add(new TaskResult.Success($"Deleted task: {taskId}"));
        }

        if (recordUndo)
        {
            _services.Undo.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public BatchTaskResult CheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            _services.Undo.BeginBatch($"Check {taskIds.Length} tasks");
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
                _services.Undo.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            var checkedTask = todoTask.Check();
            AddTaskToList(checkedTask);
            results.Add(new TaskResult.Success($"Checked task: {taskId}"));
        }

        if (recordUndo)
        {
            _services.Undo.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
        }

        return new BatchTaskResult { Results = results };
    }

    public BatchTaskResult UncheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            _services.Undo.BeginBatch($"Uncheck {taskIds.Length} tasks");
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
                _services.Undo.RecordCommand(cmd);
            }

            RemoveTaskFromTaskLists(taskId);
            var uncheckedTask = todoTask.UnCheck();
            AddTaskToList(uncheckedTask);
            results.Add(new TaskResult.Success($"Unchecked task: {taskId}"));
        }

        if (recordUndo)
        {
            _services.Undo.EndBatch();
        }

        Save();

        if (recordUndo)
        {
            _services.Undo.SaveHistory();
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
            _services.Undo.RecordCommand(cmd);
        }

        foreach (var task in tasksToMove)
        {
            RemoveTaskFromTaskLists(task.Id);
            AddTaskToTrash(task);
        }

        Save();

        if (recordUndo && tasksToMove.Length > 0)
        {
            _services.Undo.SaveHistory();
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
            _services.Undo.RecordCommand(cmd);
        }

        RemoveTaskFromTaskLists(taskId);
        var renamedTask = todoTask.Rename(newDescription);
        AddTaskToList(renamedTask);
        Save();

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

        RemoveTaskFromTaskLists(taskId);
        var movedTask = todoTask.MoveToList(targetList);
        AddTaskToList(movedTask);
        Save();

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

        RemoveTaskFromTaskLists(taskId);
        var updatedTask = dueDate.HasValue ? todoTask.SetDueDate(dueDate.Value) : todoTask.ClearDueDate();

        // Sync the metadata back to description
        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags);
        updatedTask = updatedTask.Rename(syncedDescription);

        AddTaskToList(updatedTask);
        Save();

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

        RemoveTaskFromTaskLists(taskId);
        var updatedTask = priority.HasValue ? todoTask.SetPriority(priority.Value) : todoTask.ClearPriority();

        // Sync the metadata back to description
        var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
            updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags);
        updatedTask = updatedTask.Rename(syncedDescription);

        AddTaskToList(updatedTask);
        Save();

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

    // Static methods for list management - these take TaskerServices as parameter

    public static string[] GetAllListNames(TaskerServices services)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return [ListManager.DefaultListName];
        }

        try
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);

            // Preserve array order (supports manual reordering in TaskerTray)
            var listNames = taskLists
                .Select(l => l.ListName)
                .Distinct()
                .ToArray();

            // Ensure default list is present (but don't force it to first position)
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

    /// <summary>
    /// Gets all list names using default services.
    /// </summary>
    public static string[] GetAllListNames() => GetAllListNames(TaskerServices.Default);

    public static bool ListHasTasks(TaskerServices services, string listName)
    {
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            return taskLists.Any(l => l.ListName == listName && l.Tasks.Length > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool ListHasTasks(string listName) => ListHasTasks(TaskerServices.Default, listName);

    /// <summary>
    /// Checks if a list exists (with or without tasks).
    /// </summary>
    public static bool ListExists(TaskerServices services, string listName)
    {
        if (listName == ListManager.DefaultListName)
        {
            return true;
        }

        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            return taskLists.Any(l => l.ListName == listName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool ListExists(string listName) => ListExists(TaskerServices.Default, listName);

    /// <summary>
    /// Creates an empty list.
    /// </summary>
    public static void CreateList(TaskerServices services, string listName)
    {
        services.Paths.EnsureDirectory();

        TaskList[] taskLists;
        if (File.Exists(services.Paths.AllTasksPath))
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
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
            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(taskLists));
        }
    }

    public static void CreateList(string listName) => CreateList(TaskerServices.Default, listName);

    public static void DeleteList(TaskerServices services, string listName)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(services.Paths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var remainingLists = taskLists.Where(l => l.ListName != listName).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(remainingLists));
        }

        // Also remove from trash
        if (File.Exists(services.Paths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(services.Paths.AllTrashPath);
            var trashLists = DeserializeWithMigration(trashRaw);
            var remainingTrash = trashLists.Where(l => l.ListName != listName).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(services.Paths.AllTrashPath, JsonSerializer.Serialize(remainingTrash));
            }
        }
    }

    public static void DeleteList(string listName) => DeleteList(TaskerServices.Default, listName);

    /// <summary>
    /// Gets a list by name from the active tasks file.
    /// </summary>
    public static TaskList GetListByName(TaskerServices services, string listName)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
            throw new Exceptions.ListNotFoundException(listName);

        var raw = File.ReadAllText(services.Paths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        return taskLists.FirstOrDefault(l => l.ListName == listName)
            ?? throw new Exceptions.ListNotFoundException(listName);
    }

    public static TaskList GetListByName(string listName) => GetListByName(TaskerServices.Default, listName);

    /// <summary>
    /// Gets a list by name from the trash file, or null if not found.
    /// </summary>
    public static TaskList? GetTrashedListByName(TaskerServices services, string listName)
    {
        if (!File.Exists(services.Paths.AllTrashPath))
            return null;

        var raw = File.ReadAllText(services.Paths.AllTrashPath);
        var trashLists = DeserializeWithMigration(raw);
        return trashLists.FirstOrDefault(l => l.ListName == listName);
    }

    public static TaskList? GetTrashedListByName(string listName) => GetTrashedListByName(TaskerServices.Default, listName);

    /// <summary>
    /// Gets the index of a list in the TaskLists array.
    /// </summary>
    public static int GetListIndex(TaskerServices services, string listName)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
            return -1;

        var raw = File.ReadAllText(services.Paths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        return Array.FindIndex(taskLists, l => l.ListName == listName);
    }

    public static int GetListIndex(string listName) => GetListIndex(TaskerServices.Default, listName);

    /// <summary>
    /// Restores a previously deleted list at the specified index.
    /// Used by undo to restore deleted lists.
    /// </summary>
    public static void RestoreList(TaskerServices services, TaskList activeList, TaskList? trashedList, int originalIndex)
    {
        services.Paths.EnsureDirectory();

        lock (SaveLock)
        {
            // Restore active list
            TaskList[] taskLists = [];
            if (File.Exists(services.Paths.AllTasksPath))
            {
                var raw = File.ReadAllText(services.Paths.AllTasksPath);
                taskLists = DeserializeWithMigration(raw);
            }

            var listsList = taskLists.ToList();
            var clampedIndex = Math.Clamp(originalIndex, 0, listsList.Count);
            listsList.Insert(clampedIndex, activeList);

            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(listsList.ToArray()));

            // Restore trashed list if it existed
            if (trashedList != null)
            {
                TaskList[] trashLists = [];
                if (File.Exists(services.Paths.AllTrashPath))
                {
                    var trashRaw = File.ReadAllText(services.Paths.AllTrashPath);
                    trashLists = DeserializeWithMigration(trashRaw);
                }

                var trashList = trashLists.ToList();
                trashList.Add(trashedList);
                File.WriteAllText(services.Paths.AllTrashPath, JsonSerializer.Serialize(trashList.ToArray()));
            }
        }
    }

    public static void RestoreList(TaskList activeList, TaskList? trashedList, int originalIndex) =>
        RestoreList(TaskerServices.Default, activeList, trashedList, originalIndex);

    public static void RenameList(TaskerServices services, string oldName, string newName)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(services.Paths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var updatedLists = taskLists.Select(l =>
            l.ListName == oldName
                ? l with
                {
                    ListName = newName,
                    // Also update ListName on each task within the list
                    Tasks = l.Tasks.Select(t => t with { ListName = newName }).ToArray()
                }
                : l
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(updatedLists));
        }

        // Also update in trash
        if (File.Exists(services.Paths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(services.Paths.AllTrashPath);
            var trashLists = DeserializeWithMigration(trashRaw);
            var updatedTrash = trashLists.Select(l =>
                l.ListName == oldName
                    ? l with
                    {
                        ListName = newName,
                        Tasks = l.Tasks.Select(t => t with { ListName = newName }).ToArray()
                    }
                    : l
            ).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(services.Paths.AllTrashPath, JsonSerializer.Serialize(updatedTrash));
            }
        }
    }

    public static void RenameList(string oldName, string newName) => RenameList(TaskerServices.Default, oldName, newName);

    /// <summary>
    /// Gets the collapse state for a list. Returns false if list doesn't exist.
    /// Used by TaskerTray for UI state; CLI versions ignore this.
    /// </summary>
    public static bool IsListCollapsed(TaskerServices services, string listName)
    {
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);
            var list = taskLists.FirstOrDefault(l => l.ListName == listName);
            return list?.IsCollapsed ?? false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsListCollapsed(string listName) => IsListCollapsed(TaskerServices.Default, listName);

    /// <summary>
    /// Sets the collapse state for a list.
    /// Used by TaskerTray for UI state; CLI versions ignore this.
    /// </summary>
    public static void SetListCollapsed(TaskerServices services, string listName, bool collapsed)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(services.Paths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var updatedLists = taskLists.Select(l =>
            l.ListName == listName ? l.SetCollapsed(collapsed) : l
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(updatedLists));
        }
    }

    public static void SetListCollapsed(string listName, bool collapsed) =>
        SetListCollapsed(TaskerServices.Default, listName, collapsed);

    private void Save()
    {
        _services.Paths.EnsureDirectory();

        // Create backup BEFORE writing (captures current state)
        // Backup failure should not block save
        try { _services.Backup.CreateBackup(); }
        catch { /* Ignore backup failures */ }

        lock (SaveLock)
        {
            // Atomic write for tasks: write to temp, then rename
            var tasksTempPath = _services.Paths.AllTasksPath + ".tmp";
            var tasksJson = JsonSerializer.Serialize(TaskLists);
            File.WriteAllText(tasksTempPath, tasksJson);
            File.Move(tasksTempPath, _services.Paths.AllTasksPath, overwrite: true);

            // Atomic write for trash
            var trashTempPath = _services.Paths.AllTrashPath + ".tmp";
            var trashJson = JsonSerializer.Serialize(TrashLists);
            File.WriteAllText(trashTempPath, trashJson);
            File.Move(trashTempPath, _services.Paths.AllTrashPath, overwrite: true);
        }
    }

    /// <summary>
    /// Reorders a task within its list by moving it to a new index.
    /// </summary>
    public static void ReorderTask(TaskerServices services, string taskId, int newIndex, bool recordUndo = true)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
            return;

        lock (SaveLock)
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw);

            // Find the list containing this task
            var listIndex = -1;
            var taskIndex = -1;
            for (var i = 0; i < taskLists.Length; i++)
            {
                var idx = Array.FindIndex(taskLists[i].Tasks, t => t.Id == taskId);
                if (idx >= 0)
                {
                    listIndex = i;
                    taskIndex = idx;
                    break;
                }
            }

            if (listIndex < 0 || taskIndex < 0)
                return;

            var list = taskLists[listIndex];
            var tasks = list.Tasks.ToList();

            // Clamp newIndex to valid range
            var clampedNewIndex = Math.Max(0, Math.Min(newIndex, tasks.Count - 1));

            if (taskIndex == clampedNewIndex)
                return;

            // Record undo command before modification
            if (recordUndo)
            {
                services.Undo.RecordCommand(new Undo.Commands.ReorderTaskCommand
                {
                    TaskId = taskId,
                    ListName = list.ListName,
                    OldIndex = taskIndex,
                    NewIndex = clampedNewIndex
                });
            }

            // Remove from old position and insert at new position
            var task = tasks[taskIndex];
            tasks.RemoveAt(taskIndex);
            tasks.Insert(clampedNewIndex, task);

            taskLists[listIndex] = list.ReplaceTasks(tasks.ToArray());

            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(taskLists));

            if (recordUndo)
            {
                services.Undo.SaveHistory();
            }
        }
    }

    public static void ReorderTask(string taskId, int newIndex, bool recordUndo = true) =>
        ReorderTask(TaskerServices.Default, taskId, newIndex, recordUndo);

    /// <summary>
    /// Reorders a list by moving it to a new index in the TaskLists array.
    /// </summary>
    public static void ReorderList(TaskerServices services, string listName, int newIndex, bool recordUndo = true)
    {
        services.Paths.EnsureDirectory();
        if (!File.Exists(services.Paths.AllTasksPath))
            return;

        lock (SaveLock)
        {
            var raw = File.ReadAllText(services.Paths.AllTasksPath);
            var taskLists = DeserializeWithMigration(raw).ToList();

            var currentIndex = taskLists.FindIndex(l => l.ListName == listName);
            if (currentIndex < 0)
                return;

            // Clamp newIndex to valid range
            var clampedNewIndex = Math.Max(0, Math.Min(newIndex, taskLists.Count - 1));

            if (currentIndex == clampedNewIndex)
                return;

            // Record undo command before modification
            if (recordUndo)
            {
                services.Undo.RecordCommand(new Undo.Commands.ReorderListCommand
                {
                    ListName = listName,
                    OldIndex = currentIndex,
                    NewIndex = clampedNewIndex
                });
            }

            // Remove from old position and insert at new position
            var list = taskLists[currentIndex];
            taskLists.RemoveAt(currentIndex);
            taskLists.Insert(clampedNewIndex, list);

            File.WriteAllText(services.Paths.AllTasksPath, JsonSerializer.Serialize(taskLists.ToArray()));

            if (recordUndo)
            {
                services.Undo.SaveHistory();
            }
        }
    }

    public static void ReorderList(string listName, int newIndex, bool recordUndo = true) =>
        ReorderList(TaskerServices.Default, listName, newIndex, recordUndo);
}
