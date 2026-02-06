namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record ReorderTaskCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required string ListName { get; init; }
    public required int OldIndex { get; init; }
    public required int NewIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Reorder task in {ListName}";

    public void Execute()
    {
        TodoTaskList.ReorderTask(TaskId, NewIndex, recordUndo: false);
    }

    public void Undo()
    {
        TodoTaskList.ReorderTask(TaskId, OldIndex, recordUndo: false);
    }
}
