---
title: Smooth drag reorder animation for tasks and list headers
type: chore
date: 2026-02-06
task: "b83"
---

# Smooth Drag Reorder Animation

## Overview

When dragging a task or list header to reorder, sibling items should smoothly slide apart to make visual space at the drop position. Currently there's only a static blue line indicator. The new animation uses `TranslateTransform.Y` offsets on siblings so items physically "move out of the way."

## Proposed Solution

Replace the current drop indicator (blue line inserted into DOM) with transform-based sibling displacement:

1. When the drop index changes, calculate which items need to shift
2. Apply `RenderTransform = translateY(±Npx)` to displaced siblings
3. Avalonia's existing CSS transitions (150ms CubicEaseOut) animate the shift
4. On drop/cancel, reset all transforms to `translateY(0)`
5. Remove the drop indicator entirely

Both `taskItem` and `listHeader` already have `TransformOperationsTransition` on `RenderTransform` in the AXAML styles, so the animation is automatic.

## Technical Approach

### Task Drag Animation

In `UpdateTaskDropIndicator`, instead of inserting a `_dropIndicator` border:

```csharp
// TaskListPopup.axaml.cs — UpdateTaskDropIndicator replacement
private void AnimateTaskSiblings(StackPanel tasksPanel, int dropIndex)
{
    var taskBorders = tasksPanel.Children
        .OfType<Border>()
        .Where(b => b.Classes.Contains("taskItem"))
        .ToList();

    // Get the height of the dragged item (for gap size)
    var gapHeight = _draggedBorder?.Bounds.Height + 4 ?? 44; // 4 = margin

    for (var i = 0; i < taskBorders.Count; i++)
    {
        var border = taskBorders[i];
        if (border == _draggedBorder) continue; // skip the dragged item itself

        // Find this border's logical index (excluding the dragged item)
        var logicalIndex = i;

        if (logicalIndex >= dropIndex)
        {
            // Items at or below drop position: push down
            border.RenderTransform = new TranslateTransform(0, gapHeight);
        }
        else
        {
            // Items above drop position: reset to original
            border.RenderTransform = new TranslateTransform(0, 0);
        }
    }
}
```

### List Header Drag Animation

Same pattern in `UpdateListDropIndicator`, but operating on `listHeader` borders in `TaskListPanel` and also shifting their associated `listTasks` StackPanels.

```csharp
// TaskListPopup.axaml.cs — AnimateListSiblings
private void AnimateListSiblings(int dropIndex)
{
    var listHeaders = TaskListPanel.Children
        .OfType<Border>()
        .Where(b => b.Classes.Contains("listHeader"))
        .ToList();

    var gapHeight = _draggedBorder?.Bounds.Height + 8 ?? 36; // header height + margin

    for (var i = 0; i < listHeaders.Count; i++)
    {
        var header = listHeaders[i];
        if (header == _draggedBorder) continue;

        var offset = i >= dropIndex ? gapHeight : 0;
        header.RenderTransform = new TranslateTransform(0, offset);

        // Also shift the associated listTasks panel
        var headerIdx = TaskListPanel.Children.IndexOf(header);
        if (headerIdx + 1 < TaskListPanel.Children.Count &&
            TaskListPanel.Children[headerIdx + 1] is StackPanel sp &&
            sp.Classes.Contains("listTasks"))
        {
            sp.RenderTransform = new TranslateTransform(0, offset);
        }
    }
}
```

### Reset on Cleanup

In `CleanupDrag()`, reset all transforms:

```csharp
// TaskListPopup.axaml.cs — ResetSiblingTransforms
private void ResetSiblingTransforms()
{
    // Reset task items
    foreach (var panel in _listTaskPanels.Values)
    {
        foreach (var child in panel.Children.OfType<Border>())
        {
            if (child.Classes.Contains("taskItem"))
                child.RenderTransform = new TranslateTransform(0, 0);
        }
    }

    // Reset list headers and their task panels
    foreach (var child in TaskListPanel.Children)
    {
        if (child is Border b && b.Classes.Contains("listHeader"))
            b.RenderTransform = new TranslateTransform(0, 0);
        if (child is StackPanel sp && sp.Classes.Contains("listTasks"))
            sp.RenderTransform = new TranslateTransform(0, 0);
    }
}
```

### XAML: Add transition to listTasks panels

The `StackPanel.listTasks` style needs a `RenderTransform` transition for list drag animation:

```xml
<!-- TaskListPopup.axaml — add to StackPanel.listTasks style -->
<Setter Property="RenderTransform" Value="translateY(0)"/>
<!-- Add TransformOperationsTransition to existing Transitions -->
```

### Remove Drop Indicator

- Remove `EnsureDropIndicator()`, `RemoveDropIndicator()`, `_dropIndicator` field
- Remove drop indicator XAML styles (`Border.dropIndicator`, `Border.dropIndicator.visible`)
- Replace calls to `UpdateTaskDropIndicator` with `AnimateTaskSiblings`
- Replace calls to `UpdateListDropIndicator` with `AnimateListSiblings`
- Add `ResetSiblingTransforms()` call in `CleanupDrag()`

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Replace drop indicator methods with transform animation methods |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Remove drop indicator styles, add RenderTransform transition to listTasks |

## Acceptance Criteria

- [x] Tasks smoothly slide apart when drag crosses a boundary
- [x] Tasks smoothly slide back when drag moves away
- [x] List headers smoothly slide apart during list reorder
- [x] List task panels shift along with their headers
- [x] Blue drop indicator line is removed
- [x] All transforms reset to 0 on drop completion
- [x] All transforms reset to 0 on drag cancel
- [x] Animation timing matches existing style (150ms CubicEaseOut)
- [x] Dragged item itself is not displaced (opacity 0.3 as before)
- [x] No visual glitches when rapidly changing drop position

## Edge Cases

- Only 1 task in list: drag not enabled, no animation needed
- Dragging past the last item: all items above shift normally
- Dragging above the first item: all items shift down
- Rapid mouse movement: transforms update immediately, CSS transitions handle smoothing
- Drag cancel (escape/outside): all transforms reset gracefully

## References

- Brainstorm: `docs/brainstorms/2026-02-06-drag-reorder-animation-brainstorm.md`
- Existing transitions: `TaskListPopup.axaml:37-45` (taskItem), `TaskListPopup.axaml:88-95` (listHeader)
- Drop indicator: `TaskListPopup.axaml.cs:2199-2217` (to remove)
- Update methods: `TaskListPopup.axaml.cs:1860` (task), `TaskListPopup.axaml.cs:2085` (list)
- Cleanup: `TaskListPopup.axaml.cs:2219-2267`
- Gotcha: `docs/solutions/ui-bugs/pointer-pressed-handler-conflict-prevents-drag.md` — event handler order matters
