---
title: fix: Task titles not wrapping in TaskerTray
type: fix
date: 2026-02-05
---

# Fix: Task Titles Not Wrapping in TaskerTray

## Problem

Task titles in TaskerTray don't wrap and overlap with the menu button (•••) when the title is long.

**Root cause:** The title `TextBlock` is inside a horizontal `StackPanel` (`titleRow`), which doesn't constrain width. Even though `TextWrapping.Wrap` is set, the StackPanel lets the text grow indefinitely.

## Acceptance Criteria

- [ ] Long task titles wrap to multiple lines instead of overflowing
- [ ] Title text does not overlap with the menu button
- [ ] Priority indicator still appears before the title
- [ ] Layout looks correct for short and long titles

## Solution

In `CreateTaskItem()`, change the titleRow from a horizontal `StackPanel` to a `Grid` with fixed columns that respects the parent's width constraint.

### TaskListPopup.axaml.cs

```csharp
// Before: StackPanel doesn't constrain width
var titleRow = new StackPanel
{
    Orientation = Avalonia.Layout.Orientation.Horizontal,
    Spacing = 6
};

// After: Grid with Auto + * columns constrains the title
var titleRow = new Grid
{
    ColumnDefinitions = ColumnDefinitions.Parse("Auto,*")
};

// Priority indicator in column 0
if (task.HasPriority)
{
    var priorityIndicator = new TextBlock { ... };
    priorityIndicator.Margin = new Thickness(0, 0, 6, 0); // Add spacing
    Grid.SetColumn(priorityIndicator, 0);
    titleRow.Children.Add(priorityIndicator);
}

// Title in column 1 (will wrap within available space)
var title = new TextBlock { ... };
Grid.SetColumn(title, task.HasPriority ? 1 : 0);
if (!task.HasPriority)
    Grid.SetColumnSpan(title, 2);
titleRow.Children.Add(title);
```

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Change titleRow from StackPanel to Grid in `CreateTaskItem()` |

## Verification

1. Build and run TaskerTray
2. Add a task with a long title (e.g., "This is a very long task title that should wrap to multiple lines")
3. Verify the title wraps and doesn't overlap the menu button
4. Verify tasks with priority indicators still display correctly
