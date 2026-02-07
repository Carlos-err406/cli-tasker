namespace TaskerCore.Data;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Parsing;

/// <summary>
/// One-time migration that backfills inverse markers (-^ and -!) on task descriptions
/// based on existing DB relationships (parent_id and task_dependencies).
/// </summary>
public static class InverseMarkerMigrator
{
    private const string ConfigKey = "inverse_markers_migrated";

    /// <summary>
    /// Checks if migration is needed and backfills inverse markers in a single transaction.
    /// Safe to call multiple times — skips if already migrated.
    /// </summary>
    public static void MigrateIfNeeded(TaskerDb db)
    {
        var alreadyMigrated = db.ExecuteScalar<string>(
            "SELECT value FROM config WHERE key = @key",
            ("@key", ConfigKey));

        if (alreadyMigrated == "true") return;

        // Load all non-trashed tasks with their metadata
        var tasks = db.Query(
            "SELECT id, description, priority, due_date, tags, parent_id FROM tasks WHERE is_trashed = 0",
            reader =>
            {
                var tagsJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                string[]? tags = null;
                if (tagsJson != null)
                {
                    try { tags = JsonSerializer.Deserialize<string[]>(tagsJson); }
                    catch { /* ignore */ }
                }

                var dueDateStr = reader.IsDBNull(3) ? null : reader.GetString(3);
                DateOnly? dueDate = null;
                if (dueDateStr != null && DateOnly.TryParse(dueDateStr, out var parsed))
                    dueDate = parsed;

                var priorityVal = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                Priority? priority = priorityVal.HasValue ? (Priority)priorityVal.Value : null;

                var parentId = reader.IsDBNull(5) ? null : reader.GetString(5);

                return new TaskData(
                    reader.GetString(0),
                    reader.GetString(1),
                    priority,
                    dueDate,
                    tags,
                    parentId);
            });

        // Load all blocker relationships: task_id blocks blocks_task_id
        var dependencies = db.Query(
            "SELECT task_id, blocks_task_id FROM task_dependencies",
            reader => (BlockerId: reader.GetString(0), BlockedId: reader.GetString(1)));

        if (tasks.Count == 0)
        {
            // No tasks, just set the flag
            db.Execute(
                "INSERT INTO config (key, value) VALUES (@key, 'true') ON CONFLICT(key) DO UPDATE SET value = 'true'",
                ("@key", ConfigKey));
            return;
        }

        var taskMap = tasks.ToDictionary(t => t.Id);

        // Build inverse marker sets per task
        // Key = task ID that needs markers added, Value = (subtaskIds, blockedByIds)
        var subtaskMarkers = new Dictionary<string, HashSet<string>>();
        var blockedByMarkers = new Dictionary<string, HashSet<string>>();

        // Parent-child: parent needs -^child marker
        foreach (var task in tasks)
        {
            if (task.ParentId == null) continue;
            if (!taskMap.ContainsKey(task.ParentId)) continue;

            if (!subtaskMarkers.TryGetValue(task.ParentId, out var set))
            {
                set = new HashSet<string>();
                subtaskMarkers[task.ParentId] = set;
            }
            set.Add(task.Id);
        }

        // Blocker-blocked: blocked task needs -!blocker marker
        foreach (var (blockerId, blockedId) in dependencies)
        {
            if (!taskMap.ContainsKey(blockedId)) continue;

            if (!blockedByMarkers.TryGetValue(blockedId, out var set))
            {
                set = new HashSet<string>();
                blockedByMarkers[blockedId] = set;
            }
            set.Add(blockerId);
        }

        // Apply all updates in a single transaction
        using var tx = db.BeginTransaction();
        try
        {
            foreach (var task in tasks)
            {
                var parsed = TaskDescriptionParser.Parse(task.Description);

                // Merge existing inverse markers with DB relationships
                var existingSubtasks = new HashSet<string>(parsed.HasSubtaskIds ?? []);
                var existingBlockedBy = new HashSet<string>(parsed.BlockedByIds ?? []);

                var newSubtasks = subtaskMarkers.TryGetValue(task.Id, out var subs) ? subs : [];
                var newBlockedBy = blockedByMarkers.TryGetValue(task.Id, out var blocked) ? blocked : [];

                // Skip if no new markers needed
                if (newSubtasks.IsSubsetOf(existingSubtasks) && newBlockedBy.IsSubsetOf(existingBlockedBy))
                    continue;

                existingSubtasks.UnionWith(newSubtasks);
                existingBlockedBy.UnionWith(newBlockedBy);

                var synced = TaskDescriptionParser.SyncMetadataToDescription(
                    task.Description, task.Priority, task.DueDate, task.Tags,
                    parsed.ParentId, parsed.BlocksIds,
                    existingSubtasks.Count > 0 ? existingSubtasks.ToArray() : null,
                    existingBlockedBy.Count > 0 ? existingBlockedBy.ToArray() : null,
                    parsed.RelatedIds);

                if (synced != task.Description)
                {
                    db.Execute("UPDATE tasks SET description = @desc WHERE id = @id",
                        ("@desc", synced), ("@id", task.Id));
                }
            }

            // Set migration flag
            db.Execute(
                "INSERT INTO config (key, value) VALUES (@key, 'true') ON CONFLICT(key) DO UPDATE SET value = 'true'",
                ("@key", ConfigKey));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            // Migration failed — will retry on next startup
        }
    }

    private record TaskData(
        string Id,
        string Description,
        Priority? Priority,
        DateOnly? DueDate,
        string[]? Tags,
        string? ParentId);
}
