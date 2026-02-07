---
title: "feat: Add bidirectional 'related' relationship type"
type: feat
date: 2026-02-07
brainstorm: docs/brainstorms/2026-02-07-related-relationship-type-brainstorm.md
---

# feat: Add bidirectional "related" relationship type

## Overview

Add a new symmetric "related to" relationship between tasks. Unlike blocking (directional) or subtasks (hierarchical), "related" is bidirectional — if A is related to B, then B is automatically related to A. Set via `~abc` inline metadata prefix. Displayed with `~` icon in cyan/blue across all three surfaces.

## Problem Statement / Motivation

The existing relationship types (blocking and subtask) imply direction or hierarchy. There's no way to express "these tasks are connected" without implying one depends on the other. A "related" type fills this gap for cross-referencing tasks that share context, are alternatives, or are loosely coupled.

## Proposed Solution

Follow the exact patterns established by the blocking relationship implementation — new junction table, parser regex, data layer methods, undo commands, CLI subcommands, and three-surface display rendering. The key difference is bidirectionality: setting `~abc` on task X automatically adds `~X` to task abc's metadata.

## Technical Approach

### Phase 1: Schema & Data Layer

#### 1.1 New table in `TaskerDb.cs` (line 62)

Add `task_relations` to `EnsureCreated()` after `task_dependencies`:

```sql
CREATE TABLE IF NOT EXISTS task_relations (
    task_id_1 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    task_id_2 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id_1, task_id_2),
    CHECK (task_id_1 < task_id_2),
    CHECK (task_id_1 != task_id_2)
);
```

The `CHECK (task_id_1 < task_id_2)` enforces canonical ordering — the lexicographically smaller ID is always in column 1. This prevents duplicate pairs and simplifies queries.

Add idempotent migration `MigrateAddRelations()` following the `MigrateAddDependencies()` pattern (line 203). Check if table exists, create if not, clear undo history.

#### 1.2 Data layer methods in `TodoTaskList.cs`

Add these methods following the blocker pattern (lines 1346-1475):

```
AddRelated(taskId1, taskId2, recordUndo)    → Insert canonical pair, sync both descriptions
RemoveRelated(taskId1, taskId2, recordUndo) → Delete canonical pair, sync both descriptions
GetRelated(taskId)                          → Return List<TodoTask> (union query both columns)
GetRelatedIds(taskId)                       → Return List<string> (IDs only, for sync)
```

**AddRelated validation:**
- Self-reference check (`taskId1 == taskId2`)
- Both tasks exist
- Duplicate check (already related)
- No circular detection needed (symmetric)
- Cross-list allowed

**SQL for GetRelatedIds:**
```sql
SELECT task_id_2 FROM task_relations WHERE task_id_1 = @id
UNION
SELECT task_id_1 FROM task_relations WHERE task_id_2 = @id
```

#### 1.3 Bidirectional sync on AddRelated

When `AddRelated(X, Y)` is called, both task X and task Y need their metadata descriptions updated to include the `~` reference to the other. Call `SyncMetadataToDescription()` on both tasks after inserting the row.

#### 1.4 Sync during AddTodoTask (line 278)

After the blocking references block (line 278-302), add a similar block for related references:

```csharp
if (parsed.RelatedIds is { Length: > 0 })
{
    foreach (var relatedId in parsed.RelatedIds)
    {
        // validate, insert canonical pair, sync target description
    }
}
```

#### 1.5 Sync during RenameTask (line 683)

After `SyncBlockingRelationships()` (line 688), add `SyncRelatedRelationships()`:

```csharp
var currentRelatedIds = GetRelatedIds(taskId).ToArray();
SyncRelatedRelationships(taskId, currentRelatedIds, newParsed.RelatedIds);
```

The `SyncRelatedRelationships` method computes the diff (like `SyncBlockingRelationships` at line 699) and for each added/removed relation, also updates the *other* task's description metadata.

### Phase 2: Parser

#### 2.1 New regex in `TaskDescriptionParser.cs` (line 226)

