namespace TaskerCore.Undo;

public static class UndoConfig
{
    public const int MaxUndoStackSize = 50;
    public const int MaxRedoStackSize = 50;
    public const int HistoryRetentionDays = 30;
    public const bool PersistAcrossSessions = true;

    public static string HistoryPath => StoragePaths.Current.UndoHistoryPath;
    public static string TasksPath => StoragePaths.Current.AllTasksPath;
}
