namespace TaskerCore.Undo;

using System.Security.Cryptography;
using System.Text.Json;
using TaskerCore.Undo.Commands;

public sealed class UndoManager
{
    private static readonly Lazy<UndoManager> _instance = new(() => new UndoManager());
    public static UndoManager Instance => _instance.Value;

    private static readonly object SaveLock = new();

    private List<IUndoableCommand> _undoStack = [];
    private List<IUndoableCommand> _redoStack = [];
    private CompositeCommand? _currentBatch;

    private UndoManager()
    {
        LoadHistory();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public IReadOnlyList<IUndoableCommand> UndoHistory => _undoStack.AsReadOnly();
    public IReadOnlyList<IUndoableCommand> RedoHistory => _redoStack.AsReadOnly();

    public void RecordCommand(IUndoableCommand command)
    {
        if (_currentBatch != null)
        {
            _currentBatch = _currentBatch with
            {
                Commands = [.. _currentBatch.Commands, command]
            };
        }
        else
        {
            _undoStack.Insert(0, command);
            _redoStack.Clear();
            EnforceSizeLimit();
            // Don't save immediately - wait for SaveHistory() call after task file is saved
        }
    }

    public void SaveHistory()
    {
        Save();
    }

    public void BeginBatch(string description)
    {
        _currentBatch = new CompositeCommand
        {
            BatchDescription = description,
            Commands = []
        };
    }

    public void EndBatch()
    {
        if (_currentBatch != null && _currentBatch.Commands.Count > 0)
        {
            _undoStack.Insert(0, _currentBatch);
            _redoStack.Clear();
            EnforceSizeLimit();
            // Don't save immediately - wait for SaveHistory() call after task file is saved
        }
        _currentBatch = null;
    }

    public void CancelBatch()
    {
        _currentBatch = null;
    }

    public string? Undo()
    {
        if (_undoStack.Count == 0)
            return null;

        var command = _undoStack[0];
        _undoStack.RemoveAt(0);

        command.Undo();

        _redoStack.Insert(0, command);
        Save();

        return command.Description;
    }

    public string? Redo()
    {
        if (_redoStack.Count == 0)
            return null;

        var command = _redoStack[0];
        _redoStack.RemoveAt(0);

        command.Execute();

        _undoStack.Insert(0, command);
        Save();

        return command.Description;
    }

    public void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Save();
    }

    /// <summary>
    /// Reloads history from disk. Use this when external changes are detected
    /// to sync with history saved by another process (e.g., CLI).
    /// </summary>
    public void ReloadHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        LoadHistory();
    }

    private void EnforceSizeLimit()
    {
        while (_undoStack.Count > UndoConfig.MaxUndoStackSize)
        {
            _undoStack.RemoveAt(_undoStack.Count - 1);
        }
        while (_redoStack.Count > UndoConfig.MaxRedoStackSize)
        {
            _redoStack.RemoveAt(_redoStack.Count - 1);
        }
    }

    private void LoadHistory()
    {
        if (!UndoConfig.PersistAcrossSessions || !File.Exists(UndoConfig.HistoryPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(UndoConfig.HistoryPath);
            var history = JsonSerializer.Deserialize<UndoHistory>(json, GetJsonOptions());

            if (history == null)
            {
                return;
            }

            if (!ValidateChecksum(history))
            {
                // Tasks changed externally - history is invalid
                return;
            }

            _undoStack = history.UndoStack.ToList();
            _redoStack = history.RedoStack.ToList();
            CleanupOldHistory();
        }
        catch (JsonException)
        {
            // Corrupted history file - start fresh
        }
    }

    private void Save()
    {
        StoragePaths.Current.EnsureDirectory();

        var history = new UndoHistory
        {
            UndoStack = _undoStack,
            RedoStack = _redoStack,
            TasksChecksum = ComputeChecksum(),
            TasksFileSize = GetTasksFileSize()
        };

        lock (SaveLock)
        {
            var json = JsonSerializer.Serialize(history, GetJsonOptions());
            File.WriteAllText(UndoConfig.HistoryPath, json);
        }
    }

    private bool ValidateChecksum(UndoHistory history)
    {
        return ComputeChecksum() == history.TasksChecksum
            && GetTasksFileSize() == history.TasksFileSize;
    }

    private static string ComputeChecksum()
    {
        if (!File.Exists(UndoConfig.TasksPath))
            return "";

        using var md5 = MD5.Create();
        using var stream = File.OpenRead(UndoConfig.TasksPath);
        return Convert.ToHexString(md5.ComputeHash(stream));
    }

    private static long GetTasksFileSize()
    {
        return File.Exists(UndoConfig.TasksPath) ? new FileInfo(UndoConfig.TasksPath).Length : 0;
    }

    private void CleanupOldHistory()
    {
        var cutoff = DateTime.Now.AddDays(-UndoConfig.HistoryRetentionDays);
        _undoStack = _undoStack.Where(c => c.ExecutedAt > cutoff).ToList();
        _redoStack = _redoStack.Where(c => c.ExecutedAt > cutoff).ToList();
    }

    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        WriteIndented = true
    };
}
