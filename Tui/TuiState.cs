namespace cli_tasker.Tui;

public enum TuiMode
{
    Normal,
    Search,
    MultiSelect,
    InputAdd,
    InputRename
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

    // Input mode state
    public string InputBuffer { get; init; } = "";
    public int InputCursor { get; init; } = 0; // cursor position within buffer
    public string? InputTargetTaskId { get; init; } = null; // for rename

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

    public TuiState StartInputAdd(string listName) => this with
    {
        Mode = TuiMode.InputAdd,
        InputBuffer = "",
        InputCursor = 0,
        StatusMessage = $"Adding to: {listName} (Esc to cancel)"
    };

    public TuiState StartInputRename(string taskId, string currentDescription) => this with
    {
        Mode = TuiMode.InputRename,
        InputBuffer = currentDescription,
        InputCursor = currentDescription.Length,
        InputTargetTaskId = taskId,
        StatusMessage = "Editing (Esc to cancel)"
    };

    public TuiState CancelInput() => this with
    {
        Mode = TuiMode.Normal,
        InputBuffer = "",
        InputCursor = 0,
        InputTargetTaskId = null,
        StatusMessage = "Cancelled"
    };
}
