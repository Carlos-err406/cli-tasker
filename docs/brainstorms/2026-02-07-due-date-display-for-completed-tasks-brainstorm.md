# Due Date Display for Completed Tasks

## What We're Building

Fix the due date label so completed tasks don't show "OVERDUE (Xd)" counting up forever. Instead, freeze the label based on whether the task was completed on time or late.

**Current behavior:** A task completed on Feb 5 with a due date of Feb 4 will show "OVERDUE (3d)" on Feb 7 — the counter keeps growing even though the task is done.

**Desired behavior:**
- **Completed on time** (before or on due date): Show dim `Due: [date]` — neutral, informational
- **Completed late** (after due date): Show `Completed Xd late` using the difference between `CompletedAt` and `DueDate`, frozen at completion time

## Why This Approach

The `CompletedAt` field already exists on `TodoTask` and is automatically set when a task transitions to Done. By comparing `CompletedAt` to `DueDate`, we can determine whether the task was on time or late, and by how many days — without any schema changes.

The "freeze" approach is better than hiding because it preserves useful context (was this task late?) without creating false urgency (the growing OVERDUE counter).

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| On-time display | Dim `Due: [date]` | Keeps the information visible but de-emphasized |
| Late display | `Completed Xd late` | Descriptive, clear past-tense phrasing |
| Late color | Dim (not red) | Red implies urgency; task is already done |
| Counter source | `CompletedAt - DueDate` | Frozen at completion, not growing |
| Schema changes | None | `CompletedAt` already exists |

## Display Examples

| Scenario | Current | New |
|----------|---------|-----|
| Pending, due tomorrow | `Due: Tomorrow` | `Due: Tomorrow` (unchanged) |
| Pending, overdue 3 days | `OVERDUE (3d)` | `OVERDUE (3d)` (unchanged) |
| Done on time, due was Feb 5 | `OVERDUE (2d)` | `Due: Feb 5` (dim) |
| Done 2 days late | `OVERDUE (4d)` (growing) | `Completed 2d late` (frozen) |
| Done, no due date | (none) | (none, unchanged) |

## Scope

Three places have duplicated due date formatting logic:
1. `Output.cs:FormatDueDate()` — CLI
2. `Tui/TuiRenderer.cs:FormatDueDate()` — TUI
3. `src/TaskerTray/ViewModels/TodoTaskViewModel.cs:DueDateDisplay` — Tray

All three need the same status-aware change. Consider whether to consolidate into one shared method during implementation.

## Open Questions

- Should `FormatDueDate` accept task status + completedAt, or should the caller handle the branching? (Implementation detail for planning phase)
