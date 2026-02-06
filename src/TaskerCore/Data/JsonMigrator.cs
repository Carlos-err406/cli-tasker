namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Undo;
using TaskStatus = TaskerCore.Models.TaskStatus;

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
                        INSERT OR IGNORE INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order)
                        VALUES (@id, @desc, @status, @created, @list, @due, @priority, @tags, @trashed, @order)
                        """,
                        ("@id", task.Id),
                        ("@desc", task.Description),
                        ("@status", (int)task.Status),
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
    /// and legacy flat TodoTask[] format. Maps old IsChecked bool to TaskStatus.
    /// </summary>
    private static TaskList[] DeserializeTaskLists(string json)
    {
        // Parse as raw JSON to handle old IsChecked → Status mapping
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return [];

            // Detect format: list-first has "ListName" + "Tasks", legacy has "Id" + "Description"
            var first = root[0];
            if (first.TryGetProperty("ListName", out _) && first.TryGetProperty("Tasks", out _))
            {
                // List-first format
                return root.EnumerateArray().Select(listEl =>
                {
                    var listName = listEl.GetProperty("ListName").GetString() ?? "tasks";
                    var collapsed = listEl.TryGetProperty("IsCollapsed", out var c) && c.GetBoolean();
                    var tasks = listEl.GetProperty("Tasks").EnumerateArray()
                        .Select(MapJsonTask)
                        .ToArray();
                    return new TaskList(listName, tasks, collapsed);
                }).ToArray();
            }
            else if (first.TryGetProperty("Id", out _))
            {
                // Legacy flat format
                var tasks = root.EnumerateArray().Select(MapJsonTask).ToArray();
                return tasks
                    .GroupBy(t => t.ListName)
                    .Select(g => new TaskList(g.Key, g.ToArray()))
                    .ToArray();
            }
        }
        catch { /* Malformed data */ }

        return [];
    }

    /// <summary>
    /// Maps a JSON task element to a TodoTask, handling old IsChecked bool → TaskStatus.
    /// </summary>
    private static TodoTask MapJsonTask(JsonElement el)
    {
        var id = el.GetProperty("Id").GetString() ?? "";
        var desc = el.GetProperty("Description").GetString() ?? "";
        var createdAt = el.GetProperty("CreatedAt").GetDateTime();
        var listName = el.GetProperty("ListName").GetString() ?? "tasks";

        // Handle IsChecked (old) vs Status (new)
        TaskStatus status;
        if (el.TryGetProperty("Status", out var statusEl))
        {
            status = (TaskStatus)statusEl.GetInt32();
        }
        else if (el.TryGetProperty("IsChecked", out var checkedEl))
        {
            status = checkedEl.GetBoolean() ? TaskStatus.Done : TaskStatus.Pending;
        }
        else
        {
            status = TaskStatus.Pending;
        }

        DateOnly? dueDate = null;
        if (el.TryGetProperty("DueDate", out var dueDateEl) && dueDateEl.ValueKind == JsonValueKind.String)
        {
            if (DateOnly.TryParse(dueDateEl.GetString(), out var dd))
                dueDate = dd;
        }

        Priority? priority = null;
        if (el.TryGetProperty("Priority", out var priorityEl) && priorityEl.ValueKind != JsonValueKind.Null)
        {
            priority = (Priority)priorityEl.GetInt32();
        }

        string[]? tags = null;
        if (el.TryGetProperty("Tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            tags = tagsEl.EnumerateArray()
                .Select(t => t.GetString() ?? "")
                .Where(t => t.Length > 0)
                .ToArray();
            if (tags.Length == 0) tags = null;
        }

        return new TodoTask(id, desc, status, createdAt, listName, dueDate, priority, tags);
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
