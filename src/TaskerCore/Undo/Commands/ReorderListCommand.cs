namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record ReorderListCommand : IUndoableCommand
{
    public required string ListName { get; init; }
    public required int OldIndex { get; init; }
    public required int NewIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Reorder {ListName} list";

    public void Execute()
    {
        TodoTaskList.ReorderList(ListName, NewIndex, recordUndo: false);
    }

    public void Undo()
    {
        TodoTaskList.ReorderList(ListName, OldIndex, recordUndo: false);
    }
}
