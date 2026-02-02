namespace TaskerCore.Undo.Commands;

public record CompositeCommand : IUndoableCommand
{
    public required string BatchDescription { get; init; }
    public required List<IUndoableCommand> Commands { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => BatchDescription;

    public void Execute()
    {
        foreach (var cmd in Commands)
        {
            cmd.Execute();
        }
    }

    public void Undo()
    {
        // Undo in REVERSE order
        foreach (var cmd in Commands.AsEnumerable().Reverse())
        {
            cmd.Undo();
        }
    }
}
