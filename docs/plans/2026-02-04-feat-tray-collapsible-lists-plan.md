---
title: "feat: Collapsible Lists in TaskerTray"
type: feat
date: 2026-02-04
---

# Collapsible Lists in TaskerTray

## Overview

Add the ability to collapse/expand lists in the TaskerTray popup. When collapsed, a list shows only its header with a task count summary. The collapse state persists in storage but is ignored by CLI versions (normal and TUI).

## Problem Statement / Motivation

When users have many lists with tasks, the popup becomes cluttered. Users want to focus on specific lists while keeping others collapsed to reduce visual noise. The tasker-ideas task "feature: collapse list" specifically requests this functionality.

## Proposed Solution

Add an `IsCollapsed` property to the `TaskList` record. The TaskerTray reads this property to determine whether to render tasks for each list. CLI versions simply ignore the property (it's serialized but not used).

**UI Behavior:**
- Click on list header text or a chevron icon to toggle collapse state
- Collapsed lists show: header with chevron (▶), list name, task summary ("5 tasks, 2 pending"), and action buttons
- Expanded lists show: chevron (▼), list name, tasks, and action buttons (current behavior)
- Default state for new lists: expanded

## Technical Approach

### Phase 1: Update TaskList Model

Add `IsCollapsed` property with default value `false`:

**File: `src/TaskerCore/Models/TaskList.cs`**

```csharp
namespace TaskerCore.Models;

/// <summary>
/// Represents a named list containing tasks. Lists are first-class entities that can exist even when empty.
/// </summary>
public record TaskList(string ListName, TodoTask[] Tasks, bool IsCollapsed = false)
{
    public static TaskList Create(string listName) => new(listName, [], false);

    public TaskList AddTask(TodoTask task) => this with { Tasks = [task, ..Tasks] };

    public TaskList RemoveTask(string taskId) => this with
    {
        Tasks = Tasks.Where(t => t.Id != taskId).ToArray()
    };

    public TaskList UpdateTask(TodoTask updatedTask) => this with
    {
        Tasks = Tasks.Select(t => t.Id == updatedTask.Id ? updatedTask : t).ToArray()
    };

    public TaskList ReplaceTasks(TodoTask[] newTasks) => this with { Tasks = newTasks };

    public TaskList SetCollapsed(bool collapsed) => this with { IsCollapsed = collapsed };
}
```

### Phase 2: Add TodoTaskList Methods for Collapse State

**File: `src/TaskerCore/Data/TodoTaskList.cs`**

Add static methods to get and set collapse state:

```csharp
/// <summary>
/// Gets the collapse state for a list. Returns false if list doesn't exist.
/// </summary>
public static bool IsListCollapsed(string listName)
{
    // Implementation reads TaskLists and finds the list
}

/// <summary>
/// Sets the collapse state for a list.
/// </summary>
public static void SetListCollapsed(string listName, bool collapsed)
{
    // Implementation updates the IsCollapsed property and saves
}
```

### Phase 3: Update TaskerTray BuildTaskList

Modify `BuildTaskList()` to check collapse state and render appropriately:

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private void BuildTaskList()
{
    TaskListPanel.Children.Clear();

    // ... existing create list input logic ...

    if (_currentListFilter == null)
    {
        var allListNames = TodoTaskList.GetAllListNames();
        var tasksByList = _tasks.GroupBy(t => t.ListName).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var listName in allListNames)
        {
            var isCollapsed = TodoTaskList.IsListCollapsed(listName);
            AddListHeader(listName, isCollapsed);

            // Only show tasks if not collapsed
            if (!isCollapsed)
            {
                // Show inline add if adding to this list
                if (_addingToList == listName)
                {
                    TaskListPanel.Children.Add(CreateInlineAddField(listName));
                }

                var tasksInList = tasksByList.GetValueOrDefault(listName, new List<TodoTaskViewModel>());
                // ... render tasks ...
            }
        }
    }
    // ... rest of existing logic ...
}
```

### Phase 4: Update AddListHeader for Collapse Toggle

Modify `AddListHeader()` to include collapse chevron and summary:

```csharp
private void AddListHeader(string listName, bool isCollapsed = false)
{
    var isDefaultList = listName == ListManager.DefaultListName;

    // Get task counts for summary
    var tasksInList = _tasks.Where(t => t.ListName == listName).ToList();
    var totalCount = tasksInList.Count;
    var pendingCount = tasksInList.Count(t => !t.IsChecked);

    var headerPanel = new Grid
    {
        ColumnDefinitions = ColumnDefinitions.Parse(isDefaultList ? "Auto,*,Auto,Auto" : "Auto,*,Auto,Auto,Auto"),
        Margin = new Thickness(4, 8, 4, 4)
    };

    // Collapse chevron button (column 0)
    var chevronBtn = new Button
    {
        Content = isCollapsed ? "▶" : "▼",
        Width = 18,
        Height = 18,
        FontSize = 10,
        Padding = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Color.Parse("#666")),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName, !isCollapsed);
    Grid.SetColumn(chevronBtn, 0);
    headerPanel.Children.Add(chevronBtn);

    // List name + summary (column 1)
    var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

    var header = new TextBlock
    {
        Text = listName,
        FontWeight = FontWeight.SemiBold,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#888")),
        VerticalAlignment = VerticalAlignment.Center
    };
    headerStack.Children.Add(header);

    // Show summary when collapsed
    if (isCollapsed && totalCount > 0)
    {
        var summary = new TextBlock
        {
            Text = $"{totalCount} tasks, {pendingCount} pending",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#555")),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerStack.Children.Add(summary);
    }

    Grid.SetColumn(headerStack, 1);
    headerPanel.Children.Add(headerStack);

    // Add button (column 2) - adjusted column index
    var addBtn = new Button { /* ... existing ... */ };
    Grid.SetColumn(addBtn, 2);
    headerPanel.Children.Add(addBtn);

    // Menu button for non-default lists (column 3)
    if (!isDefaultList)
    {
        var menuBtn = new Button { /* ... existing ... */ };
        Grid.SetColumn(menuBtn, 3);
        headerPanel.Children.Add(menuBtn);
    }

    TaskListPanel.Children.Add(headerPanel);
}

