---
title: "Done tasks sort by completion time with completed_at timestamp"
date: 2026-02-06
category: feature-implementations
tags: [sort-order, completed-at, timestamps, schema-migration, done-tasks, tray, tui, cli]
module: [TaskerCore, TUI, TaskerTray, CLI]
severity: medium
symptoms:
  - Done tasks sorted by priority/due date instead of when they were completed
  - Most recently completed task not at top of done group
  - No way to see when a task was completed
---

# Done Tasks Sort by Completion Time

## Problem

Done tasks used the same sort logic as active tasks: priority, due date, then created_at. This meant a task you just completed could appear below older-completed tasks if it had lower priority or was created earlier. The sort order didn't reflect the actual completion timeline.

## Root Cause

`GetSortedTasks()` applied a single sort chain to all tasks regardless of status. For the done group, priority and due date are irrelevant — what matters is recency of completion. But there was no `completed_at` column to sort by.

## Solution

Added a `completed_at` timestamp column and split `GetSortedTasks()` into two sort groups.

### 1. Model: `CompletedAt` on `TodoTask`

```csharp
// TodoTask.cs — new optional parameter
public record TodoTask(..., DateTime? CompletedAt = null)

// WithStatus() handles the timestamp
public TodoTask WithStatus(TaskStatus status) => this with
{
    Status = status,
    CompletedAt = status == TaskStatus.Done ? DateTime.UtcNow : null
};
```

**Key rule:** `CompletedAt` is non-null if and only if `Status == Done`. Any non-Done transition clears it.

### 2. Schema Migration

Simple `ALTER TABLE ADD COLUMN` — no table rebuild needed:

```csharp
private void MigrateAddCompletedAt()
{
    var hasCompletedAt = Query("PRAGMA table_info(tasks)",
        reader => reader.GetString(1), []).Any(col => col == "completed_at");
    if (hasCompletedAt) return;

    Execute("ALTER TABLE tasks ADD COLUMN completed_at TEXT");
    Execute("UPDATE tasks SET completed_at = created_at WHERE status = 2");
    Execute("DELETE FROM undo_history");
}
```

- Backfills existing done tasks with `created_at` as approximation
- Clears undo history (old serialized snapshots lack the new field)

### 3. Split Sort in `GetSortedTasks()`

```csharp
// Active: priority → due date → created_at (unchanged)
var active = filteredTasks.Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => StatusSortOrder(t.Status))
    .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
    .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
    .ThenByDescending(t => t.CreatedAt)
    .ToList();

// Done: purely by completed_at DESC (NULL sorts last)
var done = filteredTasks.Where(t => t.Status == TaskStatus.Done)
    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
    .ToList();

return [..active, ..done];
```

### 4. TUI Sort Fix

TUI re-sorts `GetSortedTasks()` results for display grouping. Updated both sort paths (single-list and all-lists) to use `CompletedAt` for done tasks:

```csharp
.ThenByDescending(t => t.Status == TaskStatus.Done
    ? (t.CompletedAt ?? DateTime.MinValue)
    : t.CreatedAt)
```

### 5. Display

- `tasker get` shows "Completed: yyyy-MM-dd HH:mm" line (omitted if null)
- `tasker get --json` includes `completedAt` ISO 8601 field
- Tray popup shows relative time ("2h ago", "3d ago") for done tasks via `FormatRelativeTime()`

## Key Design Decisions

1. **Dropped `updated_at`** — three independent reviewers flagged it as scope creep with zero current user value. Can add later.
2. **Named `CompletedAt` not `CheckedAt`** — matches "Done" status terminology.
3. **Timestamp set in `WithStatus()`** — semantically tied to status transitions, not scattered across SQL paths.
4. **Undo/redo timestamp drift is acceptable** — redo of a check gets a fresh `CompletedAt`, not the original. This is correct behavior (undo is itself a mutation event).
5. **Migration clears undo history** — same pattern as `MigrateIsCheckedToStatus()`. Old serialized `TodoTask` snapshots lack the new field and would cause deserialization issues.

## Gotchas

- **All INSERT paths must include `completed_at`** — `InsertTask()`, both `RestoreList()` paths (active + trashed tasks). Missing it causes NULL even for done tasks.
- **TUI has its own re-sort** — `TuiApp.LoadTasks()` re-sorts `GetSortedTasks()` results. If you only fix `GetSortedTasks()`, the TUI still uses the old sort. Both must be updated.
- **`DateTime.MinValue` sentinel for NULL sort** — `OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)` puts NULL-completed tasks at the bottom of the done group.

## Prevention

- When adding columns to `TodoTask`, audit ALL SQL paths that INSERT or SELECT tasks: `InsertTask`, `UpdateTask`, `RestoreList` (2 paths), `ReadTask`, `TaskSelectColumns`
- When changing sort logic in `GetSortedTasks()`, also update `TuiApp.LoadTasks()` (both single-list and all-lists branches)
- Schema migrations that add fields to `TodoTask` should clear undo history — the undo system serializes full task snapshots

## Files Changed

| File | Change |
|------|--------|
| `src/TaskerCore/Models/TodoTask.cs` | Added `CompletedAt`; `WithStatus()` sets/clears it |
| `src/TaskerCore/Data/TaskerDb.cs` | Schema column + `MigrateAddCompletedAt()` |
| `src/TaskerCore/Data/TodoTaskList.cs` | All SQL paths + split sort in `GetSortedTasks()` |
| `Tui/TuiApp.cs` | Both sort branches use `CompletedAt` for done tasks |
| `AppCommands/GetCommand.cs` | "Completed:" line + JSON field |
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | `CompletedAt` + `CompletedAtDisplay` + `FormatRelativeTime()` |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Dim "ago" label for done tasks |
| `tests/TaskerCore.Tests/Data/TaskTimestampTests.cs` | 7 new tests |

## Related

- [Inconsistent task sort order across consumers](../logic-errors/inconsistent-task-sort-order-across-consumers.md) — why `GetSortedTasks()` is the canonical sort
- [Task teleportation on status change](../ui-bugs/task-teleportation-on-status-change.md) — stable sort during interaction
- [JSON to SQLite storage migration](../database-issues/json-to-sqlite-storage-migration.md) — migration pattern reference
- [Undo support for reorder operations](../undo-system/undo-support-for-reorder-operations.md) — undo system patterns
- `docs/brainstorms/2026-02-06-done-tasks-sort-by-check-time-brainstorm.md`
- `docs/plans/2026-02-06-feat-task-timestamps-done-sort-plan.md`
