namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;

public record DeleteTaskCommand : IUndoableCommand
{
    public required TodoTask DeletedTask { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Delete: {StringHelpers.Truncate(DeletedTask.Description, 30)}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(DeletedTask.Id, save: true, moveToTrash: true, recordUndo: false);
    }

    public void Undo()
    {
        // Restore from captured state (not trash - trash may be cleared)
        var taskList = new TodoTaskList();
        taskList.AddTodoTask(DeletedTask, recordUndo: false);
    }
}
