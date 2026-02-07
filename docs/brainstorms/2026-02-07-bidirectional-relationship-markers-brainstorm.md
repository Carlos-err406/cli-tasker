# Bidirectional Relationship Markers

**Date:** 2026-02-07
**Task:** 6de — update parent task text and metadata
**Status:** Ready for planning

## What We're Building

When a task sets a relationship via inline metadata (`^abc` for parent, `!abc` for blocks), the referenced task should automatically get an inverse marker appended to its metadata line. This makes relationships visible from both sides when editing task descriptions.

**New markers:**
- `-^abc` — "task abc is my subtask" (inverse of `^abc` which means "abc is my parent")
- `-!abc` — "task abc is blocked by me" (inverse of `!abc` which means "I block abc")

**Example flow:**
1. Task `123` is created with description: `fix the bug\n^abc p1 @today`
2. Task `123` gets `parent_id = abc`, priority High, due today
3. Task `abc`'s metadata line is updated to include `-^123`
4. If the `^abc` relationship is later removed from `123`, the `-^123` marker is removed from `abc`

## Why This Approach

**Parser-level inverse markers** were chosen over display-only indicators because:
- The task explicitly asks to "update the text" on referenced tasks
- Inline visibility when editing descriptions directly (not just in display)
- Consistent with the existing metadata parsing model — everything round-trips through `TaskDescriptionParser`
- `SyncMetadataToDescription()` already handles rebuilding metadata lines

## Key Decisions

1. **Syntax:** `-^abc` for inverse parent (has subtask), `-!abc` for inverse blocker (blocked by). The `-` prefix consistently means "inverse of".
2. **Metadata scope:** When writing `^abc p1 @today`, only the current task gets `p1 @today`. The parent only gets the `-^abc` relationship marker.
3. **Bidirectional for both:** Applies to both `^` (parent/child) and `!` (blocks/blocked-by) relationships.
4. **Auto-cleanup:** Removing a relationship on one side automatically removes the inverse marker from the other task.
5. **Inverse markers are actionable:** Typing `-^abc` on task 999 creates the inverse relationship — it sets `abc`'s parent to 999 (equivalent to adding `^999` on task `abc`). Similarly, `-!abc` on task 999 means "abc blocks me" and creates the forward `!999` on task `abc`. Full bidirectional editing from either side.
6. **No metadata line length limit:** Let the line grow freely. It's already hidden in display via `GetDisplayDescription()`. Users editing inline will see it, which is fine.
7. **Atomic undo:** Adding/removing a relationship captures both sides (the relationship change AND the inverse marker update) as a single undo operation.

## Display Simplification

With inverse markers stored in descriptions, relationship display can be simplified:

- **Current:** Display code makes extra DB queries to find parent, children, blockers, and blocked-by tasks for each task.
- **New:** Display reads relationship info directly from parsed metadata markers (`^abc` = subtask of abc, `-^def` = has subtask def, `!ghi` = blocks ghi, `-!jkl` = blocked by jkl).
- **Source of truth:** Hybrid — display reads from markers for speed, DB validates/syncs on write operations to ensure integrity.
- This removes the need for separate "get relationships" queries in the display path (CLI list, TUI render, Tray popup).

## Affected Code Paths

- `TaskDescriptionParser` — add `-^` and `-!` regex patterns to `Parse()` and `SyncMetadataToDescription()`
- `TodoTaskList.AddTodoTask()` — after processing `^parent` and `!blocks`, sync the referenced task's metadata
- `TodoTaskList.RenameTask()` — sync inverse markers when relationships change via rename
- `TodoTaskList.SetParent()` / `UnsetParent()` — sync parent's metadata line
- `TodoTaskList.AddBlocker()` / `RemoveBlocker()` — sync blocked task's metadata line
- Inline metadata docs — document new `-^` and `-!` prefixes
