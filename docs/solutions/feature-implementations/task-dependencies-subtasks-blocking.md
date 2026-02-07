---
title: Task Dependencies — Subtasks and Blocking Relationships
category: feature-implementations
tags:
  - dependencies
  - subtasks
  - blocking
  - parsing
  - metadata
  - schema
  - undo
  - cascade
  - tray
  - tui
  - cli
module: TaskerCore.Data
symptoms:
  - no way to relate tasks to each other
  - no parent/child task hierarchy
  - no blocking/dependency tracking between tasks
date_solved: 2026-02-07
files_changed:
  - src/TaskerCore/Data/TaskerDb.cs
  - src/TaskerCore/Data/TodoTaskList.cs
  - src/TaskerCore/Models/TodoTask.cs
  - src/TaskerCore/Parsing/TaskDescriptionParser.cs
  - src/TaskerCore/Undo/IUndoableCommand.cs
  - src/TaskerCore/Undo/Commands/SetParentCommand.cs
  - src/TaskerCore/Undo/Commands/AddBlockerCommand.cs
  - src/TaskerCore/Undo/Commands/RemoveBlockerCommand.cs
  - AppCommands/DepsCommand.cs
  - AppCommands/AddCommand.cs
  - AppCommands/GetCommand.cs
  - AppCommands/ListCommand.cs
  - Program.cs
  - Tui/TuiRenderer.cs
  - Tui/TuiKeyHandler.cs
  - src/TaskerTray/ViewModels/TodoTaskViewModel.cs
  - src/TaskerTray/ViewModels/TaskListViewModel.cs
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
  - tests/TaskerCore.Tests/Parsing/TaskDescriptionParserTests.cs
  - tests/TaskerCore.Tests/Data/TaskDependencyTests.cs
  - tests/TaskerCore.Tests/Undo/UndoDependencyTests.cs
  - tests/TaskerCore.Tests/Undo/UndoSerializationTest.cs
---

# Task Dependencies — Subtasks and Blocking Relationships

## Overview

Adds two relationship types between tasks:

1. **Subtasks** (`^parentId`): Hierarchical parent-child tree. Single parent per task, unlimited nesting. Same-list constraint. Cascade check/trash/restore/move.
2. **Blocking** (`!blockedId`): Many-to-many dependency graph. Cross-list allowed. Visual only (informational, never prevents actions). Circular blocking prevented.

Both use the existing inline metadata system — tokens on the last line of the description.

## Schema

### New column on `tasks`

```sql
ALTER TABLE tasks ADD COLUMN parent_id TEXT REFERENCES tasks(id) ON DELETE CASCADE;
```

### New join table

```sql
CREATE TABLE IF NOT EXISTS task_dependencies (
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    blocks_task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id, blocks_task_id),
    CHECK (task_id != blocks_task_id)
);
```

Migration is idempotent via `MigrateAddDependencies()` in `TaskerDb`. Clears undo history (old commands lack new fields).

## Parser Integration

Extended `TaskDescriptionParser` with two new token types:

```
^abc     → ParentId = "abc"     (subtask of task abc)
!h67     → BlocksIds = ["h67"]  (this task blocks h67)
!h67 !j89 → BlocksIds = ["h67", "j89"]  (blocks multiple)
```

Regex patterns use word boundaries to avoid false positives:

```csharp
[GeneratedRegex(@"(?:^|\s)\^(\w{3,})(?=\s|$)")]
private static partial Regex ParentRefRegex();

[GeneratedRegex(@"(?:^|\s)!(\w{3,})(?=\s|$)")]
private static partial Regex BlocksRefRegex();
```

Tokens are only parsed from metadata-only last lines (same rules as `p1`, `@today`, `#tag`). `GetDisplayDescription()` hides them from display. `SyncMetadataToDescription()` includes them.

## Data Layer Operations

All in `TodoTaskList`:

| Method | Description |
|--------|-------------|
| `SetParent(taskId, parentId)` | Sets parent, validates same-list + no cycles |
| `UnsetParent(taskId)` | Clears parent_id |
| `AddBlocker(blockerId, blockedId)` | Inserts into task_dependencies, validates no cycles |
| `RemoveBlocker(blockerId, blockedId)` | Deletes from task_dependencies |
| `GetSubtasks(taskId)` | Direct children |
| `GetAllDescendantIds(taskId)` | Full tree via recursive CTE |
| `GetBlocks(taskId)` | Tasks this one blocks |
| `GetBlockedBy(taskId)` | Tasks blocking this one |
| `GetBlocksIds(taskId)` | IDs only (for sync comparison) |
| `HasCircularBlocking(blockerId, blockedId)` | Walks graph to detect cycles |
| `SyncBlockingRelationships(taskId, old, new)` | Diffs and updates blocker rows |

### Cascade Semantics

| Operation | Cascades? | Notes |
|-----------|-----------|-------|
| Check (Done) | Yes, down | Parent done → all non-Done descendants done |
| Uncheck | No | Only `tasker undo` reverses |
| Trash | Yes, down | Parent trashed → all descendants trashed |
| Restore | Yes, down | Parent restored → all descendants restored |
| Move | Yes, down | Parent moved → all descendants moved |
| Move subtask alone | Blocked | Error: "unset-parent first" |

