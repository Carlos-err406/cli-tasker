---
title: "Task teleportation on status change"
date: 2026-02-06
category: ui-bugs
tags: [sort-order, status-change, ux, tui, tray, cache, bump-sort-order]
module: [TaskerCore, TUI, TaskerTray]
severity: medium
symptoms:
  - Task jumps to different position when status changes (pending/in-progress/done)
  - Disorienting UX when toggling status in tray or TUI
  - Task disappears from where user was looking after checkbox click
---

# Task Teleportation on Status Change

## Problem

When changing a task's status (e.g., marking Done or setting In Progress), the task visually teleported to a different position in the list. This was disorienting because the task you just interacted with disappeared from where you were looking.

**Error/symptom:** Task jumps position immediately after status toggle in tray popup or TUI.

## Root Cause

Three factors combined to cause the teleportation:

1. **`BumpSortOrder()`** was called on every status change, moving the task to `MAX(sort_order) + 1`
2. **`GetSortedTasks()`** groups by status (InProgress > Pending > Done), so status change = different group
3. **`RefreshTasks()`** rebuilt the entire UI immediately, applying the new sort

## Solution

**Principle: Sort on open, stable during interaction.**

Tasks stay in place when status changes. Fresh sort only happens when the view is reopened.

### Phase 1: Data Layer

Remove `BumpSortOrder()` from `SetStatus()` and `SetStatuses()` in `TodoTaskList.cs`. Keep it in operations where "float to top" is intentional: add, rename, move, priority, due date.

```csharp
// In SetStatus() — removed this line:
// BumpSortOrder(taskId, updatedTask.ListName);

// In SetStatuses() — same removal per task in the batch loop
```

### Phase 2: Tray — In-Place Visual Update

Instead of calling `RefreshTasks()` after status change, update only the affected visual elements:

```csharp
private void UpdateTaskStatusInPlace(TodoTaskViewModel task, TaskStatus newStatus)
{
    task.Status = newStatus;

    if (_taskCheckboxes.TryGetValue(task.Id, out var checkbox))
        checkbox.IsChecked = newStatus switch
        {
            TaskStatus.Done => true,
            TaskStatus.InProgress => null,
            _ => false
        };

    if (_taskBorders.TryGetValue(task.Id, out var border))
    {
        if (newStatus == TaskStatus.Done) border.Classes.Add("checked");
        else border.Classes.Remove("checked");
    }

    if (_taskTitles.TryGetValue(task.Id, out var title))
        title.Foreground = new SolidColorBrush(
            Color.Parse(newStatus == TaskStatus.Done ? "#666" : "#FFF"));

    UpdateStatus(); // refresh status bar counts only
}
```

Falls back to `RefreshTasks()` if `SetStatus` returns `NotFound` (task deleted externally).

### Phase 3: TUI — Cached Task List

Added `_cachedTasks` field to `TuiApp`. `LoadTasks()` returns cached list if available. Status toggle updates the cache in-place:

```csharp
// In ToggleTask():
taskList.SetStatus(task.Id, nextStatus);
_app.UpdateCachedTask(state.CursorIndex, task.WithStatus(nextStatus));
```

All non-status mutations call `_app.InvalidateCache()`: delete, add, rename, move, priority, due date, undo, redo, list switch, bulk operations.

Cache is bypassed during active search (query changes per keystroke).

## Prevention

- **Don't call `BumpSortOrder` on property-only changes.** Reserve it for operations where "float to top" is intentional UX.
- **Prefer in-place UI updates** over full re-renders when only one element changed.
- **Test sort_order stability** — `SortOrderStabilityTests` verifies `sort_order` is unchanged after status transitions.

## Files Changed

| File | Change |
|------|--------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Removed `BumpSortOrder` from `SetStatus`/`SetStatuses` |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | In-place update with tracking dictionaries |
| `Tui/TuiApp.cs` | `_cachedTasks` field, `InvalidateCache()`, `UpdateCachedTask()` |
| `Tui/TuiKeyHandler.cs` | In-place cache update in `ToggleTask`, invalidation in all other mutations |
| `tests/TaskerCore.Tests/Data/SortOrderStabilityTests.cs` | 3 tests for sort_order stability |

## Related

- [Inconsistent task sort order across consumers](../logic-errors/inconsistent-task-sort-order-across-consumers.md)
- [JSON to SQLite storage migration](../database-issues/json-to-sqlite-storage-migration.md) — sort_order convention
- [Undo support for reorder operations](../undo-system/undo-support-for-reorder-operations.md)
- `docs/brainstorms/2026-02-06-stable-sort-on-status-change-brainstorm.md`
- `docs/plans/2026-02-06-refactor-stable-sort-on-status-change-plan.md`
