---
title: "fix: Relative date re-evaluation on rename"
type: fix
date: 2026-02-08
task: 1dd
brainstorm: docs/brainstorms/2026-02-08-relative-date-re-evaluation-bug-brainstorm.md
---

# Fix: Relative date re-evaluation on rename

## Overview

When a task is renamed/edited, `@today` and other relative date markers are re-evaluated against the current date, silently overwriting the original due date. Fix by comparing old vs new date marker text — only re-evaluate if the marker actually changed.

## Problem

1. User creates task with `@today` → resolves to `2026-02-07`
2. Next day, user edits task text without changing `@today`
3. `Rename()` re-parses `@today` → `2026-02-08`
4. Task is no longer overdue

**Root cause:** `TodoTask.Rename()` at line 75 unconditionally sets `DueDate = parsed.DueDate`, re-resolving relative markers every time.

## Acceptance Criteria

- [x] Editing a task without changing the date marker preserves the original due date
- [x] Changing the date marker (e.g., `@today` → `@friday`) re-evaluates correctly
- [x] Adding a new date marker to a task without one sets the due date
- [x] Removing a date marker clears the due date
- [x] Fix works across CLI rename, TUI inline edit, Tray inline edit
- [x] `dotnet build` — no errors
- [x] Tests pass

## Implementation

### Step 1: Add `DueDateRaw` to `ParsedTask`

**File:** `src/TaskerCore/Parsing/TaskDescriptionParser.cs`

Add a `string? DueDateRaw` field to the `ParsedTask` record (line 17 area). This stores the raw marker text (e.g., `"today"`, `"friday"`, `"+3d"`, `"2026-02-07"`).

Set it from `dueDateMatch.Groups[1].Value` at line 76.

### Step 2: Update `Rename()` to compare date markers

**File:** `src/TaskerCore/Models/TodoTask.cs`

Change `Rename()` to accept the old parsed result and compare `DueDateRaw`:

```csharp
public TodoTask Rename(string newDescription, TaskDescriptionParser.ParsedTask? oldParsed = null)
{
    var trimmed = newDescription.Trim();
    var parsed = TaskDescriptionParser.Parse(trimmed);

    // Preserve existing due date if the date marker text hasn't changed
    var newDueDate = parsed.DueDate;
    if (oldParsed != null && parsed.DueDateRaw == oldParsed.DueDateRaw)
        newDueDate = DueDate; // keep existing

    return this with
    {
        Description = trimmed,
        Priority = parsed.Priority,
        DueDate = newDueDate,
        Tags = parsed.Tags.Length > 0 ? parsed.Tags : null,
        ParentId = parsed.LastLineIsMetadataOnly ? parsed.ParentId : ParentId
    };
}
```

### Step 3: Pass old parsed to Rename() in RenameTask()

**File:** `src/TaskerCore/Data/TodoTaskList.cs`

At line 783, change:
```csharp
var renamedTask = todoTask.Rename(newDescription);
```
to:
```csharp
var renamedTask = todoTask.Rename(newDescription, oldParsed);
```

`oldParsed` is already available at line 768.

### Step 4: Add tests

**File:** `tests/TaskerCore.Tests/Data/RenameDatePreservationTests.cs` (new)

Test cases:
1. Rename text without changing `@today` → due date preserved
2. Change `@today` to `@friday` → due date re-evaluated
3. Add `@today` to task without date → due date set
4. Remove date marker → due date cleared
5. Rename with no metadata line → due date preserved
6. Rename with same absolute date marker `@2026-02-07` → preserved

## Files Changed

| File | Change |
|------|--------|
| `src/TaskerCore/Parsing/TaskDescriptionParser.cs` | Add `DueDateRaw` field to `ParsedTask` |
| `src/TaskerCore/Models/TodoTask.cs` | Compare old vs new date markers in `Rename()` |
| `src/TaskerCore/Data/TodoTaskList.cs` | Pass `oldParsed` to `Rename()` |
| `tests/TaskerCore.Tests/Data/RenameDatePreservationTests.cs` | New test file |

## References

- Bug task: 1dd
- Brainstorm: `docs/brainstorms/2026-02-08-relative-date-re-evaluation-bug-brainstorm.md`
- `TodoTask.Rename()`: `src/TaskerCore/Models/TodoTask.cs:66-81`
- `RenameTask()`: `src/TaskerCore/Data/TodoTaskList.cs:760-796`
- `TaskDescriptionParser.Parse()`: `src/TaskerCore/Parsing/TaskDescriptionParser.cs:30-130`
