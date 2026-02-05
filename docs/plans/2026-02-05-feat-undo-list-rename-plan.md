---
title: Add undo support for list rename
type: feat
date: 2026-02-05
---

# Add Undo Support for List Rename

## Overview

Add undo/redo capability for list rename operations in both CLI and TaskerTray. This follows the existing command pattern used for task operations.

## Acceptance Criteria

- [x] `tasker undo` reverses a list rename
- [x] `tasker redo` re-applies an undone list rename
- [x] Cmd+Z in TaskerTray undoes list rename
- [x] Undo history persists across sessions
- [x] If renamed list was the default, undo restores default setting
- [x] Graceful error handling when undo isn't possible (list deleted/name conflict)

## Technical Approach

### 1. Create RenameListCommand

**File:** `src/TaskerCore/Undo/Commands/RenameListCommand.cs`

```csharp
namespace TaskerCore.Undo.Commands;

public record RenameListCommand : IUndoableCommand
{
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public required bool WasDefaultList { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description => $"Rename list: {OldName} to {NewName}";

    public void Execute()
    {
        // Re-apply the rename (for redo)
        ListManager.RenameList(OldName, NewName, recordUndo: false);
    }

    public void Undo()
    {
        // Reverse the rename
        try
        {
            ListManager.RenameList(NewName, OldName, recordUndo: false);

            // Restore default list if it was changed
            if (WasDefaultList && AppConfig.GetDefaultList() == NewName)
            {
                AppConfig.SetDefaultList(OldName);
            }
        }
        catch (ListNotFoundException)
        {
            throw new InvalidOperationException($"Cannot undo: list '{NewName}' no longer exists");
        }
        catch (ListAlreadyExistsException)
        {
            throw new InvalidOperationException($"Cannot undo: list '{OldName}' already exists");
        }
    }
}
```

### 2. Register in IUndoableCommand

**File:** `src/TaskerCore/Undo/IUndoableCommand.cs`

Add the JsonDerivedType attribute:

```csharp
[JsonDerivedType(typeof(RenameListCommand), "renameList")]
```

### 3. Update ListManager.RenameList

**File:** `src/TaskerCore/Data/ListManager.cs`

Add `recordUndo` parameter:

```csharp
public static TaskResult RenameList(string oldName, string newName, bool recordUndo = true)
{
    // ... existing validation ...

    if (recordUndo)
    {
        var wasDefault = AppConfig.GetDefaultList() == oldName;
        var cmd = new RenameListCommand
        {
            OldName = oldName,
            NewName = newName,
            WasDefaultList = wasDefault
        };
        UndoManager.Instance.RecordCommand(cmd);
    }

    TodoTaskList.RenameList(oldName, newName);

    // ... existing default list update logic ...

    if (recordUndo)
    {
        UndoManager.Instance.SaveHistory();
    }

    return new TaskResult.Success($"Renamed list '{oldName}' to '{newName}'");
}
```

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| List deleted after rename | Undo fails: "Cannot undo: list 'X' no longer exists" |
| Old name now taken | Undo fails: "Cannot undo: list 'X' already exists" |
| Default changed externally | Only restore default if current default == NewName |

## Files to Modify

1. **`src/TaskerCore/Undo/Commands/RenameListCommand.cs`** (NEW)
2. **`src/TaskerCore/Undo/IUndoableCommand.cs`** - Add JsonDerivedType
3. **`src/TaskerCore/Data/ListManager.cs`** - Add recordUndo parameter

## No UI Changes Needed

TaskerTray already has Undo/Redo support via `AppViewModel`. The new command will work automatically through the existing infrastructure.

## Verification

```bash
# Test basic undo/redo cycle
tasker lists create testlist
tasker add "test task" -l testlist
tasker lists rename testlist renamed
tasker undo           # Should rename back to "testlist"
tasker redo           # Should rename to "renamed" again

# Test with default list
tasker lists set-default testlist
tasker lists rename testlist newname
tasker undo           # Should restore default to "testlist"

# Test edge case - name conflict
tasker lists rename alpha beta
tasker lists create alpha
tasker undo           # Should fail: "alpha already exists"
```
