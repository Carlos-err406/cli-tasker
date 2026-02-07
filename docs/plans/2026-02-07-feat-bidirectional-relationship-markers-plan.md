---
title: Bidirectional Relationship Markers
type: feat
date: 2026-02-07
---

# feat: Bidirectional Relationship Markers

## Overview

When a task sets a relationship via inline metadata (`^abc` for parent, `!abc` for blocks), the referenced task should automatically get an inverse marker appended to its metadata line. Two new marker prefixes are introduced: `-^abc` ("has subtask abc") and `-!abc` ("blocked by abc"). These markers are actionable — typing them creates the inverse relationship on the referenced task.

The task description becomes the **source of truth** for relationship display across all three surfaces (CLI, TUI, Tray). A one-time migration backfills inverse markers on existing tasks.

## Problem Statement / Motivation

Currently, relationships are unidirectional in the description layer. Setting `^abc` on task `123` updates only `123`'s metadata line. Task `abc` (the parent) gets no corresponding marker — its description is unchanged. Users editing tasks inline have no visibility into what references point at a given task unless they inspect the relationship indicators rendered from DB queries.

This creates an asymmetry: you can see "I am a subtask of X" in the description, but not "X is my subtask." The fix is bidirectional markers that make both sides of every relationship visible and editable inline.

## Proposed Solution

### New Marker Syntax

| Marker | Meaning | Inverse of | Example |
|--------|---------|------------|---------|
| `-^abc` | "task abc is my subtask" | `^abc` ("I am subtask of abc") | Parent task gets `-^child` when child sets `^parent` |
| `-!abc` | "task abc blocks me" | `!abc` ("I block abc") | Blocked task gets `-!blocker` when blocker sets `!blocked` |

### Behavior Rules

1. **Bidirectional sync:** Setting `^abc` on task `123` → task `abc` gets `-^123`. Setting `!def` on task `123` → task `def` gets `-!123`.
2. **Actionable inverse markers:** Typing `-^abc` on task `999` → sets `abc.parent_id = 999` AND appends `^999` to abc's metadata line.
3. **Metadata scope:** Metadata like `p1 @today #tag` applies only to the current task, never propagated to referenced tasks.
4. **Auto-cleanup:** Removing a relationship removes the inverse marker from the other task.
5. **Markers persist:** Markers are NOT cleaned up when referenced tasks are trashed/deleted. They become dangling references but are preserved.
6. **Atomic undo:** Both sides of a bidirectional change are captured in a single `CompositeCommand` via `BeginBatch`/`EndBatch`.
7. **Description is source of truth:** Relationship display reads from parsed markers. DB validates on writes.
8. **One-time migration:** Existing tasks get inverse markers backfilled from DB relationships.

### Metadata Line Order

Markers are emitted in this order by `SyncMetadataToDescription()`:

```
^parent !blocks... -^subtasks... -!blockedBy... p1/p2/p3 @date #tags
```

## Technical Approach

### Phase 1: Parser Extension

Extend `TaskDescriptionParser` to recognize `-^abc` and `-!abc` markers.

#### `TaskDescriptionParser.cs`

**1a. Add new regex patterns:**

```csharp
// Match -^abc for inverse parent (has subtask)
[GeneratedRegex(@"(?:^|\s)-\^(\w{3})(?=\s|$)")]
private static partial Regex InverseParentRefRegex();

// Match -!abc for inverse blocker (blocked by)
[GeneratedRegex(@"(?:^|\s)-!(\w{3})(?=\s|$)")]
private static partial Regex InverseBlockerRefRegex();
```

**Regex safety:** `-^abc` will NOT be matched by the existing `ParentRefRegex` (`(?:^|\s)\^(\w{3})`) because the `-` preceding `^` is not whitespace or start-of-line. Similarly `-!abc` will not be matched by `BlocksRefRegex`. Verify with tests.

**1b. Extend `ParsedTask` record:**

```csharp
public record ParsedTask(
    string Description,
    Priority? Priority,
    DateOnly? DueDate,
    string[] Tags,
    bool LastLineIsMetadataOnly,
    string? ParentId = null,
    string[]? BlocksIds = null,
    string[]? HasSubtaskIds = null,    // from -^abc markers
    string[]? BlockedByIds = null);    // from -!abc markers
```

**1c. Update `Parse()` method:**

- Add `InverseParentRefRegex()` and `InverseBlockerRefRegex()` to the stripping logic (lines 33-37)
- Extract matches into `HasSubtaskIds` and `BlockedByIds` arrays
- Return them in the `ParsedTask` result

**1d. Update `GetDisplayDescription()` method:**

- Add `InverseParentRefRegex()` and `InverseBlockerRefRegex()` to the stripping logic in both single-line (lines 109-114) and multi-line (lines 121-126) paths

**1e. Update `SyncMetadataToDescription()` method:**

