namespace cli_tasker.Undo;

public static class UndoConfig
{
    public const int MaxUndoStackSize = 50;
    public const int MaxRedoStackSize = 50;
    public const int HistoryRetentionDays = 30;
    public const bool PersistAcrossSessions = true;

    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

    public static readonly string HistoryPath = Path.Combine(Directory, "undo-history.json");
    public static readonly string TasksPath = Path.Combine(Directory, "all-tasks.json");
}
