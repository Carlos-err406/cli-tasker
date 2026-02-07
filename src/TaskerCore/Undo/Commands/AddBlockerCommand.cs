namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record AddBlockerCommand : IUndoableCommand
{
    public required string BlockerId { get; init; }
    public required string BlockedId { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Add blocker: {BlockerId} blocks {BlockedId}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.AddBlocker(BlockerId, BlockedId, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.RemoveBlocker(BlockerId, BlockedId, recordUndo: false);
    }
}
