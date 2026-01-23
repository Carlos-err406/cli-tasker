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
        TodoTasks = [.. TodoTasks, todoTask];
        Save();
    }
    private TodoTask? GetTodoTaskById(string taskId)
    {
        var todoTask = TodoTasks.FirstOrDefault((task) => task?.Id == taskId, null);
        return todoTask;
    }
    public void CompleteTask(string taskId)
    {
        var todoTask = GetTodoTaskById(taskId);
        if (todoTask == null)
        {
            Console.WriteLine($"Could not find task with id {taskId}");
            return;
        }
        DeleteTask(taskId, false);
        var completedTask = new TodoTask(todoTask.Id, todoTask.Description, true, todoTask.CreatedAt);
        AddTodoTask(completedTask);

    }
    public void DeleteTask(string taskId, bool save = true)
    {
        var exists = GetTodoTaskById(taskId);
        if (exists == null)
        {
            Console.WriteLine($"Could not find task with id {taskId}");
            return;
        }
        TodoTasks = [.. TodoTasks.Where(task => task.Id != taskId)];
        if (save)
        {
            Save();
        }
    }
    public void Print()
    {
        if (TodoTasks.Length == 0)
        {
            Console.WriteLine("No tasks saved yet... use the add command to create one");
            return;
        }
        foreach (var td in TodoTasks.OrderBy((td) => td.CreatedAt))
        {
            Console.WriteLine($"({td.Id}) {(td.IsComplete ? "[x]" : "[ ]")} - {td.Description}");
        }
    }

    void Save()
    {
        EnsureFilePath();
        var serialized = JsonSerializer.Serialize(TodoTasks);
        File.WriteAllText(filePath, serialized);
    }

    static void EnsureFilePath()
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