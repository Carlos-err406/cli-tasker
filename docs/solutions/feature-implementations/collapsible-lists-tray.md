---
title: add collapsible lists feature to TaskerTray menu bar app
category: feature-implementations
tags:
  - TaskerTray
  - menu-bar-app
  - popup-ui
  - collapsible-lists
  - state-persistence
  - json-storage
  - avalonia
module: src/TaskerTray
symptoms:
  - user requested ability to collapse/expand task lists in tray popup
  - lists took up too much vertical space when viewing multiple lists
  - no way to hide completed or inactive lists temporarily
date_solved: 2026-02-04
---

# Collapsible Lists in TaskerTray

## Problem/Feature Request

In the TaskerTray menu bar application, users with multiple task lists could find the UI cluttered when many lists had numerous tasks. There was no way to temporarily hide the contents of specific lists to focus on others or reduce visual noise. Users needed the ability to collapse individual lists while still seeing a summary of their contents.

Related task: `tasker-ideas` list, task "feature: collapse list - allow collapsing lists in desktop popup window"

## Solution Approach

The implementation adds a collapsible state to each task list that:

1. **Persists in storage**: The `IsCollapsed` property is stored in the JSON file alongside the list data, ensuring state survives app restarts
2. **Is backwards compatible**: Uses an optional property with `default false`, so existing data and CLI tools continue to work unchanged
3. **Shows a summary when collapsed**: Displays "X tasks, Y pending" so users still have context about hidden content
4. **Auto-expands on add**: Clicking the "+" button on a collapsed list automatically expands it so users can see the new task input field

## Files Changed

| File | Changes |
|------|---------|
| `src/TaskerCore/Models/TaskList.cs` | Added `IsCollapsed` property with default `false`, added `SetCollapsed()` method |
| `src/TaskerCore/Data/TodoTaskList.cs` | Added `IsListCollapsed()` and `SetListCollapsed()` static methods |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Updated `AddListHeader()` with chevron toggle, updated `BuildTaskList()` to respect collapse state, updated `StartInlineAdd()` to auto-expand |

## Code Examples

### 1. TaskList Model

Added `IsCollapsed` property with default value and a method to toggle it:

```csharp
// src/TaskerCore/Models/TaskList.cs
public record TaskList(string ListName, TodoTask[] Tasks, bool IsCollapsed = false)
{
    public static TaskList Create(string listName) => new(listName, [], false);

    // ... existing methods ...

    public TaskList SetCollapsed(bool collapsed) => this with { IsCollapsed = collapsed };
}
```

### 2. TodoTaskList Data Layer

Added static methods to read and update collapse state:

```csharp
// src/TaskerCore/Data/TodoTaskList.cs
public static bool IsListCollapsed(string listName)
{
    if (!File.Exists(StoragePaths.AllTasksPath))
        return false;

    try
    {
        var raw = File.ReadAllText(StoragePaths.AllTasksPath);
        var taskLists = DeserializeWithMigration(raw);
        var list = taskLists.FirstOrDefault(l => l.ListName == listName);
        return list?.IsCollapsed ?? false;
    }
    catch (JsonException)
    {
        return false;
    }
}

public static void SetListCollapsed(string listName, bool collapsed)
{
    StoragePaths.EnsureDirectory();
    if (!File.Exists(StoragePaths.AllTasksPath))
        return;

    var raw = File.ReadAllText(StoragePaths.AllTasksPath);
    var taskLists = DeserializeWithMigration(raw);
    var updatedLists = taskLists.Select(l =>
        l.ListName == listName ? l.SetCollapsed(collapsed) : l
    ).ToArray();

    lock (SaveLock)
    {
        File.WriteAllText(StoragePaths.AllTasksPath, JsonSerializer.Serialize(updatedLists));
    }
}
```

### 3. TaskListPopup UI

List header with chevron toggle:

