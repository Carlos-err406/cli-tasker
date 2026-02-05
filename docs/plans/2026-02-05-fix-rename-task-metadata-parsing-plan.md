---
title: fix: Rename task should parse and apply inline metadata
type: fix
date: 2026-02-05
---

# Fix: Rename Task Should Parse and Apply Inline Metadata

## Overview

When renaming a task (via CLI, TUI, or TaskerTray), if the new description contains inline metadata (e.g., `p1`, `@tomorrow`, `#urgent`), the metadata is stored as text but not parsed and applied to the task's structured fields.

## Problem Statement

**Current behavior:**
```bash
# Create task with priority
tasker add "my task
p1"  # Creates task with Priority.High

# Edit the task to change priority
tasker rename abc "my task
p2"  # Text changes to "my task\np2" but Priority stays at High!
```

The same issue affects TaskerTray's inline edit and TUI's rename feature.

**Expected behavior:**
Renaming a task should re-parse the description for metadata and update the task's `Priority`, `DueDate`, and `Tags` fields accordingly.

## Root Cause

`TodoTaskList.RenameTask()` (line 491-521 in `TodoTaskList.cs`) only calls `todoTask.Rename(newDescription)` which updates the description text but doesn't parse metadata:

```csharp
public TaskResult RenameTask(string taskId, string newDescription, bool recordUndo = true)
{
    // ...
    RemoveTaskFromTaskLists(taskId);
    var renamedTask = todoTask.Rename(newDescription);  // ← Just updates text!
    AddTaskToList(renamedTask);
    Save();
}
```

Compare this to `AddTodoTask` flows which correctly parse metadata via `TaskDescriptionParser.Parse()` before creating the task.

## Solution

Update `TodoTask.Rename()` to parse and apply metadata from the new description. Since `Priority`, `DueDate`, and `Tags` are all `TodoTask` attributes, the `Rename()` method is the natural place to handle this - following the same pattern as `this with { Description = ... }` but including the parsed metadata.

This keeps the logic centralized in the model and ensures any code that calls `Rename()` gets the metadata parsing behavior automatically.

## Acceptance Criteria

- [ ] Renaming a task with `p1`/`p2`/`p3` on the last line updates `Priority` field
- [ ] Renaming a task with `@date` on the last line updates `DueDate` field
- [ ] Renaming a task with `#tag` on the last line updates `Tags` field
- [ ] Removing metadata (e.g., editing `p1` out) clears the corresponding field
- [ ] CLI `tasker rename` command works correctly
- [ ] TUI rename (R key) works correctly
- [ ] TaskerTray inline edit works correctly

## Implementation

### TodoTask.cs

Update `Rename()` method to parse and apply metadata:

```csharp
using TaskerCore.Parsing;

public TodoTask Rename(string newDescription)
{
    var trimmed = newDescription.Trim();
    var parsed = TaskDescriptionParser.Parse(trimmed);

    return this with
    {
        Description = trimmed,
        Priority = parsed.Priority,
        DueDate = parsed.DueDate,
        Tags = parsed.Tags.Length > 0 ? parsed.Tags : null
    };
}
```

This is cleaner because:
1. The metadata is part of `TodoTask`'s state - `Rename()` should update all relevant state
2. Single place to maintain - any caller of `Rename()` gets correct behavior
3. Follows the existing pattern of `this with { ... }` for immutable updates

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Models/TodoTask.cs` | Update `Rename()` to parse and apply metadata |

## Verification

```bash
# Test CLI
tasker add "test task"
tasker list  # Note the task ID (e.g., abc)
tasker rename abc "updated task
p1 @tomorrow #urgent"
tasker list  # Should show >>> priority, tomorrow date, #urgent tag

# Test removing metadata
tasker rename abc "just text"
tasker list  # Should show no priority, no date, no tags

# Test TUI
tasker tui
# Press R on a task, change to "new text\np2 #work", press Enter
# Task should show >> priority and #work tag

# Test TaskerTray
# Click menu → Edit on a task
# Change text to include metadata
# Verify metadata is applied
```

## Related

- Previous fix: Metadata sync for `SetTaskPriority` and `SetTaskDueDate` (commit 6d06b46)
- Documentation: `docs/solutions/feature-implementations/task-metadata-inline-system.md`
