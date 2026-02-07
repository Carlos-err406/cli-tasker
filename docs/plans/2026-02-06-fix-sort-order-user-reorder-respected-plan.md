---
title: "fix: User-specified sort order should be respected"
type: fix
date: 2026-02-06
---

# fix: User-specified sort order should be respected

Drag-reorder in the tray correctly updates `sort_order` in the database, but `GetSortedTasks()` ignores it and re-sorts by status > priority > due-date > created-at. The user's manual ordering is lost on every UI refresh.

## Acceptance Criteria

- [x] Active tasks display in `sort_order DESC` order (user-specified), grouped by status (InProgress first, then Pending)
- [x] Done tasks display in `CompletedAt DESC` order (most recently completed first)
- [x] Drag-reorder persists across refreshes in tray, CLI, and TUI
- [x] `BumpSortOrder()` removed from `RenameTask`, `SetTaskDueDate`, `SetTaskPriority` (only kept for `MoveTask` and `AddTodoTask`)
- [x] `StatusSortOrder()` and `GetDueDateSortOrder()` helper methods deleted
- [x] TUI `LoadTasks()` re-sort logic removed — uses `GetSortedTasks()` directly
- [x] Existing test `GetSortedTasks_StillSortsByStatusOnFreshCall` updated to match new behavior
- [x] New test: tasks within same status group appear in `sort_order DESC` order

## Implementation

### 1. Simplify `GetSortedTasks()` in `TodoTaskList.cs:109-151`

Replace the multi-criteria sort chains with a simple status partition. Data comes from `GetFilteredTasks()` already in `sort_order DESC` order. LINQ `Where` preserves relative order.

```csharp
// TodoTaskList.cs — GetSortedTasks()
// Keep all the filter logic (filterStatus, filterChecked, filterPriority, filterOverdue) as-is.
// Replace lines 136-150 (the two OrderBy chains) with:

var active = filteredTasks
    .Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => t.Status == TaskStatus.InProgress ? 0 : 1)  // InProgress first, stable within group
    .ToList();

var done = filteredTasks
    .Where(t => t.Status == TaskStatus.Done)
    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
    .ToList();

return [..active, ..done];
```

### 2. Simplify `SearchTasks()` in `TodoTaskList.cs:1064-1087`

Same change — replace the duplicate sort chains with partition only:

```csharp
// Replace lines 1073-1083 with:
var active = tasks.Where(t => t.Status != TaskStatus.Done)
    .OrderBy(t => t.Status == TaskStatus.InProgress ? 0 : 1)
    .ToList();
var done = tasks.Where(t => t.Status == TaskStatus.Done)
    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
    .ToList();
return [..active, ..done];
```

### 3. Delete helper methods in `TodoTaskList.cs:156-169`

Remove `StatusSortOrder()` and `GetDueDateSortOrder()` — no longer called.

### 4. Remove `BumpSortOrder()` from metadata operations

In `TodoTaskList.cs`, remove the `BumpSortOrder()` call from:
- `RenameTask()` (line ~554)
- `SetTaskDueDate()` (line ~633)
- `SetTaskPriority()` (line ~675)

Keep `BumpSortOrder()` in:
- `MoveTask()` (line ~594) — moving to a new list should place at top
- `AddTodoTask()` — new tasks appear at top (this uses `GetNextSortOrder` not `BumpSortOrder`, but same effect)

### 5. Simplify TUI `LoadTasks()` in `TuiApp.cs:93-139`

Remove the re-sort logic. `GetSortedTasks()` now returns the correct order.

```csharp
// TuiApp.cs — LoadTasks()
// For single-list view: just use GetSortedTasks() directly, no re-sort
// For all-lists view: group by list name, but preserve sort_order within each list

List<TodoTask> sorted;
if (_state.CurrentList == null)
{
    sorted = tasks
        .OrderBy(t => t.ListName != ListManager.DefaultListName)
        .ThenBy(t => t.ListName)
        .ToList();
    // Within each list, GetSortedTasks already has correct status-grouped, sort_order-based ordering
    // GroupBy + SelectMany to preserve within-group order while grouping by list:
    sorted = sorted
        .GroupBy(t => t.ListName)
        .SelectMany(g => g)
        .ToList();
}
else
{
    sorted = tasks;  // Already correctly ordered by GetSortedTasks()
}
```

Also delete the duplicate `StatusSortOrder()` at `TuiApp.cs:85-91`.

### 6. Update tests in `SortOrderStabilityTests.cs`

Update `GetSortedTasks_StillSortsByStatusOnFreshCall` — it currently asserts InProgress > Pending > Done which is still correct, but add a new test verifying `sort_order` is respected within each status group:

```csharp
[Fact]
public void GetSortedTasks_RespectsUserSortOrderWithinStatusGroup()
{
    var taskList = new TodoTaskList(_services, "tasks");

    var taskA = TodoTask.CreateTodoTask("task A", "tasks");
    var taskB = TodoTask.CreateTodoTask("task B", "tasks");
    var taskC = TodoTask.CreateTodoTask("task C", "tasks");

    taskList.AddTodoTask(taskA, recordUndo: false);  // sort_order 0
    taskList.AddTodoTask(taskB, recordUndo: false);  // sort_order 1
    taskList.AddTodoTask(taskC, recordUndo: false);  // sort_order 2

    // Reorder: move C to the bottom (index 2 in display = lowest sort_order)
    TodoTaskList.ReorderTask(_services, taskC.Id, 2);

    var sorted = taskList.GetSortedTasks();

    // B should be first (highest sort_order), then A, then C (reordered to bottom)
    Assert.Equal(taskB.Id, sorted[0].Id);
    Assert.Equal(taskA.Id, sorted[1].Id);
    Assert.Equal(taskC.Id, sorted[2].Id);
}
```

## References

- Brainstorm: `docs/brainstorms/2026-02-06-user-sort-order-priority-brainstorm.md`
- Institutional learning: `docs/solutions/logic-errors/inconsistent-task-sort-order-across-consumers.md`
- Institutional learning: `docs/solutions/ui-bugs/task-teleportation-on-status-change.md`
- Institutional learning: `docs/solutions/feature-implementations/done-tasks-sort-by-completion-time.md`
- Source: `src/TaskerCore/Data/TodoTaskList.cs` (lines 88-169, 327-334, 554, 594, 633, 675, 1064-1087)
- Source: `Tui/TuiApp.cs` (lines 85-139)
- Source: `src/TaskerTray/Views/TaskListPopup.axaml.cs` (lines 267-288)
- Tests: `tests/TaskerCore.Tests/Data/SortOrderStabilityTests.cs`
