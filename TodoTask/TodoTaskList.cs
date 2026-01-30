namespace cli_tasker;

using System.Text.Json;
using Spectre.Console;

class TodoTaskList
{
    private TodoTask[] TodoTasks { get; set; } = [];
    private TodoTask[] TrashTasks { get; set; } = [];
    private readonly string filePath;
    private readonly string trashFilePath;

    public TodoTaskList(string? listName = null)
    {
        var name = listName ?? ListManager.DefaultListName;
        filePath = ListManager.GetFilePath(name);
        trashFilePath = ListManager.GetTrashFilePath(name);

        ListManager.EnsureDirectory();

        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, "[]");
        }

        try
        {
            var raw = File.ReadAllText(filePath);
            var deserialized = JsonSerializer.Deserialize<TodoTask[]>(raw);
            TodoTasks = deserialized ?? [];
        }
        catch (JsonException ex)
        {
            Output.Error($"Error reading tasks file: {ex.Message}");
            TodoTasks = [];
        }

        // Load trash
        if (File.Exists(trashFilePath))
        {
            try
            {
                var trashRaw = File.ReadAllText(trashFilePath);
                var trashDeserialized = JsonSerializer.Deserialize<TodoTask[]>(trashRaw);
                TrashTasks = trashDeserialized ?? [];
            }
            catch (JsonException)
            {
                TrashTasks = [];
            }
        }
    }

    public void AddTodoTask(TodoTask todoTask)
    {
        TodoTasks = [todoTask, .. TodoTasks];
        Save();
    }

    public TodoTask? GetTodoTaskById(string taskId)
    {
        var todoTask = TodoTasks.FirstOrDefault((task) => task?.Id == taskId, null);
        return todoTask;
    }

    public void CheckTask(string taskId)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId, false);
        var checkedTask = todoTask.Check();
        AddTodoTask(checkedTask);
    }

    public void UncheckTask(string taskId)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId);
        var uncheckedTask = todoTask.UnCheck();
        AddTodoTask(uncheckedTask);
    }

    public void DeleteTask(string taskId, bool save = true, bool moveToTrash = true)
    {
        var task = GetTodoTaskById(taskId);
        if (task == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
        }

        TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];

        if (moveToTrash)
        {
            TrashTasks = [task, .. TrashTasks];
        }

        if (save) Save();
    }

    public void DeleteTasks(string[] taskIds)
    {
        foreach (var taskId in taskIds)
        {
            var task = GetTodoTaskById(taskId);
            if (task == null)
            {
                Output.Error($"Could not find task with id {taskId}");
                continue;
            }

            TodoTasks = [.. TodoTasks.Where(t => t.Id != taskId)];
            TrashTasks = [task, .. TrashTasks];
            Output.Success($"Deleted task: {taskId}");
        }
        Save();
    }

    public void CheckTasks(string[] taskIds)
    {
        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                Output.Error($"Could not find task with id {taskId}");
                continue;
            }
            DeleteTask(taskId, save: false, moveToTrash: false);
            var checkedTask = todoTask.Check();
            TodoTasks = [checkedTask, .. TodoTasks];
            Output.Success($"Checked task: {taskId}");
        }
        Save();
    }

    public void UncheckTasks(string[] taskIds)
    {
        foreach (var taskId in taskIds)
        {
            var todoTask = GetTodoTaskById(taskId);
            if (todoTask == null)
            {
                Output.Error($"Could not find task with id {taskId}");
                continue;
            }
            DeleteTask(taskId, save: false, moveToTrash: false);
            var uncheckedTask = todoTask.UnCheck();
            TodoTasks = [uncheckedTask, .. TodoTasks];
            Output.Success($"Unchecked task: {taskId}");
        }
        Save();
    }

    public void ClearTasks()
    {
        TrashTasks = [.. TodoTasks, .. TrashTasks];
        TodoTasks = [];
        Save();
    }

    public void RenameTask(string taskId, string newDescription)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Output.Error($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId, save: false, moveToTrash: false);
        var renamedTask = todoTask.Rename(newDescription);
        TodoTasks = [renamedTask, .. TodoTasks];
        Output.Success($"Renamed task: {taskId}");
        Save();
    }

    public void ListTodoTasks(bool? filterChecked = null)
    {
        var filteredTasks = filterChecked switch
        {
            true => TodoTasks.Where(td => td.IsChecked),
            false => TodoTasks.Where(td => !td.IsChecked),
            null => TodoTasks
        };

        var taskList = filteredTasks.OrderBy(td => td.CreatedAt).ToArray();

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
        if (TrashTasks.Length == 0)
        {
            Output.Info("Trash is empty");
            return;
        }

        foreach (var td in TrashTasks)
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
        var count = TrashTasks.Length;
        TrashTasks = [];
        Save();
        if (!silent)
        {
            Output.Success($"Permanently deleted {count} task(s) from trash");
        }
        return count;
    }

    public TaskStats GetStats()
    {
        return new TaskStats
        {
            Total = TodoTasks.Length,
            Checked = TodoTasks.Count(t => t.IsChecked),
            Unchecked = TodoTasks.Count(t => !t.IsChecked),
            Trash = TrashTasks.Length
        };
    }

    private void Save()
    {
        ListManager.EnsureDirectory();
        var serialized = JsonSerializer.Serialize(TodoTasks);
        File.WriteAllText(filePath, serialized);

        var trashSerialized = JsonSerializer.Serialize(TrashTasks);
        File.WriteAllText(trashFilePath, trashSerialized);
    }
}