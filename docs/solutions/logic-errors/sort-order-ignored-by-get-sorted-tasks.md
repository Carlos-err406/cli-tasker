---
title: User Sort Order Ignored by GetSortedTasks
category: logic-errors
tags:
  - sort-order
  - drag-reorder
  - GetSortedTasks
  - BumpSortOrder
module: TaskerCore, Tui
date: 2026-02-06
severity: high
symptoms:
  - drag-reorder in tray appears to do nothing — order snaps back on refresh
  - task order in CLI does not reflect manual reordering
  - sort_order column is correctly updated in DB but ignored by display
root_cause: GetSortedTasks() re-sorted in memory by priority/due-date/created-at, discarding the sort_order from the SQL query
---

# User Sort Order Ignored by GetSortedTasks

## Problem

Drag-reorder in the tray correctly updated `sort_order` in the database via `ReorderTask()`, but the visual order snapped back on every `RefreshTasks()` call. The user's manual ordering was lost.

## Root Cause

`GetSortedTasks()` called `GetFilteredTasks()` which returned tasks in `sort_order DESC` order from SQL, then **immediately re-sorted** the results in memory:

```csharp
// BEFORE — discards sort_order from DB
var active = filteredTasks
    .Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => StatusSortOrder(t.Status))
    .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
    .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
    .ThenByDescending(t => t.CreatedAt)
    .ToList();
```

Three separate consumers all had this problem:
1. **`GetSortedTasks()`** — sorted by status > priority > due-date > created-at
2. **`SearchTasks()`** — duplicate of the same sort logic
3. **`TuiApp.LoadTasks()`** — called `GetSortedTasks()` then re-sorted *again* by status > created-at

Additionally, `BumpSortOrder()` was called from `RenameTask`, `SetTaskDueDate`, and `SetTaskPriority`, which would have caused tasks to jump to the top on any metadata change once sort_order became the visible ordering.

## Files Changed

| File | Changes |
|------|---------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Simplified `GetSortedTasks()` and `SearchTasks()` sort logic; deleted `StatusSortOrder()` and `GetDueDateSortOrder()` helpers; removed `BumpSortOrder()` from rename/priority/due-date |
| `Tui/TuiApp.cs` | Removed re-sort logic in `LoadTasks()`; deleted duplicate `StatusSortOrder()` |
| `tests/TaskerCore.Tests/Data/SortOrderStabilityTests.cs` | Added tests for sort_order preservation |

## Solution

### 1. Replace multi-criteria sort with status partition

`GetSortedTasks()` now groups by status (InProgress > Pending > Done) but within each group preserves `sort_order DESC` from the database. LINQ `OrderBy` is a stable sort in .NET, so the relative order from SQL is preserved within each status sub-group.

```csharp
// AFTER — preserves sort_order from DB, only partitions by status
var active = filteredTasks
    .Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => t.Status == TaskStatus.InProgress ? 0 : 1)
    .ToList();

var done = filteredTasks
    .Where(t => t.Status == TaskStatus.Done)
    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
    .ToList();

return [..active, ..done];
```

Done tasks still sort by `CompletedAt DESC` since `SetStatus` doesn't call `BumpSortOrder()`, so sort_order for done tasks reflects when they were last edited, not when they were completed.

### 2. Same fix applied to SearchTasks()

Replaced the duplicate sort chains with the same partition-only logic.

### 3. Removed BumpSortOrder from metadata operations

Only `MoveTask()` and `AddTodoTask()` now bump sort order. Renaming a task, changing its priority, or setting a due date no longer moves it to the top.

### 4. Simplified TUI LoadTasks()

Removed the re-sort block entirely. For all-lists view, uses `GroupBy` + `SelectMany` to group by list name while preserving within-list order from `GetSortedTasks()`.

## Key Design Decisions

**Sort order hierarchy (new canonical order):**
1. Status grouping: InProgress > Pending > Done (always)
2. Within active groups: `sort_order DESC` (user-specified)
3. Within done group: `CompletedAt DESC` (most recently completed first)

This supersedes the previous canonical order of priority > due-date > created-at established in `inconsistent-task-sort-order-across-consumers.md`.

**BumpSortOrder semantics:**
- Only `MoveTask` and `AddTodoTask` bump sort order
- Rename, priority, due-date changes preserve position
- Status changes already don't bump (established by task-teleportation fix)

## Testing

- `GetSortedTasks_StillSortsByStatusOnFreshCall` — InProgress > Pending > Done grouping preserved
- `GetSortedTasks_RespectsUserSortOrderWithinStatusGroup` — verifies `ReorderTask` persists through `GetSortedTasks()`
- `RenameTask_DoesNotChangeSortOrder` — verifies rename no longer bumps sort order

## Related Documentation

- [Inconsistent Task Sort Order Across Consumers](inconsistent-task-sort-order-across-consumers.md) — established the old canonical sort order (now superseded)
- [Task Teleportation on Status Change](../ui-bugs/task-teleportation-on-status-change.md) — removed BumpSortOrder from SetStatus
- [Done Tasks Sort by Completion Time](../feature-implementations/done-tasks-sort-by-completion-time.md) — split sort for done vs active tasks
- [Undo Support for Reorder Operations](../undo-system/undo-support-for-reorder-operations.md) — ReorderTask/ReorderList undo support
