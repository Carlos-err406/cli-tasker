---
topic: User-specified sort order should override automatic sorting
date: 2026-02-06
status: decided
---

# User Sort Order Should Win Over Automatic Sorting

## What We're Building

Fix the reordering system so that user-specified `sort_order` (via drag-and-drop in tray or `tasker reorder` in CLI) is respected as the primary ordering, instead of being overridden by automatic sorting on priority/due-date.

Currently, `GetSortedTasks()` ignores `sort_order` entirely and re-sorts by status > priority > due-date > created-at. This means drag-reorder in the tray writes to DB correctly but the visual order snaps back immediately on the next `RefreshTasks()` call.

## Why This Approach

User intent should be respected. If someone explicitly drags a task to a position, that choice should persist. Automatic sorting by priority/due-date was well-intentioned but creates a confusing UX where drag-and-drop appears broken.

## Key Decisions

1. **User order wins entirely** — `sort_order` from the database is the source of truth for task display order. No automatic re-sorting by priority or due-date.

2. **Status grouping remains** — Unchecked tasks (in-progress, pending) display above done tasks. Within each status group, tasks are ordered by `sort_order DESC`.

3. **Applies everywhere** — Both tray app and CLI (`tasker list`) use the same ordering. `sort_order` is the single source of truth.

4. **`BumpSortOrder()` still applies** — When a task is added, renamed, moved, or has metadata changed, it gets bumped to the top. This is consistent with "last touched = most visible."

## What Changes

- `GetSortedTasks()` should group by status (active vs done) but within each group sort by `sort_order DESC` instead of priority/due-date/created-at.
- `SearchTasks()` should follow the same pattern.
- CLI `ListTodoTasks()` should use the same ordering.
- No schema changes needed — `sort_order` already exists and is correctly maintained.

## Open Questions

None — approach is decided.
