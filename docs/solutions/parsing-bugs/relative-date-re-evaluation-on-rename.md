---
title: Relative date re-evaluation on rename
category: parsing-bugs
module: TaskerCore.Parsing, TaskerCore.Models
tags: [date-parsing, rename, metadata, relative-dates]
symptoms:
  - Task due date silently changes when editing task text
  - Overdue tasks become current after rename
  - "@today marker re-evaluates to current date on every edit"
date: 2026-02-08
---

# Relative Date Re-evaluation on Rename

## Problem

When a task was created with a relative date marker like `@today`, the due date was correctly resolved to an absolute date (e.g., `2026-02-07`). However, the description text still contained the raw `@today` marker. When the user later renamed/edited the task text — even without touching the date marker — `TodoTask.Rename()` re-parsed `@today` against the current date, silently overwriting the stored due date.

**Reproduction:**
1. `tasker add "buy milk\n@today"` on Feb 7 → due date = 2026-02-07
2. Next day (Feb 8), `tasker rename <id> "buy almond milk\n@today"`
3. Due date becomes 2026-02-08 — task is no longer overdue

## Root Cause

`TodoTask.Rename()` unconditionally set `DueDate = parsed.DueDate`, where `parsed` was freshly parsed from the new description. Since relative date markers resolve relative to `DateTime.Today`, the same `@today` text produces a different absolute date on different days.

```csharp
// BEFORE (buggy)
public TodoTask Rename(string newDescription)
{
    var parsed = TaskDescriptionParser.Parse(trimmed);
    return this with
    {
        DueDate = parsed.DueDate,  // Always re-evaluates!
        ...
    };
}
```

The system couldn't distinguish between:
- "User left `@today` unchanged" → should preserve existing date
- "User changed marker to `@today`" → should re-evaluate

## Solution

Added `DueDateRaw` field to `ParsedTask` to expose the raw marker text (e.g., `"today"`, `"friday"`, `"2026-02-07"`). `Rename()` now accepts the old parsed result and compares raw marker text — only re-evaluates when the marker actually changed.

**`TaskDescriptionParser.cs`** — Added `DueDateRaw` to the record:
```csharp
public record ParsedTask(
    ...,
    string? DueDateRaw = null);  // Raw marker text for comparison
```

**`TodoTask.cs`** — Compare before overwriting:
```csharp
public TodoTask Rename(string newDescription, ParsedTask? oldParsed = null)
{
    var parsed = TaskDescriptionParser.Parse(trimmed);

    var newDueDate = parsed.DueDate;
    if (oldParsed != null && parsed.DueDateRaw == oldParsed.DueDateRaw)
        newDueDate = DueDate;  // Preserve existing

    return this with { DueDate = newDueDate, ... };
}
```

**`TodoTaskList.RenameTask()`** — Pass the already-available `oldParsed`:
```csharp
var renamedTask = todoTask.Rename(newDescription, oldParsed);
```

## Key Insight

The `oldParsed` parameter defaults to `null`, which preserves the old behavior (always re-evaluate). This is intentional — callers like `SetTaskDueDate` and `SetTaskPriority` use `SyncMetadataToDescription` which produces absolute date markers, so re-parsing is safe. Only `RenameTask()` passes `oldParsed` to enable comparison.

## Prevention

- When storing user-facing text that contains resolvable expressions (dates, variables), always preserve the raw text alongside the resolved value for comparison on re-parse.
- Add tests that simulate time progression (e.g., set DB date directly, then rename) to catch re-evaluation bugs.
- The `DueDateRaw` field pattern can be reused if similar issues arise with other metadata types.

## Related

- `docs/solutions/feature-implementations/task-metadata-inline-system.md` — Full metadata system docs
- `docs/solutions/parsing-bugs/hyphenated-tags-not-parsed.md` — Similar parser bug pattern
- `docs/reference/inline-metadata.md` — Inline metadata reference
- PR #28
