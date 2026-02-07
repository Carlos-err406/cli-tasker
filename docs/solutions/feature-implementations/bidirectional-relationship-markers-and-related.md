---
title: Bidirectional Relationship Markers (-^, -!, ~) and Related Relationships
category: feature-implementations
tags:
  - parsing
  - metadata
  - regex
  - merge-conflict
  - related-markers
  - bidirectional-markers
  - inverse-markers
  - relationships
module: TaskerCore
symptoms:
  - related markers (~abc) in task descriptions not recognized during parsing
  - metadata-only line detection failed when unrecognized markers present
  - tags not extracted from descriptions containing ~abc markers
  - no way to see which tasks are subtasks of a parent from the parent's view
  - no way to see which tasks block a given task from the blocked task's view
date_solved: 2026-02-07
files_changed:
  - src/TaskerCore/Parsing/TaskDescriptionParser.cs
  - src/TaskerCore/Data/TodoTaskList.cs
  - src/TaskerCore/Data/TaskerDb.cs
  - src/TaskerCore/Data/InverseMarkerMigrator.cs
  - src/TaskerCore/Undo/Commands/AddRelatedCommand.cs
  - src/TaskerCore/Undo/Commands/RemoveRelatedCommand.cs
  - src/TaskerCore/Undo/IUndoableCommand.cs
  - AppCommands/GetCommand.cs
  - AppCommands/ListCommand.cs
  - AppCommands/DepsCommand.cs
  - Tui/TuiRenderer.cs
  - src/TaskerTray/ViewModels/TodoTaskViewModel.cs
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
  - tests/TaskerCore.Tests/Parsing/TaskDescriptionParserTests.cs
  - tests/TaskerCore.Tests/Data/TaskDependencyTests.cs
  - tests/TaskerCore.Tests/Undo/UndoDependencyTests.cs
---

# Bidirectional Relationship Markers (-^, -!, ~) and Related Relationships

## Overview

Two features were developed on separate branches and merged together:

1. **Inverse markers** (`-^abc` = has subtask, `-!abc` = blocked by) on `feat/bidirectional-relationship-markers`
2. **Related relationships** (`~abc` = related to) on `feat/related-relationship-type`

Both extend the inline metadata parser and required careful integration to avoid silently breaking metadata detection.

## Problem

Task `be9` had description:
```
try out LM studio
seems like it works best on macOS according to Reikel

#ai #local-llms #poc ~248
```

The `~248` related marker wasn't recognized by the parser because the related feature branch hadn't been merged. This caused the metadata-only line detection to fail: after stripping known patterns from `#ai #local-llms #poc ~248`, the `~248` remained, so the line was classified as "not metadata-only" and ALL metadata on that line was silently ignored.

## Root Cause

The parser's `IsMetadataOnlyLine` check strips all known regex patterns from the last line. If ANY marker type is missing from the stripping logic, the leftover text makes the line appear to contain non-metadata content, and the entire line's metadata is silently dropped.

```csharp
// All patterns must be stripped for metadata-only detection to work
var strippedLine = lastLine;
strippedLine = PriorityRegex().Replace(strippedLine, " ");
strippedLine = DueDateRegex().Replace(strippedLine, " ");
strippedLine = TagRegex().Replace(strippedLine, " ");
strippedLine = ParentRefRegex().Replace(strippedLine, " ");
strippedLine = BlocksRefRegex().Replace(strippedLine, " ");
strippedLine = InverseParentRefRegex().Replace(strippedLine, " ");
strippedLine = InverseBlockerRefRegex().Replace(strippedLine, " ");
strippedLine = RelatedRefRegex().Replace(strippedLine, " ");  // Was missing
var isMetadataOnly = string.IsNullOrWhiteSpace(strippedLine);
```

## Solution

### 1. Parser — New Marker Types

Three new `[GeneratedRegex]` patterns added to `TaskDescriptionParser`:

| Marker | Regex | Meaning |
|--------|-------|---------|
| `-^abc` | `(?:^|\s)-\^(\w{3})(?=\s|$)` | "Has subtask abc" (inverse of `^parent`) |
| `-!abc` | `(?:^|\s)-!(\w{3})(?=\s|$)` | "Blocked by abc" (inverse of `!blocks`) |
| `~abc` | `(?:^|\s)~(\w{3})(?=\s|$)` | "Related to abc" |

All three added to `ParsedTask` record, stripping logic, `Parse()` extraction, `GetDisplayDescription()`, and `SyncMetadataToDescription()`.

