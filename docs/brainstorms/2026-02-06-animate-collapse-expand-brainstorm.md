---
topic: Animate list section collapse/expand in tray
date: 2026-02-06
tags: [ux, animation, tray-only]
task: 97f
---

# Animate Collapse/Expand

## What We're Building

Smooth animated collapse/expand when toggling list sections in the tray popup. Currently clicks snap instantly because `OnToggleListCollapsed` calls `BuildTaskList()` which destroys and recreates all UI elements, preventing the existing XAML transitions from running.

## Why This Approach

The animations are **already defined** in `TaskListPopup.axaml`:

- `StackPanel.listTasks` has MaxHeight (200ms CubicEaseInOut), Opacity (150ms CubicEaseOut), and RenderTransform transitions
- `StackPanel.listTasks.collapsed` sets MaxHeight=0, Opacity=0
- `Button.chevron` has RenderTransform rotation transition (200ms CubicEaseInOut)
- `Button.chevron.collapsed` rotates -90deg

The fix is to **toggle classes in-place** instead of rebuilding the UI.

## Key Decisions

1. **In-place class toggle** - Change `OnToggleListCollapsed` to find the existing StackPanel and chevron for the target list, then add/remove the `collapsed` class. No `BuildTaskList()` call.
2. **Refine animation timing** - Review and adjust durations/easings for a polished feel (current MaxHeight 200ms may need tuning for content-heavy lists).
3. **Update tooltip** - Toggle "Expand list" / "Collapse list" tooltip on the chevron.
4. **Persist state** - Continue saving `is_collapsed` to SQLite (already works).

## Implementation Notes

- Need a way to find the StackPanel and chevron for a given list name. Options:
  - Walk `TaskListPanel.Children` sequentially (header Border, then StackPanel)
  - Tag elements with `Tag` property set to list name for direct lookup
- `ClipToBounds=true` is already set on tasks panels, so content won't overflow during animation
- `CollapseAllListsForDrag` already does in-place class toggling (lines 2319-2349) — follow that pattern

## Open Questions

- Should MaxHeight transition duration scale with content height, or stay fixed at 200ms?
- Should collapsed state also hide the task count summary on the header, or keep it visible? (Currently keeps it visible — likely keep this)
