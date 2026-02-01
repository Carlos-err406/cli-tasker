namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;

public record AddTaskCommand : IUndoableCommand
{
    public required TodoTask Task { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Add: {StringHelpers.Truncate(Task.Description, 30)}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.AddTodoTask(Task, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(Task.Id, save: true, moveToTrash: false, recordUndo: false);
    }
}