Add new parameters and emit inverse markers:

```csharp
public static string SyncMetadataToDescription(
    string description, Priority? priority, DateOnly? dueDate, string[]? tags,
    string? parentId = null, string[]? blocksIds = null,
    string[]? hasSubtaskIds = null, string[]? blockedByIds = null)
```

Emit order in the metadata line builder (after `!blocks...`, before `p1/p2/p3`):

```csharp
if (hasSubtaskIds is { Length: > 0 })
    metaParts.AddRange(hasSubtaskIds.Select(id => $"-^{id}"));
if (blockedByIds is { Length: > 0 })
    metaParts.AddRange(blockedByIds.Select(id => $"-!{id}"));
```

**Files:** `src/TaskerCore/Parsing/TaskDescriptionParser.cs`

**Tests:**
- `-^abc` parsed correctly, does not collide with `^abc`
- `-!abc` parsed correctly, does not collide with `!abc`
- Combined line: `^abc -^def !ghi -!jkl p1 @today #tag` parses all fields
- `GetDisplayDescription()` strips inverse markers
- `SyncMetadataToDescription()` emits inverse markers in correct order
- Round-trip: parse → sync → parse produces identical result

---

### Phase 2: Update All `SyncMetadataToDescription()` Call Sites

Every existing call site must pass inverse marker data through to avoid stripping them.

**Pattern:** Before calling `SyncMetadataToDescription()`, parse the current description to extract existing inverse markers and pass them through.

#### Call sites in `TodoTaskList.cs`:

| Line | Method | Current params | Add |
|------|--------|---------------|-----|
| 820-822 | `SetTaskDueDate()` | priority, dueDate, tags | + hasSubtaskIds, blockedByIds from parsed |
| 862-863 | `SetTaskPriority()` | priority, dueDate, tags | + hasSubtaskIds, blockedByIds from parsed |
| 1305-1306 | `SetParent()` | priority, dueDate, tags, parentId, blocksIds | + hasSubtaskIds, blockedByIds from parsed |
| 1335-1336 | `UnsetParent()` | priority, dueDate, tags, null, blocksIds | + hasSubtaskIds, blockedByIds from parsed |

**For each call site:** Parse the current description before calling sync, extract `HasSubtaskIds` and `BlockedByIds`, pass them through.

**Files:** `src/TaskerCore/Data/TodoTaskList.cs`

**Tests:**
- Setting due date on a task with `-^abc` preserves the inverse marker
- Setting priority on a task with `-!def` preserves the inverse marker

---

### Phase 3: Bidirectional Sync in Relationship Operations

When a relationship is created or removed, update the other task's description with the inverse marker.

#### 3a. `SetParent(taskId, parentId)`

After setting `parent_id` on the child and syncing the child's metadata:
1. Parse the parent's current description
2. Add `-^taskId` to the parent's `HasSubtaskIds`
3. Call `SyncMetadataToDescription()` on the parent's description
4. Update parent's description in DB

If the child previously had a different parent (task had `OldParentId`):
1. Parse the old parent's description
2. Remove `-^taskId` from the old parent's `HasSubtaskIds`
3. Sync and update old parent's description

Wrap in `BeginBatch`/`EndBatch` for atomic undo.

#### 3b. `UnsetParent(taskId)`

After clearing `parent_id` and syncing the child:
1. Parse the former parent's description
2. Remove `-^taskId` from the former parent's `HasSubtaskIds`
3. Sync and update former parent's description

#### 3c. `AddBlocker(blockerId, blockedId)`

After inserting into `task_dependencies`:
1. Parse the blocked task's description
2. Add `-!blockerId` to the blocked task's `BlockedByIds`
3. Sync and update blocked task's description

Also sync the blocker task's description to include `!blockedId` if not already present.

#### 3d. `RemoveBlocker(blockerId, blockedId)`

After deleting from `task_dependencies`:
1. Parse the blocked task's description
2. Remove `-!blockerId` from the blocked task's `BlockedByIds`
3. Sync and update blocked task's description

Also remove `!blockedId` from the blocker's metadata line.

**Files:** `src/TaskerCore/Data/TodoTaskList.cs`

**Tests:**
- `SetParent(child, parent)` → parent's description contains `-^child`
- `UnsetParent(child)` → former parent's description no longer contains `-^child`
- `SetParent(child, newParent)` when child had `oldParent` → oldParent loses `-^child`, newParent gains `-^child`
- `AddBlocker(blocker, blocked)` → blocked's description contains `-!blocker`
- `RemoveBlocker(blocker, blocked)` → blocked's description no longer contains `-!blocker`
- Undo of SetParent reverses both descriptions

---

### Phase 4: Bidirectional Sync in Add and Rename

#### 4a. `AddTodoTask()`

