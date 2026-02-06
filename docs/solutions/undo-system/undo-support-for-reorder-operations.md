---
title: Undo Support for Reorder Operations
category: undo-system
module: TaskerCore.Undo
tags:
  - undo-redo
  - task-reorder
  - list-reorder
  - drag-drop
  - IUndoableCommand
  - recordUndo-pattern
files_changed:
  - src/TaskerCore/Undo/Commands/ReorderTaskCommand.cs
  - src/TaskerCore/Undo/Commands/ReorderListCommand.cs
  - src/TaskerCore/Undo/IUndoableCommand.cs
  - src/TaskerCore/Data/TodoTaskList.cs
symptoms:
  - Users cannot undo accidental task reordering via drag-drop
  - Users cannot undo accidental list reordering via drag-drop
  - Reorder operations produce no undo history entries
date_solved: 2026-02-05
---

# Undo Support for Reorder Operations

## Overview

This solution adds undo/redo support for `ReorderTask()` and `ReorderList()` operations, enabling users to restore original task/list order after accidental drag-drop reordering in TaskerTray.

## Problem Statement

The reorder functionality existed for tasks and lists (drag-drop in TaskerTray), but these operations were not integrated with the undo system:

- `ReorderTask(taskId, newIndex)` moved tasks but wasn't undoable
- `ReorderList(listName, newIndex)` moved lists but wasn't undoable
- Users had no way to restore original order after mistakes

## Solution

### 1. Create Command Classes

Two new immutable record types implementing `IUndoableCommand`:

**ReorderTaskCommand.cs**
```csharp
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

**ReorderListCommand.cs**
```csharp
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

### 2. Register with JsonDerivedType

Add to `IUndoableCommand.cs`:

```csharp
[JsonDerivedType(typeof(ReorderTaskCommand), "reorderTask")]
[JsonDerivedType(typeof(ReorderListCommand), "reorderList")]
```

### 3. Add recordUndo Parameter to Data Methods

Modify `TodoTaskList.ReorderTask()` and `ReorderList()`:

```csharp
public static void ReorderTask(string taskId, int newIndex, bool recordUndo = true)
{
    // ... find task and validate ...

    var clampedNewIndex = Math.Clamp(newIndex, 0, tasks.Count - 1);

    if (taskIndex == clampedNewIndex)
        return;  // No-op: don't record undo

    // Record BEFORE modification
    if (recordUndo)
    {
        UndoManager.Instance.RecordCommand(new ReorderTaskCommand
        {
            TaskId = taskId,
            ListName = list.ListName,
            OldIndex = taskIndex,
            NewIndex = clampedNewIndex
        });
    }

    // Perform reorder
    var task = tasks[taskIndex];
    tasks.RemoveAt(taskIndex);
    tasks.Insert(clampedNewIndex, task);

    // Save and persist history
    File.WriteAllText(StoragePaths.Current.AllTasksPath, JsonSerializer.Serialize(taskLists));

    if (recordUndo)
    {
        UndoManager.Instance.SaveHistory();
    }
}
```

## Key Patterns

### The recordUndo Parameter Pattern

**Critical**: Pass `recordUndo: false` when calling from `Execute()` or `Undo()` to prevent infinite recursion:

```csharp
// In command's Execute/Undo methods:
TodoTaskList.ReorderTask(TaskId, NewIndex, recordUndo: false);  // ← false!
```

### Capture State BEFORE Modification

Always capture the current index before performing the reorder:

```csharp
var taskIndex = /* find current index */;

if (recordUndo)
{
    UndoManager.Instance.RecordCommand(new ReorderTaskCommand
    {
        OldIndex = taskIndex,      // Current position
        NewIndex = clampedNewIndex // Target position
    });
}

// Now perform the reorder
```

### No-Op Detection

Skip undo recording when no actual change occurs:

```csharp
if (taskIndex == clampedNewIndex)
    return;  // Don't record undo for no-op
```

## Testing Pattern

Tests use `[Collection("UndoTests")]` for sequential execution (UndoManager singleton):

```csharp
[Collection("UndoTests")]
public class ReorderTaskCommandTests : IDisposable
{
    private readonly string _testDir;

    public ReorderTaskCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-undo-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        StoragePaths.SetDirectory(_testDir);
        UndoManager.Instance.ClearHistory();
    }

    public void Dispose()
    {
        UndoManager.Instance.ClearHistory();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }
}
```

## Related Documentation

- **Main Undo System Plan**: `docs/plans/2026-02-01-feat-undo-redo-system-plan.md`
- **List Rename Undo**: `docs/plans/2026-02-05-feat-undo-list-rename-plan.md`
- **Backup/Recovery**: `docs/solutions/data-safety/atomic-writes-and-rolling-backups.md`
- **Test Isolation**: MEMORY.md (critical safety patterns)

## State Capture Reference

| Command | Captured State |
|---------|----------------|
| ReorderTaskCommand | TaskId, ListName, OldIndex, NewIndex |
| ReorderListCommand | ListName, OldIndex, NewIndex |
