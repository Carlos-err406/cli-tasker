namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record UncheckTaskCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required bool WasChecked { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Uncheck: {TaskId}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.UncheckTask(TaskId, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        if (WasChecked)
        {
            taskList.CheckTask(TaskId, recordUndo: false);
        }
        else
        {
            taskList.UncheckTask(TaskId, recordUndo: false);
        }
    }
}
