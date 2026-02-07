# Important Conventions

## Three-Surface Consistency

Display logic is duplicated across three surfaces. Changes must be applied to all three:
1. **CLI:** `Output.cs` + `ListCommand.cs`
2. **TUI:** `TuiRenderer.cs`
3. **Tray:** `TodoTaskViewModel.cs`

## Task Ordering

InProgress first, then Pending, then Done. Active tasks sorted by: priority → due date → newest. Done tasks sorted by `CompletedAt DESC`.

## Display Formatting

- Checkboxes: `[green][[x]][/]` (done), `[yellow][[-]][/]` (in-progress), `[grey][[ ]][/]` (pending)
- Priority: `[red bold]>>>[/]` / `[yellow]>>[/]` / `[blue]>[/]`
- Relationship indicators: `↑ Subtask of`, `↳ Subtask`, `⊘ Blocks`, `⊘ Blocked by`
- Completed due dates: frozen at completion time ("Completed Xd late" or dim "Due: [date]")

## Cascade Operations

When operating on a task with subtask descendants:
- **SetStatus to Done** → cascades to all non-Done descendants
- **Delete** → trashes all descendants
- **Move** → moves all descendants (subtasks can't be moved independently)
- **Restore** → restores trashed descendants

## Undo System

- Commands implement `IUndoableCommand` with `Execute()` and `Undo()` methods
- Use `recordUndo: false` when calling from undo/redo to prevent recursion
- Register new commands in `IUndoableCommand.cs` with `[JsonDerivedType]`
- Cascade operations use `BeginBatch()` / `EndBatch()` to group commands

## Directory Auto-Detection

`ListManager.ResolveListFilter()` auto-detects the current directory name. If a matching list exists, commands filter to it. `--all` / `-a` bypasses this. `tasker init` creates a list for the current directory.

## TaskStatus Alias

Files using `TaskStatus` need: `using TaskStatus = TaskerCore.Models.TaskStatus;` to avoid conflict with `System.Threading.Tasks.TaskStatus`.

## Default List Protection

The "tasks" list cannot be deleted or renamed (`CannotModifyDefaultListException`).

## Sort Order

Highest `sort_order` = newest. Display uses `ORDER BY sort_order DESC`. `BumpSortOrder()` moves a task to the top after modification.
