namespace cli_tasker;

using System.Text.Json;
using Spectre.Console;
using cli_tasker.Undo;
using cli_tasker.Undo.Commands;

class TodoTaskList
{
    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

    private static readonly string AllTasksPath = Path.Combine(Directory, "all-tasks.json");
    private static readonly string AllTrashPath = Path.Combine(Directory, "all-tasks.trash.json");
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
        EnsureDirectory();

        // Load all tasks
        if (File.Exists(AllTasksPath))
        {
            try
            {
                var raw = File.ReadAllText(AllTasksPath);
                var deserialized = JsonSerializer.Deserialize<TodoTask[]>(raw);
                TodoTasks = deserialized ?? [];
            }
            catch (JsonException ex)
            {
                Output.Error($"Error reading tasks file: {ex.Message}");
                TodoTasks = [];
            }
        }

        // Load all trash
        if (File.Exists(AllTrashPath))
        {
            try
            {
                var trashRaw = File.ReadAllText(AllTrashPath);
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

    // Public accessor for TUI
    public List<TodoTask> GetAllTasks() => GetFilteredTasks().ToList();

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

    public void CheckTask(string taskId, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
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
    }

    public void UncheckTask(string taskId, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
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
    }

    public void DeleteTask(string taskId, bool save = true, bool moveToTrash = true, bool recordUndo = true)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
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
    }

    public void DeleteTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Delete {taskIds.Length} tasks");
        }

        foreach (var taskId in taskIds)
        {
            var task = GetTodoTaskById(taskId);
            if (task == null)
            {
                Output.Error($"Could not find task with id {taskId}");
                continue;
            }

            if (recordUndo)
            {
                var cmd = new DeleteTaskCommand { DeletedTask = task };
                UndoManager.Instance.RecordCommand(cmd);
            }

            TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
            TrashTasks = [task, .. TrashTasks];
            Output.Success($"Deleted task: {taskId}");
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
    }