After inserting the task and processing `^parent`/`!blocks` refs:
1. If the task has a parent (`^abc`), update parent abc's description with `-^newId`
2. For each `!blockedId`, update the blocked task's description with `-!newId`
3. If the task has `-^abc` (inverse parent), set `abc.parent_id = newId` AND add `^newId` to abc's description
4. If the task has `-!abc` (inverse blocker), insert dependency (abc blocks newId) AND add `!newId` to abc's description

Validations for inverse markers:
- Self-reference check: `-^abc` on task `abc` → error
- Circular reference check: `-^abc` when abc is a descendant of current task → error
- Existence check: referenced task must exist
- Same-list constraint for `-^` (subtask must be in same list)

#### 4b. `RenameTask()`

After the existing rename flow:
1. Diff old vs new `HasSubtaskIds` (from `-^` markers):
   - For added `-^abc`: set `abc.parent_id = taskId`, add `^taskId` to abc's metadata
   - For removed `-^abc`: clear `abc.parent_id` (if it was `taskId`), remove `^taskId` from abc's metadata
2. Diff old vs new `BlockedByIds` (from `-!` markers):
   - For added `-!abc`: insert dependency (abc blocks taskId), add `!taskId` to abc's metadata
   - For removed `-!abc`: remove dependency, remove `!taskId` from abc's metadata
3. Also handle the forward markers bidirectionally:
   - If `^parent` changed: update old/new parent's `-^` markers
   - If `!blocks` changed (already handled by `SyncBlockingRelationships`): update blocked tasks' `-!` markers

**Files:** `src/TaskerCore/Data/TodoTaskList.cs`

**Tests:**
- Add task with `^abc` → parent abc gets `-^newId`
- Add task with `!def` → blocked def gets `-!newId`
- Add task with `-^abc` → abc gets `parent_id = newId` and `^newId` in description
- Add task with `-!abc` → dependency created, abc gets `!newId` in description
- Rename to add `-^abc` → abc becomes subtask
- Rename to remove `-^abc` → abc is no longer subtask
- Self-reference `-^abc` on task abc → warning
- Circular reference via inverse markers → warning

---

### Phase 5: Display from Markers (Source of Truth)

Replace the DB-query-based relationship rendering with marker-based rendering across all three surfaces.

#### 5a. CLI `ListCommand.cs` (lines 141-170)

Replace:
```csharp
// Current: queries DB for each relationship type
if (td.ParentId != null) { var parent = taskList.GetTodoTaskById(td.ParentId); ... }
var subtasks = taskList.GetSubtasks(td.Id);
var blocks = taskList.GetBlocks(td.Id);
var blockedBy = taskList.GetBlockedBy(td.Id);
```

With:
```csharp
// New: parse description for all relationship markers
var parsed = TaskDescriptionParser.Parse(td.Description);
if (parsed.ParentId != null) { var parent = taskList.GetTodoTaskById(parsed.ParentId); ... }
if (parsed.HasSubtaskIds != null) foreach (var subId in parsed.HasSubtaskIds) { ... }
if (parsed.BlocksIds != null) foreach (var bId in parsed.BlocksIds) { ... }
if (parsed.BlockedByIds != null) foreach (var bbId in parsed.BlockedByIds) { ... }
```

Note: We still call `GetTodoTaskById()` to fetch the title for display — but the **list of related IDs** comes from the description, not from DB queries like `GetSubtasks()`, `GetBlocks()`, `GetBlockedBy()`.

#### 5b. TUI `TuiRenderer.cs` (lines 187-223)

Same pattern as CLI. Replace `_taskList.GetSubtasks()`, `_taskList.GetBlocks()`, `_taskList.GetBlockedBy()` with parsed marker data. Keep `_taskList.GetTodoTaskById()` for title lookup.

#### 5c. Tray `TodoTaskViewModel.cs` (lines 244-288)

Update `LoadRelationships()` to read from parsed markers instead of DB queries.

#### 5d. CLI `GetCommand.cs`

The structured output (Parent, Subtasks, Blocks, Blocked by sections) should also read from markers. The raw description at the bottom naturally shows markers.

**Files:**
- `AppCommands/ListCommand.cs`
- `Tui/TuiRenderer.cs`
- `src/TaskerTray/ViewModels/TodoTaskViewModel.cs`
- `AppCommands/GetCommand.cs`

**Tests:**
- Display shows relationships from markers even if DB relationship doesn't exist (dangling marker)
- Display shows "?" for task title when referenced task not found

---

### Phase 6: One-Time Migration

Create a migration that reads all relationships from the DB and appends inverse markers to relevant task descriptions.

#### Migration logic:

```
For each task with parent_id:
  - Add -^{task.id} to the parent's metadata line (if not already present)

For each row in task_dependencies (blocker, blocked):
  - Add -!{blocker_id} to the blocked task's metadata line (if not already present)
  - Add !{blocked_id} to the blocker's metadata line (if not already present)
```

