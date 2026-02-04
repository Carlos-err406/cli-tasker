namespace TaskerCore.Models;

/// <summary>
/// Represents a named list containing tasks. Lists are first-class entities that can exist even when empty.
/// </summary>
public record TaskList(string ListName, TodoTask[] Tasks)
{
    public static TaskList Create(string listName) => new(listName, []);

    public TaskList AddTask(TodoTask task) => this with { Tasks = [task, ..Tasks] };

    public TaskList RemoveTask(string taskId) => this with
    {
        Tasks = Tasks.Where(t => t.Id != taskId).ToArray()
    };

    public TaskList UpdateTask(TodoTask updatedTask) => this with
    {
        Tasks = Tasks.Select(t => t.Id == updatedTask.Id ? updatedTask : t).ToArray()
    };

    public TaskList ReplaceTasks(TodoTask[] newTasks) => this with { Tasks = newTasks };
}
