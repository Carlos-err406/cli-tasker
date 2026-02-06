namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Results;

public record DeleteTaskCommand : IUndoableCommand
{
    public required TodoTask DeletedTask { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Delete: {StringHelpers.Truncate(DeletedTask.Description, 30)}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(DeletedTask.Id, save: true, moveToTrash: true, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();

        // Try to un-trash first (task still exists with is_trashed=1)
        var result = taskList.RestoreFromTrash(DeletedTask.Id);
        if (result is TaskResult.NotFound)
        {
            // Trash was cleared — re-insert from captured state
            taskList.AddTodoTask(DeletedTask, recordUndo: false);
        }
    }
}
