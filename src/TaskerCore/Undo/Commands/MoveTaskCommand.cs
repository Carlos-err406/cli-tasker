namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record MoveTaskCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required string SourceList { get; init; }
    public required string TargetList { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Move: {TaskId} to {TargetList}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.MoveTask(TaskId, TargetList, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.MoveTask(TaskId, SourceList, recordUndo: false);
    }
}
