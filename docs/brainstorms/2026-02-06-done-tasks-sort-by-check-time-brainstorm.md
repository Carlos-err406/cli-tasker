---
title: "Done tasks sort by check time"
date: 2026-02-06
category: ux
tags: [sort-order, done-tasks, updated-at, checked-at, schema]
module: [TaskerCore, TUI, TaskerTray, CLI]
---

# Done Tasks Sort by Check Time

## What We're Building

Done tasks should appear in the order they were checked off — most recently checked on top. Currently they sort by priority → due date → created_at, which is wrong for the done group (a task created a week ago that you just checked appears below a task created today that was checked earlier).

Additionally, add `updated_at` and `checked_at` timestamp fields to the task schema for richer metadata.

## Why This Approach

**Approach A: Add `updated_at` + `checked_at` columns** was chosen over:
- **Sort order bump on check** (Approach B) — conflates `sort_order` with check time, contradicts the stable-sort-on-status-change refactor we just shipped
- **`checked_at` only** (Approach C) — user explicitly wants `updated_at` too for future use

## Key Decisions

1. **Done tasks sort purely by `checked_at DESC`** — no priority or due date grouping within the done group. Most recently checked = top.

2. **Active tasks (pending/in-progress) keep current sort** — priority → due date → created_at. No change.

3. **`checked_at` is cleared on uncheck** — only set when status = Done, NULL otherwise. If a task is unchecked and re-checked, `checked_at` gets a fresh timestamp.

4. **`updated_at` tracks any mutation** — rename, move, priority change, due date change, tag change, status change. Set to `DateTime.UtcNow` on every write operation.

5. **Migration defaults** — existing tasks get `updated_at = created_at` and `checked_at = NULL`. Already-checked tasks get `checked_at = created_at` as a reasonable approximation (we don't know when they were actually checked).

## Schema Changes

```sql
ALTER TABLE tasks ADD COLUMN updated_at TEXT;
ALTER TABLE tasks ADD COLUMN checked_at TEXT;

-- Backfill
UPDATE tasks SET updated_at = created_at;
UPDATE tasks SET checked_at = created_at WHERE status = 2;
```

## Sorting Change

```
-- Current (all status groups):
ORDER BY StatusSortOrder, Priority, DueDate, CreatedAt DESC

-- New:
-- Active tasks (status 0, 1): Priority → DueDate → CreatedAt DESC (unchanged)
-- Done tasks (status 2): CheckedAt DESC (only criterion)
```

## Scope

- `TodoTask` record: add `UpdatedAt` and `CheckedAt` fields
- `TaskerDb` schema: add columns + migration
- `TodoTaskList`: set `updated_at` on every mutation, set/clear `checked_at` on status change
- `GetSortedTasks()`: change done-group sort to `checked_at DESC`
- `TuiApp.LoadTasks()`: update TUI sorting to match
- Undo commands: capture and restore both timestamps
- `tasker get`: display `updated_at` and `checked_at` in task detail view

## Resolved Questions

- **`tasker list` display:** No change. `checked_at` is shown only in `tasker get` detail view.
- **Tray popup:** Show relative check time (e.g., "2h ago") as a dim label next to done tasks.
