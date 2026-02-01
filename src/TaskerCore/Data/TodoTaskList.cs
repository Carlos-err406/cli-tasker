namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Results;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

public class TodoTaskList
{
    private static readonly object SaveLock = new();

    private TodoTask[] TodoTasks { get; set; } = [];
    private TodoTask[] TrashTasks { get; set; } = [];
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
                var deserialized = JsonSerializer.Deserialize<TodoTask[]>(raw);
                TodoTasks = deserialized ?? [];
            }
            catch (JsonException)
            {
                TodoTasks = [];
            }
        }

        // Load all trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            try
            {
                var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
                var trashDeserialized = JsonSerializer.Deserialize<TodoTask[]>(trashRaw);
                TrashTasks = trashDeserialized ?? [];
            }
            catch (JsonException)
            {
                TrashTasks = [];
            }
        }
    }

    // Filter helpers
    private TodoTask[] GetFilteredTasks() =>
        listNameFilter == null ? TodoTasks : TodoTasks.Where(t => t.ListName == listNameFilter).ToArray();

    private TodoTask[] GetFilteredTrash() =>
        listNameFilter == null ? TrashTasks : TrashTasks.Where(t => t.ListName == listNameFilter).ToArray();

    // Public accessor for TUI and GUI
    public List<TodoTask> GetAllTasks() => GetFilteredTasks().ToList();

    /// <summary>
    /// Gets tasks sorted for display: unchecked first, then checked. Newest first within each group.
    /// </summary>
    public List<TodoTask> GetSortedTasks(bool? filterChecked = null)
    {
        var tasks = GetFilteredTasks();
        var filteredTasks = filterChecked switch
        {
            true => tasks.Where(td => td.IsChecked),
            false => tasks.Where(td => !td.IsChecked),
            null => tasks
        };

        return filteredTasks
            .OrderBy(td => td.IsChecked)
            .ThenByDescending(td => td.CreatedAt)
            .ToList();
    }

    public void AddTodoTask(TodoTask todoTask, bool recordUndo = true)
    {
        if (recordUndo)
        {
            var cmd = new AddTaskCommand { Task = todoTask };
            UndoManager.Instance.RecordCommand(cmd);
        }

        TodoTasks = [todoTask, .. TodoTasks];
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }
    }

    public TodoTask? GetTodoTaskById(string taskId)
    {
        // Always search globally by ID
        return TodoTasks.FirstOrDefault(task => task.Id == taskId);
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
        TodoTasks = [checkedTask, .. TodoTasks];
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
        TodoTasks = [uncheckedTask, .. TodoTasks];
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

        TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];

        if (moveToTrash)
        {
            TrashTasks = [task, .. TrashTasks];
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

            TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
            TrashTasks = [task, .. TrashTasks];
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

            TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
            var checkedTask = todoTask.Check();
            TodoTasks = [checkedTask, .. TodoTasks];
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

            TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
            var uncheckedTask = todoTask.UnCheck();
            TodoTasks = [uncheckedTask, .. TodoTasks];
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

        var taskIdsToRemove = tasksToMove.Select(t => t.Id).ToHashSet();

        TrashTasks = [.. tasksToMove, .. TrashTasks];
        TodoTasks = [.. TodoTasks.Where(t => !taskIdsToRemove.Contains(t.Id))];
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

        TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
        var renamedTask = todoTask.Rename(newDescription);
        TodoTasks = [renamedTask, .. TodoTasks];
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

        TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
        var movedTask = todoTask.MoveToList(targetList);
        TodoTasks = [movedTask, .. TodoTasks];
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }

        return new TaskResult.Success($"Moved task {taskId} from '{sourceList}' to '{targetList}'");
    }

    // Trash methods

    public List<TodoTask> GetTrash()
    {
        return GetFilteredTrash().ToList();
    }

    public TaskResult RestoreFromTrash(string taskId)
    {
        var task = TrashTasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            return new TaskResult.NotFound(taskId);
        }

        TrashTasks = [.. TrashTasks.Where(t => t.Id != taskId)];
        TodoTasks = [task, .. TodoTasks];
        Save();

        return new TaskResult.Success($"Restored task: {taskId}");
    }

    public int ClearTrash()
    {
        var trashToClear = GetFilteredTrash();
        var count = trashToClear.Length;
        var idsToRemove = trashToClear.Select(t => t.Id).ToHashSet();
        TrashTasks = [.. TrashTasks.Where(t => !idsToRemove.Contains(t.Id))];
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
            var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
            var listNames = tasks
                .Select(t => t.ListName)
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
            var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
            return tasks.Any(t => t.ListName == listName);
        }
        catch (JsonException)
        {
            return false;
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
        var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
        var remainingTasks = tasks.Where(t => t.ListName != listName).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(remainingTasks));
        }

        // Also remove from trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
            var trash = JsonSerializer.Deserialize<TodoTask[]>(trashRaw) ?? [];
            var remainingTrash = trash.Where(t => t.ListName != listName).ToArray();
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
        var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
        var updatedTasks = tasks.Select(t =>
            t.ListName == oldName ? t with { ListName = newName } : t
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(updatedTasks));
        }

        // Also update in trash
        if (File.Exists(StoragePaths.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(StoragePaths.AllTrashPath);
            var trash = JsonSerializer.Deserialize<TodoTask[]>(trashRaw) ?? [];
            var updatedTrash = trash.Select(t =>
                t.ListName == oldName ? t with { ListName = newName } : t
            ).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(StoragePaths.AllTrashPath, JsonSerializer.Serialize(updatedTrash));
            }
        }
    }

    private void Save()
    {
        StoragePaths.EnsureDirectory();
        lock (SaveLock)
        {
            var tasksJson = JsonSerializer.Serialize(TodoTasks);
            File.WriteAllText(StoragePaths.AllTasksPath, tasksJson);

            var trashJson = JsonSerializer.Serialize(TrashTasks);
            File.WriteAllText(StoragePaths.AllTrashPath, trashJson);
        }
    }
}
