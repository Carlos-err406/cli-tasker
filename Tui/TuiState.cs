namespace cli_tasker.Tui;

public enum TuiMode
{
    Normal,
    Search,
    MultiSelect
}

public record TuiState
{
    public TuiMode Mode { get; init; } = TuiMode.Normal;
    public int CursorIndex { get; init; } = 0;
    public string? CurrentList { get; init; } = null; // null = all lists
    public string? SearchQuery { get; init; } = null;
    public HashSet<string> SelectedTaskIds { get; init; } = new();
    public string? StatusMessage { get; init; } = null;
    public DateTime? StatusMessageTime { get; init; } = null;

    public TuiState WithStatusMessage(string message) => this with
    {
        StatusMessage = message,
        StatusMessageTime = DateTime.Now
    };

    public TuiState ClearStatusIfExpired(TimeSpan expiry)
    {
        if (StatusMessageTime == null || StatusMessage == null)
            return this;

        if (DateTime.Now - StatusMessageTime > expiry)
            return this with { StatusMessage = null, StatusMessageTime = null };

        return this;
    }
}