```csharp
// Match ~abc for related reference
[GeneratedRegex(@"(?:^|\s)~(\w{3})(?=\s|$)")]
private static partial Regex RelatedRefRegex();
```

#### 2.2 Add to ParsedTask record (line 14)

```csharp
public record ParsedTask(
    string Description,
    Priority? Priority,
    DateOnly? DueDate,
    string[] Tags,
    bool LastLineIsMetadataOnly,
    string? ParentId = null,
    string[]? BlocksIds = null,
    string[]? RelatedIds = null);  // NEW
```

#### 2.3 Add to metadata-only check (5 locations)

Add `strippedLine = RelatedRefRegex().Replace(strippedLine, " ");` in:
1. `Parse()` — line 37 (after BlocksRefRegex strip)
2. `GetDisplayDescription()` single-line — line 114
3. `GetDisplayDescription()` multi-line — line 126
4. `SyncMetadataToDescription()` — line 152

#### 2.4 Extract matches in Parse() (after line 90)

```csharp
var relatedMatches = RelatedRefRegex().Matches(lastLine);
foreach (Match match in relatedMatches)
    relatedIds.Add(match.Groups[1].Value);
```

#### 2.5 Add to SyncMetadataToDescription (line 140)

Add `relatedIds` parameter. Serialize after `!blocksIds` and before priority:

```csharp
if (relatedIds is { Length: > 0 })
    metaParts.AddRange(relatedIds.Select(id => $"~{id}"));
```

**Metadata line ordering:** `^parent !blocks ~related p1 @date #tags`

### Phase 3: Undo Commands

#### 3.1 AddRelatedCommand.cs

Following `AddBlockerCommand.cs` pattern exactly:

```csharp
public record AddRelatedCommand : IUndoableCommand
{
    public required string TaskId1 { get; init; }
    public required string TaskId2 { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;
    public string Description => $"Add related: {TaskId1} ↔ {TaskId2}";

    public void Execute() => new TodoTaskList().AddRelated(TaskId1, TaskId2, recordUndo: false);
    public void Undo() => new TodoTaskList().RemoveRelated(TaskId1, TaskId2, recordUndo: false);
}
```

#### 3.2 RemoveRelatedCommand.cs

Inverse of above.

#### 3.3 Register in IUndoableCommand.cs (line 21)

```csharp
[JsonDerivedType(typeof(AddRelatedCommand), "add-related")]
[JsonDerivedType(typeof(RemoveRelatedCommand), "remove-related")]
```

### Phase 4: CLI Commands

#### 4.1 DepsCommand.cs — add two subcommands

Update description (line 10): `"Manage task dependencies (subtasks, blocking, and related)"`

Add `CreateAddRelatedCommand()` and `CreateRemoveRelatedCommand()` following the blocker pattern (lines 69-119):

```
tasker deps add-related <taskId1> <taskId2>
tasker deps remove-related <taskId1> <taskId2>
```

Arguments are symmetric — order doesn't matter (stored canonically).

### Phase 5: Display (Three-Surface Consistency)

#### 5.1 CLI List — `ListCommand.cs` (after line 170)

```csharp
var related = taskList.GetRelated(td.Id);
foreach (var r in related)
{
    var rTitle = Markup.Escape(StringHelpers.Truncate(
        TaskDescriptionParser.GetDisplayDescription(r.Description).Split('\n')[0], 40));
    Output.Markup($"{indent}[cyan dim]~ Related to ({r.Id}) {rTitle}[/]");
}
```

#### 5.2 CLI Get — `GetCommand.cs`

Human-readable (after line 148):
```csharp
var related = taskList.GetRelated(task.Id);
if (related.Count > 0)
{
    Output.Markup($"[bold]Related:[/]");
    foreach (var r in related)
        Output.Markup($"               [dim]({r.Id}) {Markup.Escape(StringHelpers.Truncate(r.Description, 40))}[/]");
}
```

JSON output (after line 91):
```csharp
related = related.Select(r => new { id = r.Id, description = StringHelpers.Truncate(r.Description, 50) }).ToArray()
```

