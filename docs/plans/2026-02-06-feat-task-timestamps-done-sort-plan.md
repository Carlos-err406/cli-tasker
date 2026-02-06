---
title: "feat: Done-sort-by-completion-time with completed_at timestamp"
type: feat
date: 2026-02-06
---

# feat: Done-Sort-by-Completion-Time

## Overview

Add a `completed_at` timestamp column to the tasks table. Done tasks sort purely by `completed_at DESC` (most recently completed on top). Active tasks keep current sort. Show relative completion time in tray popup for done tasks. Show completion time in `tasker get`.

## Acceptance Criteria

- [x] `completed_at` set to `DateTime.UtcNow` when status transitions TO Done; cleared to NULL for ANY non-Done transition
- [x] Done tasks sorted purely by `completed_at DESC` — no priority/due date sub-sort
- [x] Active tasks (pending/in-progress) keep current sort: priority → due date → created_at DESC
- [x] NULL `completed_at` (legacy done tasks) sort to bottom of done group
- [x] `tasker get <id>` shows "Completed:" line (omitted if null)
- [x] `tasker get --json` includes `completedAt` field
- [x] Tray popup shows dim relative time label (e.g., "2h ago") for done tasks with `completed_at`
- [x] Schema migration: `ALTER TABLE ADD COLUMN completed_at TEXT`, clear undo history
- [x] New tasks: `completed_at = NULL`
- [x] Migration backfill: existing done tasks get `completed_at = created_at`
- [x] TUI sort updated to match
- [x] Undo/redo correctly handles timestamp (SetStatusCommand, DeleteTaskCommand, ClearTasksCommand)

## Key Design Decisions

1. **No `updated_at` column** — all 3 reviewers flagged it as scope creep with zero current user value. Can add later if needed.

2. **`CompletedAt` naming** (not `CheckedAt`) — matches "Done" status terminology. Column: `completed_at`.

3. **`CompletedAt` is a property on the `TodoTask` record** — not DB-only. The undo system's `DeleteTaskCommand` and `ClearTasksCommand` capture full `TodoTask` snapshots, so the timestamp must survive that round-trip.

4. **`completed_at` cleared for ANY non-Done transition** — Done→Pending, Done→InProgress, all clear it. Rule: `completed_at` is non-null if and only if `status == Done`.

5. **`completed_at` set in `WithStatus()`** — semantically tied to status transitions, so the record method handles it (not scattered across SQL paths).

6. **Migration clears undo history** — same pattern as `MigrateIsCheckedToStatus()`. Old serialized `TodoTask` snapshots lack the new field.

7. **Migration backfills `completed_at = created_at` for existing done tasks** — so they get a reasonable sort position and display "ago" label in tray.

8. **Undo/redo timestamp drift is acceptable** — redo of a check gets a fresh `completed_at`, not the original. All reviewers agree this is correct behavior.

9. **Relative time format**: just now (<1m), Xm ago (<1h), Xh ago (<24h), Xd ago (<7d), "MMM d" (>=7d).

10. **Tray "ago" label placement**: below task title, same position/style as due date display, dim gray color. Only shown for done tasks with non-null `completed_at`.

## Implementation Phases

### Phase 1: Schema + Model

**`src/TaskerCore/Models/TodoTask.cs`**
- Add `DateTime? CompletedAt = null` to record
- Update `WithStatus()`: set `CompletedAt = DateTime.UtcNow` when Done, clear to null otherwise

**`src/TaskerCore/Data/TaskerDb.cs`**
- Add `completed_at TEXT` to CREATE TABLE
- Add `MigrateAddCompletedAt()` migration method:
  - Detect via `PRAGMA table_info(tasks)` for absence of `completed_at`
  - `ALTER TABLE tasks ADD COLUMN completed_at TEXT`
  - `UPDATE tasks SET completed_at = created_at WHERE status = 2`
  - Clear undo_history table

### Phase 2: Data Layer

**`src/TaskerCore/Data/TodoTaskList.cs`**
- `TaskSelectColumns`: add `completed_at`
- `ReadTask()`: read new column
- `InsertTask()`: add `completed_at` to INSERT SQL
- `UpdateTask()`: add `completed_at = @completed` to UPDATE SET
- `RestoreList()`: add new column to INSERT SQL in both paths
- `GetSortedTasks()`: change done-group sort:

