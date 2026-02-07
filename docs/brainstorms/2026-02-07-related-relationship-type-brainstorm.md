# Related Relationship Type

**Date:** 2026-02-07
**Task:** #0e0

## What We're Building

A new bidirectional "related" relationship type between tasks. Unlike blocking (directional: A blocks B) or subtasks (hierarchical: A is child of B), "related" is symmetric — if task A is related to B, then B is automatically related to A.

Users set it via inline metadata with the `~` prefix (e.g., `~abc` on the metadata line). The system automatically mirrors the relationship on the target task. A task can be related to any number of other tasks but not to itself.

## Why This Approach

The "related" relationship fills a gap between the existing types. Blocking implies dependency and affects workflow. Subtasks imply hierarchy and ownership. "Related" is purely associative — it says "these tasks are connected" without implying order, dependency, or hierarchy. This is useful for grouping tasks that share context, are alternatives to each other, or simply need cross-referencing.

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Inline prefix | `~` (tilde) | Not used by existing metadata. Visually suggests "approximately associated." |
| Display icon | `~` | Consistent with the prefix character. |
| Display color | Cyan/blue | Distinguishes from subtask (gray) and blocking (yellow/amber). |
| Storage | New `task_relations` junction table | Separates concerns from directional `task_dependencies`. Stores one row per pair with canonical ID ordering. |
| Cross-list | Allowed | Informational relationship like blocking, not hierarchical like subtasks. |
| Cascade behavior | None | Purely informational. No status cascades, no move cascades. |
| Circular detection | Not needed | Symmetric relationships have no direction, so cycles are meaningless. |

## Design Details

### Storage

New table `task_relations`:
```sql
task_relations (
    task_id_1 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    task_id_2 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id_1, task_id_2),
    CHECK (task_id_1 < task_id_2),
    CHECK (task_id_1 != task_id_2)
)
```

The `CHECK (task_id_1 < task_id_2)` constraint enforces canonical ordering — the alphabetically lower ID is always in column 1. This prevents duplicate pairs (A,B) and (B,A) from coexisting and simplifies queries.

### Querying

To get all tasks related to task X:
```sql
SELECT task_id_2 FROM task_relations WHERE task_id_1 = @id
UNION
SELECT task_id_1 FROM task_relations WHERE task_id_2 = @id
```

### Display (Three-Surface Consistency)

```
CLI List:   ~ Related to (abc) Task title truncated...    [cyan dim]
CLI Get:    Related:
                     (abc) Task description                [cyan]
TUI:        ~ Related to (abc) Task title truncated...    [cyan dim]
TaskerTray: ~ Related to (abc) Task title truncated...    #5FB3B3 (cyan)
```

### Inline Metadata

On the metadata line (last line of description), `~abc` marks a relationship. Multiple are allowed: `~abc ~def`. When set on a task, the system automatically adds the reciprocal `~` reference to the target task's metadata.

Metadata line ordering: `^parent !blocks ~related p1 @date #tags`

### Bidirectional Auto-Sync

When creating/editing task X with `~abc` in metadata:
1. Insert (min(X, abc), max(X, abc)) into `task_relations`
2. Update task abc's description metadata to include `~X` (via `SyncMetadataToDescription`)

When removing `~abc` from task X's metadata:
1. Delete the row from `task_relations`
2. Update task abc's description metadata to remove `~X`

### Undo Commands

- `AddRelatedCommand` — undoes by removing the relation and updating both descriptions
- `RemoveRelatedCommand` — undoes by re-adding the relation and updating both descriptions

### CLI Commands

```
tasker deps add-related <taskId1> <taskId2>
tasker deps remove-related <taskId1> <taskId2>
```

## Open Questions

None — all design decisions resolved during brainstorm.

## Touchpoints

- `TaskerCore/Data/TaskerDb.cs` — new table creation
- `TaskerCore/Parsing/TaskDescriptionParser.cs` — new regex, parse, strip, sync
- `TaskerCore/Data/TodoTaskList.cs` — AddRelated, RemoveRelated, GetRelated, SyncRelatedRelationships
- `TaskerCore/Undo/Commands/` — AddRelatedCommand, RemoveRelatedCommand
- `TaskerCore/Undo/IUndoableCommand.cs` — register new types
- `AppCommands/DepsCommand.cs` — add-related, remove-related subcommands
- `AppCommands/ListCommand.cs` — related display line
- `AppCommands/GetCommand.cs` — related display section + JSON
- `Tui/TuiRenderer.cs` — related display + CountTaskLines
- `TaskerTray/ViewModels/TodoTaskViewModel.cs` — RelatedDisplay, HasRelated
- `TaskerTray/Views/TaskListPopup.axaml.cs` — render related lines
- Tests: parsing, data layer, undo/redo
- Docs: inline-metadata.md, models-and-schema.md, conventions.md