#### 5.3 TUI — `TuiRenderer.cs`

RenderTask (after line 223):
```csharp
var related = _taskList.GetRelated(task.Id);
foreach (var r in related)
{
    if (linesRendered >= maxLines) break;
    var rTitle = Markup.Escape(StringHelpers.Truncate(
        TaskDescriptionParser.GetDisplayDescription(r.Description).Split('\n')[0], 40));
    WriteLineCleared($"{indent}[cyan]~ Related to ({r.Id}) {rTitle}[/]");
    linesRendered++;
}
```

CountTaskLines (after line 240):
```csharp
count += _taskList.GetRelated(task.Id).Count;
```

#### 5.4 TaskerTray ViewModel — `TodoTaskViewModel.cs`

Add properties (after line 44):
```csharp
public string[]? RelatedDisplay { get; private set; }
public bool HasRelated => RelatedDisplay is { Length: > 0 };
```

Update `HasRelationships` (line 45):
```csharp
public bool HasRelationships => HasParent || HasSubtasks || HasBlocks || HasBlockedBy || HasRelated;
```

LoadRelationships (after line 287):
```csharp
var related = taskList.GetRelated(_task.Id);
if (related.Count > 0)
{
    RelatedDisplay = related.Select(r =>
    {
        var title = TaskDescriptionParser.GetDisplayDescription(r.Description).Split('\n')[0];
        return $"Related to ({r.Id}) {title}";
    }).ToArray();
}
```

#### 5.5 TaskerTray View — `TaskListPopup.axaml.cs` (after line 1295)

```csharp
if (task.HasRelated)
{
    foreach (var line in task.RelatedDisplay!)
        AddRelationshipLabel(contentPanel, $"~ {line}", "#5FB3B3");
}
```

Color `#5FB3B3` is a muted cyan that fits the existing palette (gray for subtasks, amber for blocking).

## Acceptance Criteria

### Functional Requirements

- [x] `~abc` in metadata line creates bidirectional related relationship
- [x] Setting `~abc` on task X automatically adds `~X` to task abc's description
- [x] Removing `~abc` from task X automatically removes `~X` from task abc's description
- [x] `tasker deps add-related <id1> <id2>` creates relationship and syncs both descriptions
- [x] `tasker deps remove-related <id1> <id2>` removes relationship and syncs both descriptions
- [x] A task cannot be related to itself
- [x] Duplicate relationships are silently ignored (no error, no-change result)
- [x] Related tasks display with `~` icon in cyan across CLI list, CLI get, TUI, and TaskerTray
- [x] `tasker get <id> --json` includes `related` array
- [x] Cross-list relationships work
- [x] Deleting a task removes its related rows via CASCADE
- [x] Trashing a task: related links are preserved in DB, display hides trashed tasks
- [x] Undo/redo works for both add-related and remove-related
- [x] Schema migration is idempotent for existing databases

### Edge Cases

- [x] Creating task with `~abc` where abc doesn't exist: warning, skip relationship
- [x] Creating task with `~abc` where abc is the task itself: warning, skip
- [x] Both tasks already have `~` refs to each other: single DB row, no duplicate
- [x] Editing task to add `~abc` when abc already has `~X`: idempotent, no error
- [x] Editing task to remove all `~` refs: removes from DB and other task descriptions

## Dependencies & Risks