```csharp
// Split into status groups for different sort logic
var active = filteredTasks.Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => StatusSortOrder(t.Status))
    .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
    .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
    .ThenByDescending(t => t.CreatedAt)
    .ToList();

var done = filteredTasks.Where(t => t.Status == TaskStatus.Done)
    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue) // NULL sorts last
    .ToList();

return [..active, ..done];
```

### Phase 3: TUI Sort Fix

**`Tui/TuiApp.cs`**
- `LoadTasks()`: update the re-sort logic to use `CompletedAt` for done tasks:

```csharp
sorted = tasks
    .OrderBy(t => StatusSortOrder(t.Status))
    .ThenByDescending(t => t.Status == TaskStatus.Done
        ? (t.CompletedAt ?? DateTime.MinValue)
        : t.CreatedAt)
    .ToList();
```

### Phase 4: CLI Display

**`AppCommands/GetCommand.cs`**
- `OutputHumanReadable()`: add after "Created:" line:
  - `Completed: {task.CompletedAt:yyyy-MM-dd HH:mm}` (omit if null)
- `OutputJson()`: add `completedAt` to anonymous object (ISO 8601 string or null)

### Phase 5: Tray Popup

**`src/TaskerTray/ViewModels/TodoTaskViewModel.cs`**
- Expose `DateTime? CompletedAt` from `_task`
- Add `string? CompletedAtDisplay` computed property using relative time formatting

**`src/TaskerTray/Views/TaskListPopup.axaml.cs`**
- In `CreateTaskItem()`: after title row, for done tasks with non-null `CompletedAt`, add a dim TextBlock showing relative time (e.g., "2h ago")
- Style: `FontSize=11`, `Foreground=#888`, same margin pattern as due date label

**Relative time helper** (static method on TodoTaskViewModel):
```csharp
static string FormatRelativeTime(DateTime timestamp)
{
    var span = DateTime.UtcNow - timestamp;
    return span.TotalMinutes < 1 ? "just now"
         : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m ago"
         : span.TotalDays < 1 ? $"{(int)span.TotalHours}h ago"
         : span.TotalDays < 7 ? $"{(int)span.TotalDays}d ago"
         : timestamp.ToString("MMM d");
}
```

### Phase 6: Tests

**`tests/TaskerCore.Tests/Data/TaskTimestampTests.cs`** (new file)
- `SetStatus_ToDone_SetsCompletedAt` — completed_at non-null after checking
- `SetStatus_FromDone_ClearsCompletedAt` — completed_at null after unchecking
- `SetStatus_FromDone_ToInProgress_ClearsCompletedAt` — Done→InProgress clears it
- `SetStatus_ReCheck_GetsNewCompletedAt` — fresh timestamp on re-check
- `GetSortedTasks_DoneGroupSortsByCompletedAtDesc` — most recently completed first
- `GetSortedTasks_NullCompletedAt_SortsLast` — legacy done tasks at bottom
- `Migration_AddsColumn_BackfillsData` — schema migration test (file-based, not in-memory)

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerCore/Models/TodoTask.cs` | Add `CompletedAt`; update `WithStatus()` |
| `src/TaskerCore/Data/TaskerDb.cs` | Schema + migration |
| `src/TaskerCore/Data/TodoTaskList.cs` | ReadTask, InsertTask, UpdateTask, RestoreList, GetSortedTasks |
| `Tui/TuiApp.cs` | Fix TUI sort for done tasks |
| `AppCommands/GetCommand.cs` | Display completed timestamp in get output |
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | Expose CompletedAt + display property |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | "ago" label for done tasks |
| `tests/TaskerCore.Tests/Data/TaskTimestampTests.cs` | New test file |

**No changes needed:**
- `src/TaskerCore/Data/JsonMigrator.cs` — NULL default works; `MapJsonTask()` compiles with optional param
- Undo commands — `SetStatusCommand` works via `SetStatus()` which calls `WithStatus()` handling timestamps; `DeleteTaskCommand`/`ClearTasksCommand` capture full `TodoTask` with new field
- `IUndoableCommand.cs` — no new command types

## References

- Brainstorm: `docs/brainstorms/2026-02-06-done-tasks-sort-by-check-time-brainstorm.md`
- Schema migration pattern: `src/TaskerCore/Data/TaskerDb.cs:115` (`MigrateIsCheckedToStatus`)
- Sort order learning: `docs/solutions/logic-errors/inconsistent-task-sort-order-across-consumers.md`
- Stable sort learning: `docs/solutions/ui-bugs/task-teleportation-on-status-change.md`
- Undo patterns: `docs/solutions/undo-system/undo-support-for-reorder-operations.md`
