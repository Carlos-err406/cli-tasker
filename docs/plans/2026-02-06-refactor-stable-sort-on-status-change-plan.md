---
title: "refactor: Stable sort on status change"
type: refactor
date: 2026-02-06
---

# refactor: Stable sort on status change

When a task's status changes, it teleports to a different position in the list because `GetSortedTasks()` groups by status and `BumpSortOrder()` moves the task to the top of its group. This is disorienting in the tray and TUI where the user just interacted with the task.

**Principle:** Sort on open, stable during interaction.

## Acceptance Criteria

- [ ] Status change does NOT modify `sort_order` in the database
- [ ] Tray: checkbox click, right-click, and context menu all update the task icon in-place without moving the task
- [ ] Tray: status bar counts update after in-place status change
- [ ] Tray: next popup open applies fresh sort (InProgress > Priority > DueDate > Pending > Done)
- [ ] TUI: space toggle updates the task in-place without re-sorting the list
- [ ] TUI: launch/list-switch applies fresh sort
- [ ] CLI: `tasker list` always shows fresh sort (no change needed)
- [ ] If `SetStatus` returns `NotFound`, fall back to full refresh

## Changes

### Phase 1: Data Layer

**`src/TaskerCore/Data/TodoTaskList.cs`**

Remove `BumpSortOrder()` call from:
- `SetStatus()` (line ~279)
- `SetStatuses()` (line ~447)

Keep `BumpSortOrder` in: `AddTodoTask`, `RenameTask`, `MoveTask`, `SetTaskDueDate`, `SetTaskPriority`, `ReorderTask`.

### Phase 2: Tray — In-Place Update

**`src/TaskerTray/Views/TaskListPopup.axaml.cs`**

Extract a shared `UpdateTaskStatusInPlace(TodoTaskViewModel task, TaskStatus newStatus)` method:
1. Update `task.Status` on the ViewModel
2. Find the checkbox in `_taskBorders` and set `IsChecked` (true=Done, null=InProgress, false=Pending)
3. Update border CSS class ("checked" add/remove)
4. Update title text color (#666 for Done, #FFF otherwise)
5. Call `UpdateStatus()` to refresh status bar counts
6. Do NOT call `RefreshTasks()`

Call this from all three paths:
- `OnCheckboxClicked` — replace the current visual feedback + `RefreshTasks()` with `UpdateTaskStatusInPlace` + `SetStatus`
- `OnSetStatus` — replace `RefreshTasks()` with `UpdateTaskStatusInPlace`
- `OnSetInProgress` — delegates to `OnSetStatus`, so covered

Error handling: if `SetStatus` returns `NotFound`, fall back to `RefreshTasks()`.

### Phase 3: TUI — Cached Task List

**`Tui/TuiApp.cs`**

Add `private List<TodoTask>? _cachedTasks` field.

In `LoadTasks()`: return `_cachedTasks` if non-null, otherwise fetch and sort as usual and cache the result.

Invalidate cache (`_cachedTasks = null`) after any operation that is NOT a pure status change:
- Add, delete, rename, move, priority change, due date change
- Undo/redo (any type — safest to always invalidate)
- List switch, search query change

**`Tui/TuiKeyHandler.cs`**

In `ToggleTask()`: after `SetStatus` succeeds, replace the `TodoTask` at `CursorIndex` in the cached list with `task.WithStatus(nextStatus)`. Return a signal to TuiApp that this was a status-only change (no cache invalidation needed).

In bulk check/uncheck: same pattern — update each affected task in the cache.

### Phase 4: Tests

**`tests/TaskerCore.Tests/Data/TodoTaskListTests.cs`** (or new file)

- `SetStatus_DoesNotChangeSortOrder` — verify `sort_order` column is unchanged after status toggle
- `SetStatuses_DoesNotChangeSortOrder` — same for batch
- `GetSortedTasks_StillSortsByStatusOnFreshCall` — verify the sort order is InProgress > Pending > Done

## Surface Area

| File | Change |
|------|--------|
| `TodoTaskList.cs:279` | Remove `BumpSortOrder` from `SetStatus` |
| `TodoTaskList.cs:447` | Remove `BumpSortOrder` from `SetStatuses` |
| `TaskListPopup.axaml.cs` | Extract `UpdateTaskStatusInPlace`, update 3 status paths |
| `TuiApp.cs` | Add `_cachedTasks` field, cache/invalidation logic |
| `TuiKeyHandler.cs` | Update `ToggleTask` to modify cached task in-place |
| Test file | New tests for sort_order stability |

## Notes

- `BumpSortOrder` stays in rename, move, priority, due date — those are less frequent and the "float to top" behavior is intentional for those edits
- Cross-process changes (CLI while tray is open) are already undetected — existing behavior, not made worse
- Drag reorder works on `sort_order` directly, which is unaffected by this change since we're removing the bump