Use `SyncMetadataToDescription()` to ensure proper formatting. Run in a single SQLite transaction.

**Trigger:** Run automatically on app startup if a migration flag is not set. Store the flag in the `config` table (e.g., `inverse_markers_migrated = true`).

**Files:** `src/TaskerCore/Data/TodoTaskList.cs` (or a new `Migrations/` class)

**Tests:**
- Migration adds `-^child` to parent tasks
- Migration adds `-!blocker` to blocked tasks
- Migration adds `!blocked` to blocker tasks
- Migration is idempotent (running twice produces same result)
- Migration sets config flag
- Migration does not run if flag already set

---

## Acceptance Criteria

### Functional Requirements

- [x] `-^abc` and `-!abc` are parsed from metadata-only last lines
- [x] `SyncMetadataToDescription()` emits inverse markers in correct order
- [x] `GetDisplayDescription()` strips inverse markers from display
- [x] `SetParent()` / `UnsetParent()` syncs inverse markers on the parent task
- [x] `AddBlocker()` / `RemoveBlocker()` syncs inverse markers on the blocked task
- [x] `AddTodoTask()` with `^parent` updates parent's description with `-^newId`
- [x] `AddTodoTask()` with `!blocked` updates blocked task's description with `-!newId`
- [x] `AddTodoTask()` with `-^abc` creates inverse relationship (abc becomes subtask)
- [x] `AddTodoTask()` with `-!abc` creates inverse relationship (abc blocks current task)
- [x] `RenameTask()` diffs and syncs inverse markers bidirectionally
- [x] All existing `SyncMetadataToDescription()` call sites preserve inverse markers
- [x] Undo/redo of bidirectional changes works atomically
- [x] Self-reference and circular reference checks work for inverse markers
- [x] Display on all three surfaces reads relationships from parsed markers
- [x] One-time migration backfills inverse markers on existing tasks
- [x] Markers persist when referenced tasks are trashed/deleted

### Quality Gates

- [x] All existing `TaskDescriptionParserTests` still pass
- [x] All existing `TaskDependencyTests` still pass
- [x] All existing `UndoDependencyTests` still pass
- [x] New tests cover all 14 user flows identified in SpecFlow analysis
- [x] Regex non-collision verified: `-^abc` does not match `^abc` pattern and vice versa

## Dependencies & Risks

**Risk: Silent marker stripping.** If any `SyncMetadataToDescription()` call site misses the new parameters, inverse markers will be silently dropped. **Mitigation:** Phase 2 explicitly lists all call sites. Tests verify markers survive unrelated metadata changes.

**Risk: Undo recursion.** If `SetParent()` called during undo triggers inverse marker sync, which itself tries to record undo, infinite recursion occurs. **Mitigation:** All relationship methods already have `recordUndo: false` parameter used by undo commands. Inverse marker sync must respect this flag.

**Risk: Migration on large datasets.** The one-time migration modifies many task descriptions in a transaction. **Mitigation:** SQLite handles this well for typical task counts (< 10,000). Wrap in a single transaction for atomicity.

## References & Research

### Internal References

- Parser: `src/TaskerCore/Parsing/TaskDescriptionParser.cs`
- Data layer: `src/TaskerCore/Data/TodoTaskList.cs`
- Model: `src/TaskerCore/Models/TodoTask.cs`
- Undo system: `src/TaskerCore/Undo/UndoManager.cs`, `src/TaskerCore/Undo/Commands/CompositeCommand.cs`
- CLI display: `AppCommands/ListCommand.cs:141-170`, `AppCommands/GetCommand.cs`
- TUI display: `Tui/TuiRenderer.cs:187-223`
- Tray display: `src/TaskerTray/ViewModels/TodoTaskViewModel.cs:244-288`
- Existing tests: `tests/TaskerCore.Tests/Parsing/TaskDescriptionParserTests.cs`, `tests/TaskerCore.Tests/Data/TaskDependencyTests.cs`
- Brainstorm: `docs/brainstorms/2026-02-07-bidirectional-relationship-markers-brainstorm.md`

### Institutional Learnings

- **Rename sync bug (critical):** Use `parsed.LastLineIsMetadataOnly ? parsed.ParentId : ParentId` to avoid accidentally preserving stale parents
- **Cascade effect of regex failure:** If one regex pattern fails to match its token, the entire metadata line is not recognized as metadata-only, causing ALL metadata to be unparsed
- **Tag hyphen bug:** Regex character classes must include hyphens explicitly (`[\w-]+`)
- **Test isolation:** All tests must use isolated temp directories via `TaskerServices.CreateInMemory()` or `TaskerServices(testDir)`
- **Undo batching:** Cascade operations use `BeginBatch()`/`EndBatch()` for atomic undo grouping
