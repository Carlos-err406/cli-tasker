namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record RenameTaskCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required string OldDescription { get; init; }
    public required string NewDescription { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Rename: {TaskId}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.RenameTask(TaskId, NewDescription, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.RenameTask(TaskId, OldDescription, recordUndo: false);
    }
}
