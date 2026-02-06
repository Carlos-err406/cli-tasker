namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Undo;

/// <summary>
/// Migrates data from old JSON file storage to the new SQLite database.
/// Detects JSON files, imports data, and renames originals to .bak.
/// </summary>
public static class JsonMigrator
{
    /// <summary>
    /// Checks for old JSON files and migrates them into the SQLite database.
    /// Safe to call multiple times — only migrates if JSON files exist.
    /// </summary>
    public static void MigrateIfNeeded(StoragePaths paths, TaskerDb db)
    {
        var tasksPath = paths.AllTasksPath;
        var trashPath = paths.AllTrashPath;
        var configPath = paths.ConfigPath;
        var undoPath = paths.UndoHistoryPath;

        // Check if any JSON files exist
        var hasJsonFiles = File.Exists(tasksPath) || File.Exists(trashPath) ||
                           File.Exists(configPath) || File.Exists(undoPath);

        if (!hasJsonFiles) return;

        using var transaction = db.BeginTransaction();
        try
        {
            MigrateTasks(db, tasksPath, isTrashed: false);
            MigrateTasks(db, trashPath, isTrashed: true);
            MigrateConfig(db, configPath);
            MigrateUndoHistory(db, undoPath);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            // Migration failed — leave JSON files in place for next attempt
            return;
        }

        // Rename JSON files to .bak
        RenameToBackup(tasksPath);
        RenameToBackup(trashPath);
        RenameToBackup(configPath);
        RenameToBackup(undoPath);
    }

    private static void MigrateTasks(TaskerDb db, string jsonPath, bool isTrashed)
    {
        if (!File.Exists(jsonPath)) return;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var taskLists = DeserializeTaskLists(json);

            foreach (var list in taskLists)
            {
                // Ensure list exists
                db.Execute(
                    "INSERT OR IGNORE INTO lists (name, is_collapsed, sort_order) VALUES (@name, @collapsed, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM lists))",
                    ("@name", list.ListName),
                    ("@collapsed", list.IsCollapsed ? 1 : 0));

                // Insert tasks (preserve original order — array index becomes sort_order)
                for (var i = 0; i < list.Tasks.Length; i++)
                {
                    var task = list.Tasks[i];
                    // Tasks were stored newest-first in arrays; sort_order DESC = display order
                    // So index 0 = highest sort_order (newest)
                    var sortOrder = list.Tasks.Length - 1 - i;

                    var tagsJson = task.Tags is { Length: > 0 }
                        ? JsonSerializer.Serialize(task.Tags)
                        : null;

                    db.Execute("""
                        INSERT OR IGNORE INTO tasks (id, description, is_checked, created_at, list_name, due_date, priority, tags, is_trashed, sort_order)
                        VALUES (@id, @desc, @checked, @created, @list, @due, @priority, @tags, @trashed, @order)
                        """,
                        ("@id", task.Id),
                        ("@desc", task.Description),
                        ("@checked", task.IsChecked ? 1 : 0),
                        ("@created", task.CreatedAt.ToString("o")),
                        ("@list", task.ListName),
                        ("@due", (object?)task.DueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value),
                        ("@priority", (object?)(task.Priority.HasValue ? (int)task.Priority.Value : null) ?? DBNull.Value),
                        ("@tags", (object?)tagsJson ?? DBNull.Value),
                        ("@trashed", isTrashed ? 1 : 0),
                        ("@order", sortOrder));
                }
            }
        }
        catch
        {
            // Silently skip malformed files
        }
    }

    /// <summary>
    /// Deserializes task lists from JSON, handling both new list-first format
    /// and legacy flat TodoTask[] format.
    /// </summary>
    private static TaskList[] DeserializeTaskLists(string json)
    {
        // Try new list-first format: [{ListName: "tasks", Tasks: [...]}]
        try
        {
            var lists = JsonSerializer.Deserialize<TaskList[]>(json);
            if (lists != null) return lists;
        }
        catch { /* Try legacy format */ }

        // Try legacy flat format: [{Id: "abc", Description: "...", ...}]
        try
        {
            var tasks = JsonSerializer.Deserialize<TodoTask[]>(json);
            if (tasks != null)
            {
                return tasks
                    .GroupBy(t => t.ListName)
                    .Select(g => new TaskList(g.Key, g.ToArray()))
                    .ToArray();
            }
        }
        catch { /* Malformed data */ }

        return [];
    }

    private static void MigrateConfig(TaskerDb db, string configPath)
    {
        if (!File.Exists(configPath)) return;

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<JsonElement>(json);

            if (config.TryGetProperty("DefaultList", out var defaultList))
            {
                var value = defaultList.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    db.Execute(
                        "INSERT INTO config (key, value) VALUES ('default_list', @value) ON CONFLICT(key) DO UPDATE SET value = @value",
                        ("@value", value));
                }
            }
        }
        catch { /* Silently skip malformed config */ }
    }

    private static void MigrateUndoHistory(TaskerDb db, string undoPath)
    {
        if (!File.Exists(undoPath)) return;

        try
        {
            var json = File.ReadAllText(undoPath);
            var history = JsonSerializer.Deserialize<UndoHistory>(json, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            if (history == null) return;

            foreach (var cmd in history.UndoStack)
            {
                var cmdJson = JsonSerializer.Serialize<IUndoableCommand>(cmd);
                db.Execute(
                    "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES ('undo', @json, @created)",
                    ("@json", cmdJson),
                    ("@created", cmd.ExecutedAt.ToString("o")));
            }

            foreach (var cmd in history.RedoStack)
            {
                var cmdJson = JsonSerializer.Serialize<IUndoableCommand>(cmd);
                db.Execute(
                    "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES ('redo', @json, @created)",
                    ("@json", cmdJson),
                    ("@created", cmd.ExecutedAt.ToString("o")));
            }
        }
        catch { /* Silently skip malformed history */ }
    }

    private static void RenameToBackup(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.Move(path, path + ".bak", overwrite: true);
        }
        catch { /* Ignore rename failures */ }
    }
}