private void OnToggleListCollapsed(string listName, bool collapsed)
{
    TodoTaskList.SetListCollapsed(listName, collapsed);
    BuildTaskList();
}
```

### Phase 5: Handle Edge Cases

1. **Adding task to collapsed list**: Auto-expand the list when user clicks "+" on a collapsed list header
2. **Creating new list**: New lists are expanded by default (`IsCollapsed = false`)
3. **Single list view**: Collapse state not relevant when filtering to single list (always show tasks)

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Collapsed display | Header + summary line | User preference - more informative than header only |
| Persistence | In storage (JSON) | User preference - survives app restarts |
| Default state | Expanded | User preference - new lists show tasks by default |
| CLI behavior | Ignore IsCollapsed | CLI doesn't need collapse; property is just ignored |
| Toggle trigger | Chevron button click | Clear affordance for collapse action |
| Collapse all lists | Not included | Keep scope minimal; can add later if needed |

## Acceptance Criteria

### Core Functionality
- [x] Lists can be collapsed by clicking chevron button
- [x] Collapsed lists show: chevron (▶), name, and summary ("X tasks, Y pending")
- [x] Expanded lists show: chevron (▼), name, all tasks (current behavior)
- [x] Collapse state persists between app sessions
- [x] New lists default to expanded state

### CLI Compatibility
- [x] `tasker list` works unchanged (ignores IsCollapsed)
- [x] `tasker tui` works unchanged (ignores IsCollapsed)
- [x] Old JSON files without IsCollapsed load correctly (default false)

### Edge Cases
- [x] Clicking "+" on collapsed list header adds task (expands list)
- [x] Single-list filter view shows all tasks regardless of collapse state
- [x] Empty lists can be collapsed (shows "0 tasks, 0 pending")

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Models/TaskList.cs` | Add `IsCollapsed` property with default `false` |
| `src/TaskerCore/Data/TodoTaskList.cs` | Add `IsListCollapsed()` and `SetListCollapsed()` static methods |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Update `BuildTaskList()` and `AddListHeader()` for collapse UI |

## Verification

```bash
# Build
cd /Users/carlos/self-development/cli-tasker/src/TaskerTray
dotnet build

# Run and test
dotnet run

# Test cases:
# 1. Click chevron on list -> collapses, shows summary
# 2. Click chevron again -> expands, shows tasks
# 3. Close and reopen popup -> collapse state preserved
# 4. Quit and restart app -> collapse state preserved
# 5. Collapse list, click "+" button -> expands and shows add field
# 6. Filter to single list -> tasks shown regardless of collapse state
# 7. Create new list -> starts expanded
# 8. Run `tasker list` -> works normally (ignores IsCollapsed)
# 9. Run `tasker tui` -> works normally (ignores IsCollapsed)
```

## References

### Internal References
- TaskList model: `src/TaskerCore/Models/TaskList.cs`
- BuildTaskList implementation: `src/TaskerTray/Views/TaskListPopup.axaml.cs:103-205`
- AddListHeader implementation: `src/TaskerTray/Views/TaskListPopup.axaml.cs:207-277`
- Related task: tasker-ideas list, task 487 "feature: collapse list"

### JSON Migration
The `IsCollapsed` property is optional with default `false`. Existing JSON files will deserialize correctly:
- Files without `IsCollapsed` property will default to `false` (expanded)
- System.Text.Json handles missing properties gracefully with record default values
