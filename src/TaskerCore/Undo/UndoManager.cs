namespace TaskerCore.Undo;

using System.Text.Json;
using TaskerCore.Data;
using TaskerCore.Undo.Commands;

public sealed class UndoManager
{
    private readonly TaskerDb _db;

    private List<IUndoableCommand> _undoStack = [];
    private List<IUndoableCommand> _redoStack = [];
    private CompositeCommand? _currentBatch;

    public UndoManager(TaskerDb db)
    {
        _db = db;
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
    /// Reloads history from database. Use this when external changes are detected
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
        if (!UndoConfig.PersistAcrossSessions)
            return;

        try
        {
            var cutoff = DateTime.Now.AddDays(-UndoConfig.HistoryRetentionDays);
            var cutoffStr = cutoff.ToString("o");

            _undoStack = _db.Query(
                "SELECT command_json FROM undo_history WHERE stack_type = 'undo' AND created_at > @cutoff ORDER BY id DESC",
                reader =>
                {
                    var json = reader.GetString(0);
                    return JsonSerializer.Deserialize<IUndoableCommand>(json, GetJsonOptions())!;
                },
                ("@cutoff", cutoffStr))
                .ToList();

            _redoStack = _db.Query(
                "SELECT command_json FROM undo_history WHERE stack_type = 'redo' AND created_at > @cutoff ORDER BY id DESC",
                reader =>
                {
                    var json = reader.GetString(0);
                    return JsonSerializer.Deserialize<IUndoableCommand>(json, GetJsonOptions())!;
                },
                ("@cutoff", cutoffStr))
                .ToList();
        }
        catch
        {
            // Corrupted history — start fresh
            _undoStack = [];
            _redoStack = [];
        }
    }

    private void Save()
    {
        using var transaction = _db.BeginTransaction();
        try
        {
            // Clear existing history
            _db.Execute("DELETE FROM undo_history");

            // Write undo stack (newest first = highest id)
            for (var i = 0; i < _undoStack.Count; i++)
            {
                var cmd = _undoStack[i];
                var json = JsonSerializer.Serialize<IUndoableCommand>(cmd, GetJsonOptions());
                _db.Execute(
                    "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES ('undo', @json, @created)",
                    ("@json", json),
                    ("@created", cmd.ExecutedAt.ToString("o")));
            }

            // Write redo stack
            for (var i = 0; i < _redoStack.Count; i++)
            {
                var cmd = _redoStack[i];
                var json = JsonSerializer.Serialize<IUndoableCommand>(cmd, GetJsonOptions());
                _db.Execute(
                    "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES ('redo', @json, @created)",
                    ("@json", json),
                    ("@created", cmd.ExecutedAt.ToString("o")));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
        }
    }

    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        WriteIndented = false
    };
}
