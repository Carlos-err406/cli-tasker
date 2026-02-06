namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;
using TaskerCore.Exceptions;

public record RenameListCommand : IUndoableCommand
{
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public required bool WasDefaultList { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Rename list: {OldName} to {NewName}";

    public void Execute()
    {
        // Re-apply the rename (for redo)
        ListManager.RenameList(OldName, NewName, recordUndo: false);
    }

    public void Undo()
    {
        // Reverse the rename
        try
        {
            ListManager.RenameList(NewName, OldName, recordUndo: false);

            // Restore default list if it was changed
            var services = TaskerServices.Default;
            if (WasDefaultList && services.Config.GetDefaultList() == NewName)
            {
                services.Config.SetDefaultList(OldName);
            }
        }
        catch (ListNotFoundException)
        {
            throw new InvalidOperationException($"Cannot undo: list '{NewName}' no longer exists");
        }
        catch (ListAlreadyExistsException)
        {
            throw new InvalidOperationException($"Cannot undo: list '{OldName}' already exists");
        }
    }
}
