namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskStatus = TaskerCore.Models.TaskStatus;

public record SetStatusCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required TaskStatus OldStatus { get; init; }
    public required TaskStatus NewStatus { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Status: {TaskId} → {NewStatus}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.SetStatus(TaskId, NewStatus, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.SetStatus(TaskId, OldStatus, recordUndo: false);
    }
}