- **SyncMetadataToDescription signature change** — Adding `relatedIds` parameter affects all existing callers. Need to add it as an optional parameter with default `null` to avoid breaking changes.
- **Bidirectional description sync** — When syncing task A's metadata, updating task B's description must not trigger a recursive sync loop. The `recordUndo: false` + direct DB update pattern (same as blocking sync) avoids this.
- **Undo atomicity** — AddRelated affects two task descriptions + one DB row. Use `BeginBatch()`/`EndBatch()` for composite undo when called from commands, but not from sync (which is already inside a rename undo).

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerCore/Data/TaskerDb.cs` | New table + migration |
| `src/TaskerCore/Parsing/TaskDescriptionParser.cs` | New regex, ParsedTask field, 5 strip locations, extract, serialize |
| `src/TaskerCore/Data/TodoTaskList.cs` | AddRelated, RemoveRelated, GetRelated, GetRelatedIds, SyncRelatedRelationships, AddTodoTask hook, RenameTask hook |
| `src/TaskerCore/Undo/Commands/AddRelatedCommand.cs` | New file |
| `src/TaskerCore/Undo/Commands/RemoveRelatedCommand.cs` | New file |
| `src/TaskerCore/Undo/IUndoableCommand.cs` | Register two new types |
| `AppCommands/DepsCommand.cs` | add-related + remove-related subcommands |
| `AppCommands/ListCommand.cs` | Related display lines |
| `AppCommands/GetCommand.cs` | Related in human-readable + JSON output |
| `Tui/TuiRenderer.cs` | RenderTask + CountTaskLines |
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | RelatedDisplay, HasRelated, LoadRelationships |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Render related lines |

## New Files

| File | Purpose |
|------|---------|
| `src/TaskerCore/Undo/Commands/AddRelatedCommand.cs` | Undo command for adding relation |
| `src/TaskerCore/Undo/Commands/RemoveRelatedCommand.cs` | Undo command for removing relation |

## Tests to Write

All tests use `[Collection("IsolatedTests")]` with temp directories per institutional learnings.

### Parser tests (`TaskDescriptionParserTests.cs`)
- [x] Parse `~abc` from metadata line → `RelatedIds = ["abc"]`
- [x] Parse multiple `~abc ~def` → `RelatedIds = ["abc", "def"]`
- [x] Parse mixed metadata `~abc !h67 p1 #tag` → all fields populated
- [x] `GetDisplayDescription` hides `~abc` from multi-line descriptions
- [x] `SyncMetadataToDescription` serializes related IDs in correct position

### Data layer tests (`TaskDependencyTests.cs`)
- [x] AddRelated creates row with canonical ordering
- [x] AddRelated self-reference returns error
- [x] AddRelated duplicate returns no-change
- [x] AddRelated with nonexistent task returns error
- [x] RemoveRelated deletes the row
- [x] RemoveRelated for non-existing relation returns no-change
- [x] GetRelated returns tasks from both sides
- [x] GetRelatedIds returns IDs from both sides
- [x] Adding task with `~abc` inline creates relationship
- [x] Renaming task to add `~abc` creates relationship
- [x] Renaming task to remove `~abc` removes relationship
- [x] Bidirectional sync: adding `~abc` to X updates abc's description
- [x] Bidirectional sync: removing `~abc` from X updates abc's description

### Undo tests (`UndoDependencyTests.cs`)
- [x] AddRelated undo removes the relation
- [x] RemoveRelated undo restores the relation
- [x] Undo serialization round-trip for AddRelatedCommand
- [x] Undo serialization round-trip for RemoveRelatedCommand

## References & Research

### Internal References
- Blocking implementation pattern: `TodoTaskList.cs:1346-1475`
- Parser metadata parsing: `TaskDescriptionParser.cs:1-228`
- Undo command pattern: `AddBlockerCommand.cs:1-24`
- Schema and migrations: `TaskerDb.cs:62-229`
- Display patterns: `ListCommand.cs:141-171`, `GetCommand.cs:118-148`, `TuiRenderer.cs:187-241`
- TaskerTray display: `TodoTaskViewModel.cs:37-288`, `TaskListPopup.axaml.cs:1273-1295`

### Brainstorm
- `docs/brainstorms/2026-02-07-related-relationship-type-brainstorm.md`

### Institutional Learnings
- Test isolation: `docs/solutions/testing/test-isolation-prevention-strategies.md`
- Task dependencies: `docs/solutions/feature-implementations/task-dependencies-subtasks-blocking.md`
- Metadata parsing: `docs/solutions/feature-implementations/task-metadata-inline-system.md`
- Sort consistency: `docs/solutions/logic-errors/inconsistent-task-sort-order-across-consumers.md`