    public void CheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Check {taskIds.Length} tasks");
        }

        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                Output.Error($"Could not find task with id {taskId}");
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
            Output.Success($"Checked task: {taskId}");
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
    }

    public void UncheckTasks(string[] taskIds, bool recordUndo = true)
    {
        if (recordUndo)
        {
            UndoManager.Instance.BeginBatch($"Uncheck {taskIds.Length} tasks");
        }

        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                Output.Error($"Could not find task with id {taskId}");
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
            Output.Success($"Unchecked task: {taskId}");
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
    }

    public void ClearTasks(bool recordUndo = true)
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
    }

    public void RenameTask(string taskId, string newDescription, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
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
        Output.Success($"Renamed task: {taskId}");
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }
    }

    public void MoveTask(string taskId, string targetList, bool recordUndo = true)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
        }

        if (todoTask.ListName == targetList)
        {
            Output.Info($"Task is already in '{targetList}'");
            return;
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
        Output.Success($"Moved task {taskId} from '{sourceList}' to '{targetList}'");
        Save();

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
        }
    }

    public void ListTodoTasks(bool? filterChecked = null)
    {
        var tasks = GetFilteredTasks();
        var filteredTasks = filterChecked switch
        {
            true => tasks.Where(td => td.IsChecked),
            false => tasks.Where(td => !td.IsChecked),
            null => tasks
        };

        var taskList = filteredTasks
            .OrderBy(td => td.IsChecked)       // Unchecked tasks first
            .ThenByDescending(td => td.CreatedAt)  // Newest first within each group
            .ToArray();

        if (taskList.Length == 0)
        {
            var message = filterChecked switch
            {
                true => "No checked tasks found",
                false => "No unchecked tasks found",
                null => "No tasks saved yet... use the add command to create one"
            };
            Output.Info(message);
            return;
        }
        foreach (var td in taskList)
        {
            var indent = new string(' ', AppConfig.TaskPrefixLength);
            var lines = td.Description.Split('\n');
            var firstLine = $"[bold]{Markup.Escape(lines[0])}[/]";
            var restLines = lines.Length > 1
                ? "\n" + indent + "[dim]" + string.Join("\n" + indent, lines.Skip(1).Select(Markup.Escape)) + "[/]"
                : "";

            var checkbox = td.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";
            var taskId = $"[dim]({td.Id})[/]";
            Output.Markup($"{taskId} {checkbox} - {firstLine}{restLines}");
        }
    }

    // Trash methods

    public void ListTrash()
    {
        var trash = GetFilteredTrash();
        if (trash.Length == 0)
        {
            Output.Info("Trash is empty");
            return;
        }

        foreach (var td in trash)
        {
            var indent = new string(' ', AppConfig.TaskPrefixLength);
            var lines = td.Description.Split('\n');
            var firstLine = $"[bold]{Markup.Escape(lines[0])}[/]";
            var restLines = lines.Length > 1
                ? "\n" + indent + "[dim]" + string.Join("\n" + indent, lines.Skip(1).Select(Markup.Escape)) + "[/]"
                : "";

            var checkbox = td.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";
            var taskId = $"[dim]({td.Id})[/]";
            Output.Markup($"{taskId} {checkbox} - {firstLine}{restLines}");
        }
    }

    public void RestoreFromTrash(string taskId)
    {
        var task = TrashTasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            Output.Error($"Could not find task with id {taskId} in trash");
            return;
        }

        TrashTasks = [.. TrashTasks.Where(t => t.Id != taskId)];
        TodoTasks = [task, .. TodoTasks];
        Output.Success($"Restored task: {taskId}");
        Save();
    }

    public int ClearTrash(bool silent = false)
    {
        var trashToClear = GetFilteredTrash();
        var count = trashToClear.Length;
        var idsToRemove = trashToClear.Select(t => t.Id).ToHashSet();
        TrashTasks = [.. TrashTasks.Where(t => !idsToRemove.Contains(t.Id))];
        Save();
        if (!silent)
        {
            Output.Success($"Permanently deleted {count} task(s) from trash");
        }
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
        EnsureDirectory();
        if (!File.Exists(AllTasksPath))
        {
            return [ListManager.DefaultListName];
        }

        try
        {
            var raw = File.ReadAllText(AllTasksPath);
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
        if (!File.Exists(AllTasksPath))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(AllTasksPath);
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
        EnsureDirectory();
        if (!File.Exists(AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(AllTasksPath);
        var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
        var remainingTasks = tasks.Where(t => t.ListName != listName).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(AllTasksPath, JsonSerializer.Serialize(remainingTasks));
        }

        // Also remove from trash
        if (File.Exists(AllTrashPath))
        {
            var trashRaw = File.ReadAllText(AllTrashPath);
            var trash = JsonSerializer.Deserialize<TodoTask[]>(trashRaw) ?? [];
            var remainingTrash = trash.Where(t => t.ListName != listName).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(AllTrashPath, JsonSerializer.Serialize(remainingTrash));
            }
        }
    }

    public static void RenameList(string oldName, string newName)
    {
        EnsureDirectory();
        if (!File.Exists(AllTasksPath))
        {
            return;
        }

        var raw = File.ReadAllText(AllTasksPath);
        var tasks = JsonSerializer.Deserialize<TodoTask[]>(raw) ?? [];
        var updatedTasks = tasks.Select(t =>
            t.ListName == oldName ? t with { ListName = newName } : t
        ).ToArray();

        lock (SaveLock)
        {
            File.WriteAllText(AllTasksPath, JsonSerializer.Serialize(updatedTasks));
        }

        // Also update in trash
        if (File.Exists(AllTrashPath))
        {
            var trashRaw = File.ReadAllText(AllTrashPath);
            var trash = JsonSerializer.Deserialize<TodoTask[]>(trashRaw) ?? [];
            var updatedTrash = trash.Select(t =>
                t.ListName == oldName ? t with { ListName = newName } : t
            ).ToArray();
            lock (SaveLock)
            {
                File.WriteAllText(AllTrashPath, JsonSerializer.Serialize(updatedTrash));
            }
        }
    }

    private void Save()
    {
        EnsureDirectory();
        lock (SaveLock)
        {
            var tasksJson = JsonSerializer.Serialize(TodoTasks);
            File.WriteAllText(AllTasksPath, tasksJson);

            var trashJson = JsonSerializer.Serialize(TrashTasks);
            File.WriteAllText(AllTrashPath, trashJson);
        }
    }

    private static void EnsureDirectory()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
    }
}
