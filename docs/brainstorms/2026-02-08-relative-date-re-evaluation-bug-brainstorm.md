# Brainstorm: Relative Date Re-evaluation Bug

**Date:** 2026-02-08
**Task:** 1dd — "tasks with relative due dates"

## What We're Building

Fix a bug where renaming/editing a task re-evaluates relative date markers (`@today`, `@tomorrow`, `@friday`, `@+3d`) against the current date, silently overwriting the original due date.

**Reproduction:** Create task with `@today` (resolves to 2026-02-07). Next day, edit the task text without changing the date marker. `@today` re-evaluates to 2026-02-08, and the task is no longer overdue.

**Root cause:** `TodoTask.Rename()` unconditionally re-parses `DueDate` from the description, which re-resolves relative dates. It can't distinguish between "user left `@today` unchanged" and "user changed the date marker to `@today`".

## Why This Approach

- **Resolve once at creation, freeze it** — relative dates are evaluated at task creation time and stored as absolute dates in the DB. On rename, only re-evaluate if the user actually changed the date marker text.
- **Compare old vs new metadata** — diff the date marker text from the old and new descriptions. If the marker is the same (both `@today`, or both `@friday`), preserve the existing `DueDate`. Only re-evaluate when the marker text actually changed or was newly added/removed.
- **Fix at data layer** — the fix goes in `TodoTask.Rename()` or `TodoTaskList.RenameTask()`, so all three surfaces (CLI, TUI, Tray) benefit automatically.

## Key Decisions

1. **Resolve relative dates once at creation** — don't re-evaluate on edit unless the marker changed
2. **Compare old vs new date marker text** — detect changes by diffing the raw marker strings
3. **Fix only due date** — priority and tags don't have the same time-dependent staleness issue
4. **All surfaces fixed** — single fix at data layer covers CLI rename, TUI inline edit, Tray inline edit

## Open Questions

None — the approach is clear.
