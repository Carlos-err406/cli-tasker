---
title: feat: Add undo support for list deletion
type: feat
date: 2026-02-05
---

# Add Undo Support for List Deletion

## Overview

Add undo/redo support for `ListManager.DeleteList()` operation. Currently, deleting a list permanently removes it and all its tasks (both active and trashed), with no way to recover.

## Problem Statement

- `ListManager.DeleteList(name)` permanently removes a list and all its tasks
- Users who accidentally delete a list have no way to recover (except backup restore)
- This is inconsistent with other list operations (rename, reorder) which are undoable

## Proposed Solution

Create `DeleteListCommand` following established undo patterns, capturing:
1. The deleted list with all its tasks
2. Any trashed tasks that belonged to this list
3. Whether it was the default list
4. Original index for position restoration

## Technical Approach

### Files to Create

| File | Purpose |
|------|---------|
| `src/TaskerCore/Undo/Commands/DeleteListCommand.cs` | Undo command for list deletion |
| `tests/TaskerCore.Tests/Undo/DeleteListCommandTests.cs` | Tests for list delete undo |

### Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Undo/IUndoableCommand.cs` | Add `[JsonDerivedType]` for DeleteListCommand |
| `src/TaskerCore/Data/ListManager.cs` | Add `recordUndo` parameter to `DeleteList()` |
| `src/TaskerCore/Data/TodoTaskList.cs` | Add `recordUndo` parameter to `DeleteList()` |

### Implementation

#### DeleteListCommand.cs

```csharp
namespace TaskerCore.Undo.Commands;

using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;

public record DeleteListCommand : IUndoableCommand
{
    public required string ListName { get; init; }
    public required TaskList DeletedList { get; init; }
    public TaskList? TrashedList { get; init; }
    public required bool WasDefaultList { get; init; }
    public required int OriginalIndex { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Delete list: {ListName}";

    public void Execute()
    {
        ListManager.DeleteList(ListName, recordUndo: false);
    }

    public void Undo()
    {
        // Restore list with tasks at original position
        TodoTaskList.RestoreList(DeletedList, TrashedList, OriginalIndex);

        // Restore default list if it was the default
        if (WasDefaultList)
        {
            AppConfig.SetDefaultList(ListName);
        }
    }
}
```

#### TodoTaskList.RestoreList() (new method)

```csharp
public static void RestoreList(TaskList activeList, TaskList? trashedList, int index)
{
    lock (SaveLock)
    {
        // Restore active list
        var raw = File.ReadAllText(StoragePaths.Current.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw).ToList();

        var clampedIndex = Math.Clamp(index, 0, taskLists.Count);
        taskLists.Insert(clampedIndex, activeList);

        File.WriteAllText(StoragePaths.Current.AllTasksPath, JsonSerializer.Serialize(taskLists.ToArray()));

        // Restore trashed list if it existed
        if (trashedList != null && File.Exists(StoragePaths.Current.AllTrashPath))
        {
            var trashRaw = File.ReadAllText(StoragePaths.Current.AllTrashPath);
            var trashLists = DeserializeWithMigration(trashRaw).ToList();
            trashLists.Add(trashedList);
            File.WriteAllText(StoragePaths.Current.AllTrashPath, JsonSerializer.Serialize(trashLists.ToArray()));
        }
    }
}
```

#### ListManager.DeleteList() modification

```csharp
public static TaskResult DeleteList(string name, bool recordUndo = true)
{
    if (name == DefaultListName)
        throw new CannotModifyDefaultListException("delete");

    if (!ListExists(name))
        throw new ListNotFoundException(name);

    if (recordUndo)
    {
        // Capture state before deletion
        var deletedList = TodoTaskList.GetListByName(name);
        var trashedList = TodoTaskList.GetTrashedListByName(name);
        var originalIndex = TodoTaskList.GetListIndex(name);
        var wasDefault = AppConfig.GetDefaultList() == name;

        UndoManager.Instance.RecordCommand(new DeleteListCommand
        {
            ListName = name,
            DeletedList = deletedList,
            TrashedList = trashedList,
            WasDefaultList = wasDefault,
            OriginalIndex = originalIndex
        });
    }

    TodoTaskList.DeleteList(name);

    if (recordUndo)
    {
        UndoManager.Instance.SaveHistory();
    }

    // Reset default if deleting the default list
    if (AppConfig.GetDefaultList() == name)
    {
        AppConfig.SetDefaultList(DefaultListName);
        return new TaskResult.Success($"Deleted list '{name}'. Default reset to '{DefaultListName}'.");
    }

    return new TaskResult.Success($"Deleted list '{name}'");
}
```

### Helper Methods Needed

Add to `TodoTaskList.cs`:

```csharp
public static TaskList GetListByName(string listName)
{
    var raw = File.ReadAllText(StoragePaths.Current.AllTasksPath);
    var taskLists = DeserializeWithMigration(raw);
    return taskLists.FirstOrDefault(l => l.ListName == listName)
        ?? throw new ListNotFoundException(listName);
}

public static TaskList? GetTrashedListByName(string listName)
{
    if (!File.Exists(StoragePaths.Current.AllTrashPath))
        return null;
    var raw = File.ReadAllText(StoragePaths.Current.AllTrashPath);
    var trashLists = DeserializeWithMigration(raw);
    return trashLists.FirstOrDefault(l => l.ListName == listName);
}

public static int GetListIndex(string listName)
{
    var raw = File.ReadAllText(StoragePaths.Current.AllTasksPath);
    var taskLists = DeserializeWithMigration(raw);
    return Array.FindIndex(taskLists, l => l.ListName == listName);
}
```

## Acceptance Criteria

- [x] `DeleteListCommand` implements `IUndoableCommand`
- [x] Command registered with `[JsonDerivedType]` in `IUndoableCommand.cs`
- [x] `ListManager.DeleteList()` records undo command when `recordUndo: true`
- [x] Undo restores list with all its tasks at original position
- [x] Undo restores trashed tasks that belonged to the list
- [x] Undo restores default list setting if it was the default
- [x] Redo deletes the list again
- [x] Commands serialize/deserialize correctly

## Quality Gates

- [x] Unit tests for `DeleteListCommand` (undo, redo, serialization)
- [x] Test with empty list, list with tasks, list with trashed tasks
- [x] Test default list deletion and restoration
- [x] Tests use `[Collection("UndoTests")]` for sequential execution
- [x] All existing tests pass (flaky test isolation issues pre-existing)

## References

### Internal References
- Similar command: `src/TaskerCore/Undo/Commands/RenameListCommand.cs`
- Clear pattern: `src/TaskerCore/Undo/Commands/ClearTasksCommand.cs`
- Delete list impl: `src/TaskerCore/Data/TodoTaskList.cs:809-837`
- Undo patterns doc: `docs/solutions/undo-system/undo-support-for-reorder-operations.md`
