namespace cli_tasker.Undo.Commands;

public record DeleteTaskCommand : IUndoableCommand
{
    public required TodoTask DeletedTask { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Delete: {Truncate(DeletedTask.Description, 30)}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(DeletedTask.Id, save: true, moveToTrash: true, recordUndo: false);
    }

    public void Undo()
    {
        // Restore from captured state (not trash - trash may be cleared)
        var taskList = new TodoTaskList();
        taskList.AddTodoTask(DeletedTask, recordUndo: false);
    }

    private static string Truncate(string text, int maxLength)
    {
        var firstLine = text.Split('\n')[0];
        return firstLine.Length <= maxLength ? firstLine : firstLine[..maxLength] + "...";
    }
}
