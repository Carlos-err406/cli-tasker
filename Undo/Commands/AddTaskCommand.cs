namespace cli_tasker.Undo.Commands;

public record AddTaskCommand : IUndoableCommand
{
    public required TodoTask Task { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Add: {Truncate(Task.Description, 30)}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.AddTodoTask(Task, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(Task.Id, save: true, moveToTrash: false, recordUndo: false);
    }

    private static string Truncate(string text, int maxLength)
    {
        var firstLine = text.Split('\n')[0];
        return firstLine.Length <= maxLength ? firstLine : firstLine[..maxLength] + "...";
    }
}