### 2. Metadata Line Emission Order

```
^parent !blocks... -^subtasks... -!blockedBy... ~related... p1/p2/p3 @date #tags
```

### 3. SyncMetadataToDescription — All Call Sites

The `SyncMetadataToDescription` method gained a `relatedIds` parameter. **Every call site** (15 total in `TodoTaskList.cs` + 1 in `InverseMarkerMigrator.cs`) must pass `parsed.RelatedIds` to avoid silently stripping `~abc` markers during metadata rebuild.

This is the most common source of bugs: adding a new parameter to `SyncMetadataToDescription` but missing some call sites. The compiler won't catch it because the parameter is optional with a `null` default.

### 4. Schema — task_relations Table

```sql
CREATE TABLE IF NOT EXISTS task_relations (
    task_id_1 TEXT NOT NULL,
    task_id_2 TEXT NOT NULL,
    PRIMARY KEY (task_id_1, task_id_2),
    FOREIGN KEY (task_id_1) REFERENCES tasks(id),
    FOREIGN KEY (task_id_2) REFERENCES tasks(id)
);
```

Canonical ordering: `task_id_1 < task_id_2` (string comparison) prevents duplicates.

### 5. InverseMarkerMigrator — One-Time Migration

`InverseMarkerMigrator.MigrateIfNeeded()` runs at startup (after `JsonMigrator`). Reads all tasks with `parent_id` and `task_dependencies`, then backfills `-^childId` on parents and `-!blockerId` on blocked tasks. Guarded by `config` table flag `inverse_markers_migrated`.

### 6. Display — Three-Surface Consistency

All three surfaces (CLI, TUI, Tray) read relationship IDs from **parsed description markers**, not from DB queries. This ensures the description is the source of truth for display.

## Merge Conflict Resolution

6 files had conflicts when merging `feat/related-relationship-type` into `feat/bidirectional-relationship-markers`:

| File | Strategy |
|------|----------|
| `TaskDescriptionParser.cs` | Combined all marker types in ParsedTask, stripping, extraction, sync |
| `TodoTaskList.cs` | Kept inverse marker sync logic + added `SyncRelatedRelationships` call |
| `GetCommand.cs` | Both JSON and human-readable output use `parsed.RelatedIds` |
| `TuiRenderer.cs` | `CountTaskLines` includes related count, render uses `parsed.RelatedIds` |
| `TaskDependencyTests.cs` | Included both inverse marker and related relationship test sets |
| `TaskDescriptionParserTests.cs` | Included both sets; updated round-trip test to include `~xyz` |

## Prevention Checklist — Adding New Marker Types

When adding a new marker type to the parser:

1. Add `[GeneratedRegex]` pattern to `TaskDescriptionParser`
2. Add field to `ParsedTask` record
3. Add regex to ALL THREE stripping locations (metadata-only detection, `GetDisplayDescription`, `SyncMetadataToDescription`)
4. Add extraction logic in `Parse()`
5. Add emission logic in `SyncMetadataToDescription()`
6. Update ALL `SyncMetadataToDescription` call sites to pass the new parameter
7. Write parser tests: extraction, display hiding, sync round-trip, metadata-only detection
8. Write integration tests in `TaskDependencyTests`

**Critical**: Step 6 is the most error-prone. Search for `SyncMetadataToDescription(` across the entire codebase to find all call sites.

## Test Coverage

- 288 total tests (31 new from related relationships)
- Parser tests: extraction of all marker types, display hiding, sync round-trip with all markers
- Dependency tests: inverse marker preservation on metadata changes, bidirectional sync on add/remove/rename
- Migrator tests: 11 tests covering parent-child, blocker, idempotency, metadata preservation

## Cross-References

- [Task Dependencies — Subtasks and Blocking](./task-dependencies-subtasks-blocking.md) — Foundation pattern
- [Inline Metadata System](./task-metadata-inline-system.md) — Core parsing architecture
- [Inline Metadata Reference](../../reference/inline-metadata.md) — Marker syntax reference
- [Brainstorm: Related Relationship Type](../../brainstorms/2026-02-07-related-relationship-type-brainstorm.md)
- [Brainstorm: Bidirectional Markers](../../brainstorms/2026-02-07-bidirectional-relationship-markers-brainstorm.md)
- [Plan: Related Relationship](../../plans/2026-02-07-feat-bidirectional-related-relationship-plan.md)
- [Plan: Bidirectional Markers](../../plans/2026-02-07-feat-bidirectional-relationship-markers-plan.md)
