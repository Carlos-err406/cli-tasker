namespace TaskerCore.Data;

/// <summary>
/// Statistics about tasks in a list or across all lists.
/// </summary>
public record TaskStats
{
    public int Total { get; init; }
    public int Checked { get; init; }
    public int Unchecked { get; init; }
    public int Trash { get; init; }
}
