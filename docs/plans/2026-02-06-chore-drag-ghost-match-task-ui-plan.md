---
title: Drag ghost should match the UI of the element being dragged
type: chore
date: 2026-02-06
task: "ae2"
---

# Drag Ghost Should Match the UI of the Element Being Dragged

## Overview

The drag ghost that follows the cursor during reorder is currently a minimal TextBlock showing only the title (tasks) or list name (list headers). It should visually match the actual element being dragged — including checkbox, priority, tags, description, due date, and menu button for tasks, and chevron, summary, and buttons for list headers.

## Problem Statement

Current ghost is a single `TextBlock` inside a `Border` with box shadow. Users can't visually confirm *which* task they're dragging when multiple tasks have similar titles — the metadata (priority, tags, due date) is what distinguishes them.

## Proposed Solution

Refactor `CreateDragGhost` and `CreateListDragGhost` to build the same visual hierarchy as the real elements, but with interactivity disabled and a drag-appropriate style (shadow, slightly elevated background, `IsHitTestVisible = false`).

### Strategy: Extract shared rendering helpers

Rather than duplicating the task/header building code, extract the visual-only parts into helper methods that both the real items and ghosts can use. The ghost versions skip event handlers and interactivity.

## Changes

### `TaskListPopup.axaml.cs`

#### 1. Refactor `CreateDragGhost(Border original, TodoTaskViewModel task)`

Replace the current single-TextBlock ghost with a full task layout:

```csharp
// TaskListPopup.axaml.cs — CreateDragGhost
private void CreateDragGhost(Border original, TodoTaskViewModel task)
{
    var grid = new Grid
    {
        ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto")
    };

    // Checkbox (visual only, non-interactive)
    var checkbox = new CheckBox
    {
        IsChecked = task.IsChecked,
        Margin = new Thickness(0, 0, 10, 0),
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false
    };
    Grid.SetColumn(checkbox, 0);
    grid.Children.Add(checkbox);

    // Content panel (title + description + due + tags)
    var contentPanel = BuildTaskContentPanel(task, original.Bounds.Width);
    Grid.SetColumn(contentPanel, 1);
    grid.Children.Add(contentPanel);

    // Menu button (visual placeholder, non-interactive)
    var menuPlaceholder = new TextBlock
    {
        Text = "•••",
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#888")),
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(4, 4, 0, 0)
    };
    Grid.SetColumn(menuPlaceholder, 2);
    grid.Children.Add(menuPlaceholder);

    _dragGhost = new Border
    {
        Width = original.Bounds.Width,
        Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8),
        Opacity = 0.9,
        IsHitTestVisible = false,
        BoxShadow = new BoxShadows(new BoxShadow { ... }),
        Child = grid
    };

    var canvas = GetOrCreateDragCanvas();
    canvas.Children.Add(_dragGhost);
    // ... position as before
}
```

#### 2. Extract `BuildTaskContentPanel(TodoTaskViewModel task, double maxWidth)` helper

Builds the content StackPanel used by both real task items and the ghost:

- Priority indicator (if present)
- Title TextBlock (bold, wrapping)
- Description TextBlock (if present, grey, truncated)
- Due date TextBlock (if present, colored by urgency)
- Tag pills row (if present, using `TagColors.GetHexColor`)

This avoids duplicating 60+ lines of content rendering code.

#### 3. Refactor `CreateListDragGhost(Border original, string listName)`

Replace single TextBlock with list header layout:

```csharp
// TaskListPopup.axaml.cs — CreateListDragGhost
private void CreateListDragGhost(Border original, string listName)
{
    var grid = new Grid { ... };

    // Chevron (visual only)
    var chevron = new TextBlock { Text = "▼", FontSize = 10, ... };
    grid.Children.Add(chevron);

    // List name
    var nameBlock = new TextBlock { Text = listName, FontWeight = SemiBold, ... };
    grid.Children.Add(nameBlock);

    // Summary (task count)
    var taskCount = _tasks.Count(t => t.ListName == listName);
    var uncheckedCount = _tasks.Count(t => t.ListName == listName && !t.IsChecked);
    var summary = new TextBlock { Text = $"{taskCount} tasks, {uncheckedCount} pending", ... };
    grid.Children.Add(summary);

    _dragGhost = new Border { ..., Child = grid };
    // ... position as before
}
```

## Acceptance Criteria

- [x] Task drag ghost shows checkbox (matching checked state)
- [x] Task drag ghost shows priority indicator (if present)
- [x] Task drag ghost shows title text (bold, wrapping/ellipsis)
- [x] Task drag ghost shows description preview (if present)
- [x] Task drag ghost shows due date (if present, with correct color)
- [x] Task drag ghost shows tag pills with correct colors
- [x] Task drag ghost shows "•••" menu placeholder (non-interactive)
- [x] List header drag ghost shows chevron "▼"
- [x] List header drag ghost shows list name
- [x] List header drag ghost shows task count summary
- [x] Ghost has `IsHitTestVisible = false` (no event interference)
- [x] Ghost retains shadow, elevated background, 0.9 opacity
- [x] No code duplication — content rendering shared between real items and ghosts

## Edge Cases

- Checked tasks: ghost should show checked checkbox + dimmed/strikethrough styling
- Long titles: should wrap or ellipsis within ghost width
- Tasks with no metadata: ghost should still look correct (just title + checkbox)
- Tasks with all metadata: ghost should fit priority + title + description + due + tags without overflow

## References

- Current ghost: `TaskListPopup.axaml.cs:1620` (`CreateDragGhost`)
- Current list ghost: `TaskListPopup.axaml.cs:1855` (`CreateListDragGhost`)
- Task item builder: `TaskListPopup.axaml.cs:1033-1262`
- List header builder: `TaskListPopup.axaml.cs:349-509`
- Tag color utility: `src/TaskerCore/Utilities/TagColors.cs`
- Related: `docs/solutions/ui-bugs/cursor-inheritance-on-interactive-children.md` — ghost elements should use `IsHitTestVisible = false`
