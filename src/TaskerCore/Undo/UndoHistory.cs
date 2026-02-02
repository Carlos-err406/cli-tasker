namespace TaskerCore.Undo;

public record UndoHistory
{
    public int Version { get; init; } = 1;
    public string TasksChecksum { get; init; } = "";
    public long TasksFileSize { get; init; }
    public List<IUndoableCommand> UndoStack { get; init; } = [];
    public List<IUndoableCommand> RedoStack { get; init; } = [];
    public DateTime SavedAt { get; init; } = DateTime.Now;
}
