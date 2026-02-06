# Stable Sort on Status Change

**Date:** 2026-02-06
**Status:** Decided

## Problem

When a task's status changes (e.g., checking it Done or setting In Progress), the task teleports to a different position in the list because:
1. `GetSortedTasks()` groups by status (InProgress first, Pending, Done last)
2. `BumpSortOrder()` moves the task to the top of its group
3. `RefreshTasks()` rebuilds the entire list immediately after the change

This is disorienting — the task you just interacted with disappears from where you were looking, especially in the tray where mouse-driven interaction makes positional stability important.

## What We're Building

**Sort on open, stable during interaction.**

- **On fresh open** (popup show, TUI launch, CLI list): tasks are sorted using the full sort: InProgress > Priority > DueDate > CreatedAt > Pending > Done
- **During interaction**: status changes update the visual indicator in-place without moving the task. No re-sort, no BumpSortOrder.
- **Re-sort happens** next time the view is freshly opened.

## Key Decisions

1. **No reorder on status change** — task stays exactly where it is visually. Only the status icon (checkbox, dash, etc.) updates.
2. **No BumpSortOrder on status change** — sort_order is unchanged. Only new tasks get the highest sort_order (appear at top on next sort).
3. **Sort order on load** — InProgress > Priority > DueDate > CreatedAt > Pending > Done (same as current `GetSortedTasks` logic).
4. **Applies to all consumers** — Tray, TUI, and CLI all use the same sort-on-load behavior. The difference is that interactive consumers (tray, TUI) don't re-sort after status changes.

## Approach

### Tray
- After status change: update the checkbox/icon in-place, do NOT call `RefreshTasks()`. The task stays in its current DOM position.
- On popup show (`ShowAtPosition`): full refresh with sorting (already happens).

### TUI
- After status toggle: update the task object in the local list, re-render without re-fetching/re-sorting.
- On launch or list switch: full sort.

### CLI
- No change needed — each `tasker list` invocation is a fresh sort.

### Data Layer
- Remove `BumpSortOrder()` call from `SetStatus()` / `SetStatuses()`.
- Keep `BumpSortOrder()` for add and explicit reorder operations only.

## Open Questions

None — approach is straightforward.
