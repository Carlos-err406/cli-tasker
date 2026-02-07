namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record RemoveRelatedCommand : IUndoableCommand
{
    public required string TaskId1 { get; init; }
    public required string TaskId2 { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Remove related: {TaskId1} ↔ {TaskId2}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.RemoveRelated(TaskId1, TaskId2, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.AddRelated(TaskId1, TaskId2, recordUndo: false);
    }
}
