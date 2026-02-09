---
title: Tray sticky list headers
date: 2026-02-08
tags: [tray, ui, ux, scroll, sticky-headers]
---

# Tray Sticky List Headers

## What We're Building

When scrolling through tasks in the Tray popup's "All Lists" view, the current list's header should pin to the top of the scroll area — like iOS UITableView section headers. As the next section header scrolls up and reaches the pinned header, it pushes the previous one out (push-replace behavior). Only one header is sticky at a time.

The sticky header IS the real list header with full interactivity: chevron toggle, task count summary, add button, and menu button all remain functional while pinned.

## Why This Approach

Users need quick access to list operations (add task, rename, collapse) for the list they're currently viewing. When a list has many tasks, the header scrolls off-screen, forcing the user to scroll back up to interact with it. Sticky headers solve this by keeping the active section's header always visible.

## Key Decisions

1. **Behavior**: Push-replace (iOS-style). Only one sticky header at a time; the next section header pushes the current one out as it approaches.

2. **Content**: The sticky header is the actual header element, not a simplified version. All actions (collapse, add, menu) remain functional.

3. **Implementation**: Transform-based pinning. On `ScrollChanged`, apply a `TranslateTransform` to the header Border that should be sticky, offsetting it by the scroll amount to keep it at the top. When the next header approaches, clamp the transform so the current header gets pushed up naturally. No element duplication — the real header moves.

4. **Scope**: Only applies when viewing "All Lists" with multiple sections. When a single list is filtered, no sticky behavior is needed since there's only one header.

## Approach Details

### Transform Pinning

- Listen to `ScrollViewer.ScrollChanged` on `TaskListScrollViewer`
- Track each list header Border and its corresponding tasks StackPanel in `_listTaskPanels` (already tracked)
- On scroll:
  1. Find which section headers are above the scroll viewport top
  2. The last header that scrolled past the top is the "sticky" candidate
  3. Apply `TranslateTransform.Y = scrollOffset - headerNaturalPosition` to pin it
  4. If the next header is approaching, clamp the transform so sticky header gets pushed up
  5. Set `ZIndex` on the sticky header so it renders above task items
- On scroll back: remove transforms as headers return to natural position
- On collapse/expand: recalculate positions since layout changes

### Edge Cases

- Collapsed sections: header is sticky but no tasks below, so the next header immediately follows
- Very short sections: header may barely pin before the next one pushes it
- Window resize: positions need recalculation
- Help panel visible: no sticky behavior needed (task list is hidden)

## Open Questions

- Does Avalonia's `TranslateTransform` on a child inside a `StackPanel` play well with hit-testing? (The header needs to remain clickable at its visual position, not its layout position.) If not, may need to fall back to Approach A (overlay clone).
- Should the sticky header have a subtle visual distinction (e.g., slightly different background, bottom border, or shadow) to indicate it's pinned?

## References

- `src/TaskerTray/Views/TaskListPopup.axaml` — ScrollViewer and StackPanel layout (lines 215-220)
- `src/TaskerTray/Views/TaskListPopup.axaml.cs` — `BuildTaskList()` (~line 296), `AddListHeader()` (~line 442)
- `_listTaskPanels` dictionary — already tracks list section panels
- XAML styles for `Border.listHeader` — existing header styles (lines 71-89)
