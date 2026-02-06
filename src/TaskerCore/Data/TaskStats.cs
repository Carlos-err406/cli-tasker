namespace TaskerCore.Data;

/// <summary>
/// Statistics about tasks in a list or across all lists.
/// </summary>
public record TaskStats
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int InProgress { get; init; }
    public int Done { get; init; }
    public int Trash { get; init; }
}
