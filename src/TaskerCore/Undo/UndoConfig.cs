namespace TaskerCore.Undo;

/// <summary>
/// Constants for undo system configuration.
/// </summary>
public static class UndoConfig
{
    public const int MaxUndoStackSize = 50;
    public const int MaxRedoStackSize = 50;
    public const int HistoryRetentionDays = 30;
    public const bool PersistAcrossSessions = true;
}
