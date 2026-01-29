namespace cli_tasker;

using System.Text.Json;

class TodoTaskList
{
    private TodoTask[] TodoTasks { get; set; } = [];
    private readonly string filePath;

    public TodoTaskList(string? listName = null)
    {
        var name = listName ?? ListManager.DefaultListName;
        filePath = ListManager.GetFilePath(name);

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
            Console.WriteLine($"Error reading tasks file: {ex.Message}");
            TodoTasks = [];
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
            Console.WriteLine($"Could not find task with id {taskId}");
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
            Console.WriteLine($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId);
        var uncheckedTask = todoTask.UnCheck();
        AddTodoTask(uncheckedTask);
    }

    public void DeleteTask(string taskId, bool save = true)
    {
        var originalLength = TodoTasks.Length;
        TodoTasks = [.. TodoTasks.Where(task => task.Id != taskId)];

        if (TodoTasks.Length == originalLength)
        {
            Console.WriteLine($"Could not find task with id {taskId}");
            return;
        }

        if (save) Save();
    }

    public void DeleteTasks(string[] taskIds)
    {
        foreach (var taskId in taskIds)
        {
            var originalLength = TodoTasks.Length;
            TodoTasks = [.. TodoTasks.Where(task => task.Id != taskId)];

            if (TodoTasks.Length == originalLength)
            {
                Console.WriteLine($"Could not find task with id {taskId}");
            }
            else
            {
                Console.WriteLine($"Deleted task: {taskId}");
            }
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
                Console.WriteLine($"Could not find task with id {taskId}");
                continue;
            }
            DeleteTask(taskId, false);
            var checkedTask = todoTask.Check();
            TodoTasks = [checkedTask, .. TodoTasks];
            Console.WriteLine($"Checked task: {taskId}");
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
                Console.WriteLine($"Could not find task with id {taskId}");
                continue;
            }
            DeleteTask(taskId, false);
            var uncheckedTask = todoTask.UnCheck();
            TodoTasks = [uncheckedTask, .. TodoTasks];
            Console.WriteLine($"Unchecked task: {taskId}");
        }
        Save();
    }

    public void ClearTasks()
    {
        TodoTasks = [];
        Save();
    }

    public void RenameTask(string taskId, string newDescription)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Console.WriteLine($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId, false);
        var renamedTask = todoTask.Rename(newDescription);
        TodoTasks = [renamedTask, .. TodoTasks];
        Console.WriteLine($"Renamed task: {taskId}");
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
            Console.WriteLine(message);
            return;
        }
        foreach (var td in taskList)
        {
            Console.WriteLine($"({td.Id}) {(td.IsChecked ? "[x]" : "[ ]")} - {td.Description}");
        }
    }

    private void Save()
    {
        ListManager.EnsureDirectory();
        var serialized = JsonSerializer.Serialize(TodoTasks);
        File.WriteAllText(filePath, serialized);
    }
}