namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;

public record TaskMetadataChangedCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public DateOnly? OldDueDate { get; init; }
    public DateOnly? NewDueDate { get; init; }
    public Priority? OldPriority { get; init; }
    public Priority? NewPriority { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description
    {
        get
        {
            var changes = new List<string>();
            if (OldDueDate != NewDueDate)
                changes.Add(NewDueDate.HasValue ? $"due → {NewDueDate:MMM d}" : "due → cleared");
            if (OldPriority != NewPriority)
                changes.Add(NewPriority.HasValue ? $"priority → {NewPriority}" : "priority → cleared");
            return $"Changed {TaskId}: {string.Join(", ", changes)}";
        }
    }

    public void Execute()
    {
        var taskList = new TodoTaskList();
        if (OldDueDate != NewDueDate)
            taskList.SetTaskDueDate(TaskId, NewDueDate, recordUndo: false);
        if (OldPriority != NewPriority)
            taskList.SetTaskPriority(TaskId, NewPriority, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        if (OldDueDate != NewDueDate)
            taskList.SetTaskDueDate(TaskId, OldDueDate, recordUndo: false);
        if (OldPriority != NewPriority)
            taskList.SetTaskPriority(TaskId, OldPriority, recordUndo: false);
    }
}
