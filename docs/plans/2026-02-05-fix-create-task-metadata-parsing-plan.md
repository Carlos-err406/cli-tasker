---
title: fix: CreateTodoTask should parse inline metadata
type: fix
date: 2026-02-05
---

# Fix: CreateTodoTask Should Parse Inline Metadata

## Problem

When creating a task via some code paths (notably `AppViewModel.AddTask()`), inline metadata (`p1`, `@date`, `#tags`) is not parsed - it's stored as plain text.

**Root cause:** `TodoTask.CreateTodoTask()` doesn't parse metadata from the description. Most callers manually call `TaskDescriptionParser.Parse()` before creating the task, but some don't.

## Acceptance Criteria

- [ ] `CreateTodoTask()` automatically parses metadata from the description
- [ ] All existing add flows continue to work correctly
- [ ] Callers that manually parse can be simplified (remove duplicate parsing)

## Solution

Apply the same pattern used for `Rename()` - parse metadata inside `CreateTodoTask()` so all callers get consistent behavior.

### TodoTask.cs

```csharp
public static TodoTask CreateTodoTask(string description, string listName)
{
    var trimmed = description.Trim();
    var parsed = TaskDescriptionParser.Parse(trimmed);

    return new TodoTask(
        Guid.NewGuid().ToString()[..3],
        trimmed,
        false,
        DateTime.Now,
        listName,
        parsed.DueDate,
        parsed.Priority,
        parsed.Tags.Length > 0 ? parsed.Tags : null
    );
}
```

### Simplify Callers (Optional)

Once `CreateTodoTask` handles parsing, callers can be simplified:

```csharp
// Before (AddCommand.cs, TuiKeyHandler.cs, TaskListPopup.axaml.cs)
var parsed = TaskDescriptionParser.Parse(description);
var task = TodoTask.CreateTodoTask(parsed.Description, listName);
if (parsed.Priority.HasValue)
    task = task.SetPriority(parsed.Priority.Value);
if (parsed.DueDate.HasValue)
    task = task.SetDueDate(parsed.DueDate.Value);
if (parsed.Tags.Length > 0)
    task = task.SetTags(parsed.Tags);

// After
var task = TodoTask.CreateTodoTask(description, listName);
```

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Models/TodoTask.cs` | Update `CreateTodoTask()` to parse metadata |
| `AppCommands/AddCommand.cs` | Simplify - remove manual parsing |
| `Tui/TuiKeyHandler.cs` | Simplify - remove manual parsing |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Simplify - remove manual parsing |

## Verification

```bash
# All these should parse metadata automatically:
tasker add "test task
p1 @tomorrow #urgent"
# → Should show >>> priority, tomorrow date, #urgent tag

# TaskerTray inline add should also work
# TUI add should also work
```
