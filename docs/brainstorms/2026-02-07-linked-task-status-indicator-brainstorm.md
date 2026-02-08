# Brainstorm: Show Linked Task Status in Relationship Lines

**Date:** 2026-02-07
**Task:** 03c — "when a task gets checked update linked task lines"

## What We're Building

When viewing a task's relationship lines (subtasks, blocks, blocked-by, related), show the linked task's current status as a colored label after the title. All three statuses are shown:

- **Done** → `[Done]` in green
- **In Progress** → `[In Progress]` in yellow
- **Pending** → nothing (default state, no label needed)

This applies to all five relationship types across all three surfaces (CLI, TUI, Tray).

### Example Output

```
(a3f) >>> [ ] Build the API
      ↑ Subtask of (b12) Main project [Done]
      ↳ Subtask (c45) Write tests [In Progress]
      ⊘ Blocks (d78) Deploy to staging
      ⊘ Blocked by (e90) Get credentials [Done]
      ~ Related to (f23) API docs [In Progress]
```

## Why This Approach

**Dynamic over static:** The status label is computed at display time by reading the linked task's current status from the DB. This means:

- No cascade logic needed on status changes
- Always reflects the real status
- No risk of desync between the label and reality
- Zero migration — existing data works as-is

**Label after title:** Matches the user's original task description example (`lorem ipsum [Done]`). Keeps the relationship type and task ID in their natural reading position.

**Three statuses, not just Done:** Showing In-Progress gives useful context (e.g., knowing a blocker is being worked on vs. sitting idle).

## Key Decisions

1. **Dynamic display** — read linked task status at render time, don't store it
2. **Label position** — after the task title: `~ Related to (abc) title [Done]`
3. **Color scheme** — green for Done, yellow for In Progress, nothing for Pending
4. **All surfaces** — CLI list, CLI get, TUI, Tray popup
5. **All relationship types** — parent, subtask, blocks, blocked-by, related

## Implementation Surface Map

Each surface already fetches the linked task via `GetTodoTaskById()` to get the title. The task object includes `.Status`, so no additional DB queries are needed.

| Surface | File | What Changes |
|---------|------|-------------|
| CLI list | `AppCommands/ListCommand.cs` | Append status label to each relationship line (~L144-199) |
| CLI get | `AppCommands/GetCommand.cs` | Append status label to each relationship line (~L141-190) |
| TUI | `Tui/TuiRenderer.cs` | Append status label to each relationship line (~L190-254) |
| Tray VM | `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | Include status in `*Display` strings (~L246-310) |

### Status Label Formatting

**CLI + TUI (Spectre.Console markup):**
```
[green]Done[/]        → for TaskStatus.Done
[yellow]In Progress[/] → for TaskStatus.InProgress
(nothing)              → for TaskStatus.Pending
```

**Tray (Avalonia):**
Append a colored TextBlock or inline Run to the relationship label, or include it in the display string and parse it in the popup renderer.

### Potential Helper

A small helper method could centralize the status label logic:

```csharp
// In a shared location (Output? StringHelpers?)
static string FormatLinkedStatus(TaskStatus status) => status switch
{
    TaskStatus.Done => "[green]Done[/]",
    TaskStatus.InProgress => "[yellow]In Progress[/]",
    _ => ""
};
```

The tray would need its own variant since it uses Avalonia colors, not Spectre markup.

## Open Questions

1. **Truncation interaction** — when the title is truncated to 40 chars, should the status label come after the truncated title or be included in the truncation budget? (Recommendation: after truncation — the label is metadata, not part of the title)
2. **Tray rendering** — should the status label be a separate colored TextBlock, or part of the same text with color parsing? (Recommendation: separate inline element for clean color control)

## Scope

- ~4 files changed (+ optional shared helper)
- No DB changes, no migration
- No new commands or config
- Pure display change

## Next Steps

Run `/workflows:plan` to create the implementation plan.
