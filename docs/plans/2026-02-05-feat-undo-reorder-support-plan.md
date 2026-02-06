---
title: feat: Add undo support for reorder operations
type: feat
date: 2026-02-05
---

# Add Undo Support for Reorder Operations

## Overview

Add undo/redo support for `ReorderTask()` and `ReorderList()` operations, following the established undo command patterns. Currently, reordering tasks or lists via drag-drop in TaskerTray cannot be undone.

## Problem Statement

- `ReorderTask(taskId, newIndex)` moves a task within its list but isn't undoable
- `ReorderList(listName, newIndex)` moves a list in the sidebar but isn't undoable
- Users who accidentally reorder items have no way to restore the original order

## Proposed Solution

Create two new undo commands following existing patterns:
1. `ReorderTaskCommand` - captures task ID, list name, old index, new index
2. `ReorderListCommand` - captures list name, old index, new index

## Technical Approach

### Files to Create

| File | Purpose |
|------|---------|
| `src/TaskerCore/Undo/Commands/ReorderTaskCommand.cs` | Undo command for task reordering |
| `src/TaskerCore/Undo/Commands/ReorderListCommand.cs` | Undo command for list reordering |
| `tests/TaskerCore.Tests/Undo/ReorderTaskCommandTests.cs` | Tests for task reorder undo |
| `tests/TaskerCore.Tests/Undo/ReorderListCommandTests.cs` | Tests for list reorder undo |

### Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Undo/IUndoableCommand.cs` | Add `[JsonDerivedType]` for both commands |
| `src/TaskerCore/Data/TodoTaskList.cs` | Add `recordUndo` parameter to `ReorderTask()` and `ReorderList()` |

### Implementation

#### ReorderTaskCommand.cs

```csharp
namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record ReorderTaskCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required string ListName { get; init; }
    public required int OldIndex { get; init; }
    public required int NewIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Reorder task in {ListName}";

    public void Execute()
    {
        TodoTaskList.ReorderTask(TaskId, NewIndex, recordUndo: false);
    }

    public void Undo()
    {
        TodoTaskList.ReorderTask(TaskId, OldIndex, recordUndo: false);
    }
}
```

#### ReorderListCommand.cs

```csharp
namespace TaskerCore.Undo.Commands;

using TaskerCore.Data;

public record ReorderListCommand : IUndoableCommand
{
    public required string ListName { get; init; }
    public required int OldIndex { get; init; }
    public required int NewIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Reorder {ListName} list";

    public void Execute()
    {
        TodoTaskList.ReorderList(ListName, NewIndex, recordUndo: false);
    }

    public void Undo()
    {
        TodoTaskList.ReorderList(ListName, OldIndex, recordUndo: false);
    }
}
```

#### IUndoableCommand.cs Registration

```csharp
[JsonDerivedType(typeof(ReorderTaskCommand), "reorderTask")]
[JsonDerivedType(typeof(ReorderListCommand), "reorderList")]
```

#### TodoTaskList.ReorderTask() Modification

```csharp
public static void ReorderTask(string taskId, int newIndex, bool recordUndo = true)
{
    // ... existing validation ...

    // Find current index BEFORE modification
    var taskIndex = list.Tasks.ToList().FindIndex(t => t.Id == taskId);

    // Early return if no change needed
    if (taskIndex == newIndex)
        return;

    if (recordUndo)
    {
        UndoManager.Instance.RecordCommand(new ReorderTaskCommand
        {
            TaskId = taskId,
            ListName = task.ListName,
            OldIndex = taskIndex,
            NewIndex = Math.Clamp(newIndex, 0, list.Tasks.Length - 1)
        });
    }

    // ... existing reorder logic ...

    if (recordUndo)
    {
        UndoManager.Instance.SaveHistory();
    }
}
```

#### TodoTaskList.ReorderList() Modification

```csharp
public static void ReorderList(string listName, int newIndex, bool recordUndo = true)
{
    // ... existing validation ...

    // Find current index BEFORE modification
    var listIndex = taskLists.FindIndex(l => l.ListName == listName);

    // Early return if no change needed
    if (listIndex == newIndex)
        return;

    if (recordUndo)
    {
        UndoManager.Instance.RecordCommand(new ReorderListCommand
        {
            ListName = listName,
            OldIndex = listIndex,
            NewIndex = Math.Clamp(newIndex, 0, taskLists.Count - 1)
        });
    }

    // ... existing reorder logic ...

    if (recordUndo)
    {
        UndoManager.Instance.SaveHistory();
    }
}
```

## Acceptance Criteria

- [x] `ReorderTaskCommand` implements `IUndoableCommand`
- [x] `ReorderListCommand` implements `IUndoableCommand`
- [x] Both commands registered with `[JsonDerivedType]` in `IUndoableCommand.cs`
- [x] `ReorderTask()` records undo command when `recordUndo: true`
- [x] `ReorderList()` records undo command when `recordUndo: true`
- [x] Undo restores task/list to original position
- [x] Redo moves task/list back to new position
- [x] Commands serialize/deserialize correctly (survives app restart)
- [x] No undo recorded when `newIndex == currentIndex` (no-op)

## Quality Gates

- [x] Unit tests for `ReorderTaskCommand` (undo, redo, serialization)
- [x] Unit tests for `ReorderListCommand` (undo, redo, serialization)
- [x] Tests use isolated storage (`StoragePaths.SetDirectory(testDir)`)
- [x] Tests use `[Collection("UndoTests")]` for sequential execution
- [x] All existing tests pass

## Known Limitations

- If list is deleted (not currently undoable), reorder undo for that list will fail
- Future work: Add `DeleteListCommand` for full undo coverage

## References

### Internal References
- Undo system: `src/TaskerCore/Undo/UndoManager.cs`
- Similar command: `src/TaskerCore/Undo/Commands/RenameListCommand.cs`
- Reorder methods: `src/TaskerCore/Data/TodoTaskList.cs:962-1041`
- Test pattern: `tests/TaskerCore.Tests/Undo/RenameListCommandTests.cs`

### Pattern Reference
- `recordUndo: false` prevents recursion when called from Execute/Undo
- Capture state BEFORE modification
- Save history AFTER file save