```csharp
// src/TaskerTray/Views/TaskListPopup.axaml.cs
private void AddListHeader(string listName, bool isCollapsed = false)
{
    // Get task counts for summary display when collapsed
    var tasksInList = _tasks.Where(t => t.ListName == listName).ToList();
    var totalCount = tasksInList.Count;
    var pendingCount = tasksInList.Count(t => !t.IsChecked);

    // Collapse chevron button
    var chevronBtn = new Button
    {
        Content = isCollapsed ? "▶" : "▼",
        Width = 18, Height = 18, FontSize = 10,
        // ...styling...
    };
    ToolTip.SetTip(chevronBtn, isCollapsed ? "Expand list" : "Collapse list");
    chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName, !isCollapsed);

    // Show summary when collapsed
    if (isCollapsed)
    {
        var summary = new TextBlock
        {
            Text = $"{totalCount} tasks, {pendingCount} pending",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#555")),
        };
        headerStack.Children.Add(summary);
    }
}

private void OnToggleListCollapsed(string listName, bool collapsed)
{
    TodoTaskList.SetListCollapsed(listName, collapsed);
    BuildTaskList();
}
```

BuildTaskList respects collapse state:

```csharp
private void BuildTaskList()
{
    foreach (var listName in allListNames)
    {
        var isCollapsed = TodoTaskList.IsListCollapsed(listName);
        AddListHeader(listName, isCollapsed);

        // Only show tasks if list is not collapsed
        if (!isCollapsed)
        {
            // Show inline add field and tasks...
        }
    }
}
```

Auto-expand when adding:

```csharp
private void StartInlineAdd(string listName)
{
    CancelInlineEdit();
    _addingToList = listName;

    // Auto-expand the list if it's collapsed
    if (TodoTaskList.IsListCollapsed(listName))
    {
        TodoTaskList.SetListCollapsed(listName, false);
    }

    BuildTaskList();
}
```

## How It Works

1. **Storage Format**: The `IsCollapsed` boolean is stored as an optional property on each `TaskList` in the JSON file:
   ```json
   [
     {"ListName": "tasks", "Tasks": [...], "IsCollapsed": false},
     {"ListName": "work", "Tasks": [...], "IsCollapsed": true}
   ]
   ```

2. **Toggle Interaction**: Each list header displays a chevron button (▼ expanded, ▶ collapsed). Clicking it calls `OnToggleListCollapsed()` which updates storage and rebuilds the UI.

3. **Summary Display**: When collapsed, the header shows "X tasks, Y pending" next to the list name.

4. **Backwards Compatibility**:
   - The `IsCollapsed` property has a default value of `false`
   - CLI commands completely ignore this property
   - Old JSON files without `IsCollapsed` work seamlessly (lists appear expanded)

5. **Single List View Exception**: When viewing a specific list (via the filter dropdown), the collapse state is ignored and tasks are always shown.

## Key Patterns Used

### Optional Properties for Backwards Compatibility

```csharp
// Default value ensures old JSON files load correctly
public record TaskList(string ListName, TodoTask[] Tasks, bool IsCollapsed = false)
```

### Auto-Expand on User Action

When user clicks "+" to add a task on a collapsed list, auto-expand first so they can see the input field.

### Static Methods for Cross-Cutting Concerns

Use static methods in `TodoTaskList` for operations that don't require instance state or list filtering.

## Testing Checklist

- [ ] Click chevron on expanded list → collapses, shows summary
- [ ] Click chevron on collapsed list → expands, shows tasks
- [ ] Close/reopen popup → collapse state preserved
- [ ] Quit/restart app → collapse state preserved
- [ ] Click "+" on collapsed list → auto-expands
- [ ] Filter to single list → tasks shown regardless of collapse state
- [ ] `tasker list` CLI → works unchanged
- [ ] Old JSON without `IsCollapsed` → loads correctly (expanded)

## Related Documentation

- [List Duplication Bug Fix](../ui-bugs/list-duplication-on-inline-add.md) - Race condition pattern used in this feature
- [Create List Button Plan](../../plans/2026-02-04-feat-tray-create-list-button-plan.md) - Related list management feature
- [Menu Bar Desktop App Plan](../../plans/2026-02-01-feat-menu-bar-desktop-app-plan.md) - TaskerTray architecture foundation
