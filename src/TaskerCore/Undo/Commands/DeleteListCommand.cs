namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Models;

public record DeleteListCommand : IUndoableCommand
{
    public required string ListName { get; init; }
    public required TaskList DeletedList { get; init; }
    public TaskList? TrashedList { get; init; }
    public required bool WasDefaultList { get; init; }
    public required int OriginalIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Delete list: {ListName}";

    public void Execute()
    {
        ListManager.DeleteList(ListName, recordUndo: false);
    }

    public void Undo()
    {
        // Restore list with tasks at original position
        TodoTaskList.RestoreList(DeletedList, TrashedList, OriginalIndex);

        // Restore default list if it was the default
        if (WasDefaultList)
        {
            TaskerServices.Default.Config.SetDefaultList(ListName);
        }
    }
}
