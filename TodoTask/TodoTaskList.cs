namespace cli_tasker;

using System.Text.Json;

class TodoTaskList
{
    public TodoTask[] TodoTasks { get; set; } = [];
    private static readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cli-tasker");
    private static readonly string filePath = Path.Combine(directory, "tasks.json");

    public TodoTaskList()
    {
        EnsureFilePath();
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

    public void ListTodoTasks()
    {
        if (TodoTasks.Length == 0)
        {
            Console.WriteLine("No tasks saved yet... use the add command to create one");
            return;
        }
        foreach (var td in TodoTasks.OrderBy((td) => td.CreatedAt))
        {
            Console.WriteLine($"({td.Id}) {(td.IsChecked ? "[x]" : "[ ]")} - {td.Description}");
        }
    }

    private void Save()
    {
        EnsureFilePath();
        var serialized = JsonSerializer.Serialize(TodoTasks);
        File.WriteAllText(filePath, serialized);
    }

    private static void EnsureFilePath()
    {
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"App directory does not exist ({directory}), creating...");
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Tasks file does not exist ({filePath}), creating empty tasks file...");
            File.WriteAllText(filePath, "[]");
        }
    }
}