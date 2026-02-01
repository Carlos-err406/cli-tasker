namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;

public record ClearTasksCommand : IUndoableCommand
{
    public required string? ListName { get; init; }
    public required TodoTask[] ClearedTasks { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Clear: {ClearedTasks.Length} tasks from {ListName ?? "all lists"}";

    public void Execute()
    {
        var taskList = new TodoTaskList(ListName);
        taskList.ClearTasks(recordUndo: false);
    }

    public void Undo()
    {
        // Restore all cleared tasks
        var taskList = new TodoTaskList();
        foreach (var task in ClearedTasks)
        {
            taskList.AddTodoTask(task, recordUndo: false);
        }
    }
}
