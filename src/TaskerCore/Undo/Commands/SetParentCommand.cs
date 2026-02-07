namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record SetParentCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required string? OldParentId { get; init; }
    public required string? NewParentId { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => NewParentId != null
        ? $"Set parent: {TaskId} → {NewParentId}"
        : $"Remove parent: {TaskId}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        if (NewParentId != null)
            taskList.SetParent(TaskId, NewParentId, recordUndo: false);
        else
            taskList.UnsetParent(TaskId, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        if (OldParentId != null)
            taskList.SetParent(TaskId, OldParentId, recordUndo: false);
        else
            taskList.UnsetParent(TaskId, recordUndo: false);
    }
}