Cascade uses `GetAllDescendantIds()` (recursive CTE) and wraps in `BeginBatch()`/`EndBatch()` for undo.

## CLI Commands

### `tasker deps` subcommand group

```
tasker deps set-parent <taskId> <parentId>
tasker deps unset-parent <taskId>
tasker deps add-blocker <blockerId> <blockedId>
tasker deps remove-blocker <blockerId> <blockedId>
```

### Inline syntax on add

```
tasker add "build API ^abc"     # subtask of abc
tasker add "fix bug !h67"       # blocks h67
```

### Get command

`tasker get <id>` shows relationships:

```
Parent:      (abc) build mobile app
Subtasks:    (de1) research frameworks
             (f23) build prototype
Blocks:      (h67) deploy to production
Blocked by:  (g45) write documentation
```

`tasker get <id> --json` includes `parentId`, `subtasks`, `blocks`, `blockedBy` fields.

### List command

Relationship indicator lines below tasks in `tasker list` output (dim text).

## TUI

- Relationship lines rendered below tasks with prefix icons
- `s` key creates subtask of highlighted task (pre-fills `^parentId`)
- Cascade feedback in status bar ("Checked (abc) and N subtasks")
- `CountTaskLines()` accounts for relationship line height

## Tray Display

### ViewModel (`TodoTaskViewModel`)

```csharp
public string? ParentDisplay { get; private set; }
public string[]? SubtasksDisplay { get; private set; }
public string[]? BlocksDisplay { get; private set; }
public string[]? BlockedByDisplay { get; private set; }
```

`LoadRelationships(TodoTaskList)` populates these with `"(id) title"` strings.

### Rendering (`TaskListPopup.axaml.cs`)

Each relationship gets its own TextBlock line with prefix icon:
- `↑` subtask of (gray)
- `↳` subtask (gray)
- `⊘` blocks / blocked by (orange `#D4A054`)

Uses `TextTrimming.CharacterEllipsis` for dynamic truncation.

### Loading

Both `TaskListViewModel.LoadTasks()` and `TaskListPopup.DoRefreshTasks()` call `LoadRelationships()` on each VM after construction.

## Undo System

Three new commands registered in `IUndoableCommand.cs`:

| Command | Execute | Undo |
|---------|---------|------|
| `SetParentCommand` | `SetParent()` or `UnsetParent()` | Reverse |
| `AddBlockerCommand` | `AddBlocker()` | `RemoveBlocker()` |
| `RemoveBlockerCommand` | `RemoveBlocker()` | `AddBlocker()` |

Cascade operations (check, trash, move) use `BeginBatch()`/`EndBatch()` to group sub-commands.

## Bugs Fixed During Development

### 1. Tray not loading relationship data

**Root cause**: `DoRefreshTasks()` in `TaskListPopup.axaml.cs` creates VMs independently from `TaskListViewModel` — it never called `LoadRelationships()`.

**Fix**: Added `LoadRelationships()` call in `DoRefreshTasks()` after constructing each VM.

### 2. Rename not syncing dependency tokens

**Root cause**: `TodoTask.Rename()` used `ParentId = parsed.ParentId ?? ParentId` which preserved old parent when new description had no `^` token (even if user explicitly removed it). Blocking relationships were not synced at all on rename.

**Fix**:
- Changed to `ParentId = parsed.LastLineIsMetadataOnly ? parsed.ParentId : ParentId` — only clear parent when metadata line explicitly changes
- Added `SyncBlockingRelationships()` in `RenameTask()` that diffs actual DB state (`GetBlocksIds()`) against desired state from new description

### 3. UI display format iteration

Iterated from count-only display → per-line with ID + title → dynamic truncation with `TextTrimming.CharacterEllipsis`.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Parent cardinality | Single parent (tree) | Simpler than DAG, sufficient for task management |
| Blocking enforcement | Visual only | Never block user actions — informational |
| Cross-list subtasks | Prevented | Same list constraint, enforced on set-parent |
| Cross-list blocking | Allowed | Blocking is informational, no list restriction |
| Cascade trash | Application-level | FK CASCADE is safety net for hard delete only |
| Uncheck cascade | No | Only undo reverses cascade-check |
| DB state vs description | Compare DB state | `AddBlocker()` doesn't update description text |
| `!` direction | "I block X" | Active/assertive: `!h67` = this task blocks h67 |

## Test Coverage

- **Parser tests**: 11 new tests (parent tokens, blocks tokens, combined metadata, display hiding, sync, rename metadata handling)
- **Data layer tests**: 40+ integration tests (CRUD, cascade, circular detection, rename sync, FK cascade, deep nesting)
- **Undo tests**: Serialization round-trip for new command types
- Total: 213 tests passing

## Related Documentation

- [Plan](../../plans/2026-02-06-feat-task-dependencies-subtasks-blocking-plan.md) — Full implementation spec
- [Task Metadata Inline System](./task-metadata-inline-system.md) — Parser foundation
- [JSON to SQLite Migration](../database-issues/json-to-sqlite-storage-migration.md) — Schema migration pattern
- [Undo Reorder](../undo-system/undo-support-for-reorder-operations.md) — Undo command pattern
- [Collapsible Lists Tray](./collapsible-lists-tray.md) — Tray VM/rendering pattern
