---
title: "feat: Add Animations and Transitions to TaskerTray"
type: feat
date: 2026-02-04
---

# Add Animations and Transitions to TaskerTray

## Overview

Add subtle, fast animations (100-200ms) throughout the TaskerTray popup to create a more polished, user-friendly experience. This includes popup open/close, list collapse/expand, and task interactions (add, delete, check/uncheck).

## Problem Statement / Motivation

The current TaskerTray popup feels static and abrupt. UI elements appear and disappear instantly without visual feedback, making the app feel less polished. Animations provide:
- Visual continuity between states
- Feedback that actions were registered
- A more premium, native-feeling experience

## Proposed Solution

Add CSS-class based transitions using Avalonia's built-in animation system. Use subtle timing (100-200ms) with appropriate easing functions to keep the app snappy while adding polish.

**Animation Categories:**
1. **Popup open/close**: Fade + slide for smooth appearance/disappearance
2. **List collapse/expand**: Height + opacity transitions for accordion effect
3. **Task add**: Slide-in + fade for new task items
4. **Task delete**: Slide-out + fade before removal
5. **Checkbox toggle**: Color transition + subtle feedback

## Technical Approach

### Phase 1: Add Global Animation Styles

Add reusable animation styles to the AXAML file.

**File: `src/TaskerTray/Views/TaskListPopup.axaml`**

```xml
<Window.Styles>
    <!-- Popup content fade + slide -->
    <Style Selector="Border.popupContent">
        <Setter Property="Opacity" Value="0"/>
        <Setter Property="RenderTransform" Value="translateY(-8px)"/>
        <Setter Property="Transitions">
            <Transitions>
                <DoubleTransition Property="Opacity" Duration="0:0:0.15" Easing="CubicEaseOut"/>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.15" Easing="CubicEaseOut"/>
            </Transitions>
        </Setter>
    </Style>
    <Style Selector="Border.popupContent.visible">
        <Setter Property="Opacity" Value="1"/>
        <Setter Property="RenderTransform" Value="translateY(0)"/>
    </Style>

    <!-- Task item base with transitions -->
    <Style Selector="Border.taskItem">
        <Setter Property="Opacity" Value="1"/>
        <Setter Property="RenderTransform" Value="translateX(0)"/>
        <Setter Property="Transitions">
            <Transitions>
                <DoubleTransition Property="Opacity" Duration="0:0:0.15" Easing="CubicEaseOut"/>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.15" Easing="CubicEaseOut"/>
            </Transitions>
        </Setter>
    </Style>
    <Style Selector="Border.taskItem.checked">
        <Setter Property="Opacity" Value="0.7"/>
    </Style>
    <Style Selector="Border.taskItem.entering">
        <Setter Property="Opacity" Value="0"/>
        <Setter Property="RenderTransform" Value="translateY(-10px)"/>
    </Style>
    <Style Selector="Border.taskItem.exiting">
        <Setter Property="Opacity" Value="0"/>
        <Setter Property="RenderTransform" Value="translateX(20px)"/>
    </Style>

    <!-- List section collapse/expand -->
    <Style Selector="StackPanel.listTasks">
        <Setter Property="MaxHeight" Value="2000"/>
        <Setter Property="Opacity" Value="1"/>
        <Setter Property="Transitions">
            <Transitions>
                <DoubleTransition Property="MaxHeight" Duration="0:0:0.2" Easing="CubicEaseInOut"/>
                <DoubleTransition Property="Opacity" Duration="0:0:0.15" Easing="CubicEaseOut"/>
            </Transitions>
        </Setter>
    </Style>
    <Style Selector="StackPanel.listTasks.collapsed">
        <Setter Property="MaxHeight" Value="0"/>
        <Setter Property="Opacity" Value="0"/>
    </Style>

    <!-- Chevron rotation -->
    <Style Selector="Button.chevron">
        <Setter Property="RenderTransform" Value="rotate(0deg)"/>
        <Setter Property="RenderTransformOrigin" Value="50%, 50%"/>
        <Setter Property="Transitions">
            <Transitions>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.2" Easing="CubicEaseInOut"/>
            </Transitions>
        </Setter>
    </Style>
    <Style Selector="Button.chevron.collapsed">
        <Setter Property="RenderTransform" Value="rotate(-90deg)"/>
    </Style>

    <!-- Inline input field fade-in -->
    <Style Selector="Border.inlineInput">
        <Setter Property="Opacity" Value="1"/>
        <Setter Property="RenderTransform" Value="translateY(0)"/>
        <Setter Property="Transitions">
            <Transitions>
                <DoubleTransition Property="Opacity" Duration="0:0:0.15" Easing="CubicEaseOut"/>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.15" Easing="CubicEaseOut"/>
            </Transitions>
        </Setter>
    </Style>
    <Style Selector="Border.inlineInput.entering">
        <Setter Property="Opacity" Value="0"/>
        <Setter Property="RenderTransform" Value="translateY(-8px)"/>
    </Style>

    <!-- Hover effect for task items -->
    <Style Selector="Border.taskItem:pointerover">
        <Setter Property="Background" Value="#333333"/>
    </Style>
</Window.Styles>
```

### Phase 2: Popup Open/Close Animation

Update the main Border to use animation classes.

**File: `src/TaskerTray/Views/TaskListPopup.axaml`**

```xml
<Border x:Name="PopupContent"
        Classes="popupContent"
        CornerRadius="14"
        Background="#202020"
        BorderBrush="#404040"
        BorderThickness="1"
        Padding="0">
```

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
public void ShowAtPosition(PixelPoint position)
{
    _showCount++;
    Position = position;
    CancelInlineEdit();
    RefreshTasks();

    // Reset to hidden state
    PopupContent.Classes.Remove("visible");

    Show();

    // Trigger fade-in after render
    Dispatcher.UIThread.Post(() =>
    {
        PopupContent.Classes.Add("visible");
    }, DispatcherPriority.Render);

    // ... rest of existing activation code
}

private async void OnDeactivated()
{
    SavePendingInlineAdd();
    CancelInlineEdit();

    // Animate out before hiding
    PopupContent.Classes.Remove("visible");
    await Task.Delay(150);

    Hide();
    // ... rest of existing hide code
}
```

### Phase 3: List Collapse/Expand Animation

Refactor `BuildTaskList()` to use container panels for each list's tasks that can be animated.

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private Dictionary<string, StackPanel> _listTaskPanels = new();

private void BuildTaskList()
{
    TaskListPanel.Children.Clear();
    _listTaskPanels.Clear();

    // ... existing create list input logic ...

    if (_currentListFilter == null)
    {
        var allListNames = TodoTaskList.GetAllListNames();
        var tasksByList = _tasks.GroupBy(t => t.ListName).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var listName in allListNames)
        {
            var isCollapsed = TodoTaskList.IsListCollapsed(listName);
            AddListHeader(listName, isCollapsed);

            // Create container panel for this list's tasks
            var tasksPanel = new StackPanel
            {
                Classes = { "listTasks" },
                ClipToBounds = true
            };
            if (isCollapsed)
            {
                tasksPanel.Classes.Add("collapsed");
            }
            _listTaskPanels[listName] = tasksPanel;

            // Add tasks to the container
            if (_addingToList == listName)
            {
                tasksPanel.Children.Add(CreateInlineAddField(listName));
            }

            var tasksInList = tasksByList.GetValueOrDefault(listName, new List<TodoTaskViewModel>());
            if (tasksInList.Count == 0 && _addingToList != listName)
            {
                tasksPanel.Children.Add(CreateEmptyIndicator());
            }
            else
            {
                foreach (var task in tasksInList)
                {
                    tasksPanel.Children.Add(CreateTaskItem(task));
                }
            }

            TaskListPanel.Children.Add(tasksPanel);
        }
    }
    // ... rest of existing logic
}

private void OnToggleListCollapsed(string listName, bool collapsed)
{
    TodoTaskList.SetListCollapsed(listName, collapsed);

    // Animate the tasks panel
    if (_listTaskPanels.TryGetValue(listName, out var tasksPanel))
    {
        if (collapsed)
            tasksPanel.Classes.Add("collapsed");
        else
            tasksPanel.Classes.Remove("collapsed");
    }

    // Update chevron
    if (_chevronButtons.TryGetValue(listName, out var chevron))
    {
        if (collapsed)
            chevron.Classes.Add("collapsed");
        else
            chevron.Classes.Remove("collapsed");
    }

    // Update header summary
    BuildTaskList(); // Rebuild to update summary text
}
```

### Phase 4: Task Add Animation

Animate new task items when they appear.

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private string? _newlyAddedTaskId;

private void SubmitInlineAdd(string? text, string listName)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        CancelInlineEdit();
        return;
    }

    try
    {
        var task = TodoTask.CreateTodoTask(text.Trim(), listName);
        _newlyAddedTaskId = task.Id; // Track for animation
        var taskList = new TodoTaskList(listName);
        taskList.AddTodoTask(task);
        CancelInlineEdit();
        RefreshTasks();
    }
    catch (Exception ex)
    {
        StatusText.Text = $"Error: {ex.Message}";
        CancelInlineEdit();
    }
}

private Border CreateTaskItem(TodoTaskViewModel task)
{
    var border = new Border
    {
        Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 2),
        Classes = { "taskItem" }
    };

    // Add checked class if task is checked
    if (task.IsChecked)
    {
        border.Classes.Add("checked");
    }

    // Animate entrance for newly added task
    if (task.Id == _newlyAddedTaskId)
    {
        border.Classes.Add("entering");
        Dispatcher.UIThread.Post(() =>
        {
            border.Classes.Remove("entering");
        }, DispatcherPriority.Render);
        _newlyAddedTaskId = null;
    }

    // ... rest of existing task item creation
    return border;
}
```

### Phase 5: Task Delete Animation

Animate task removal before actually deleting.

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private Dictionary<string, Border> _taskBorders = new();

private Border CreateTaskItem(TodoTaskViewModel task)
{
    var border = new Border { /* ... existing ... */ };
    _taskBorders[task.Id] = border; // Track for animation
    // ... rest
    return border;
}

private async void OnDeleteTaskClicked(TodoTaskViewModel task)
{
    // Animate out
    if (_taskBorders.TryGetValue(task.Id, out var border))
    {
        border.Classes.Add("exiting");
        await Task.Delay(150); // Wait for animation
    }

    try
    {
        var taskList = new TodoTaskList();
        taskList.DeleteTask(task.Id);
        RefreshTasks();
    }
    catch (Exception ex)
    {
        StatusText.Text = $"Error: {ex.Message}";
    }
}
```

### Phase 6: Checkbox Toggle Animation

Add visual feedback for check/uncheck.

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private async void OnCheckboxClicked(TodoTaskViewModel task, CheckBox checkbox)
{
    // Visual feedback on the task border
    if (_taskBorders.TryGetValue(task.Id, out var border))
    {
        if (checkbox.IsChecked == true)
        {
            border.Classes.Add("checked");
        }
        else
        {
            border.Classes.Remove("checked");
        }
    }

    try
    {
        var taskList = new TodoTaskList();
        if (checkbox.IsChecked == true)
        {
            taskList.CheckTask(task.Id);
        }
        else
        {
            taskList.UncheckTask(task.Id);
        }

        // Delayed refresh to allow animation to complete
        await Task.Delay(150);
        Dispatcher.UIThread.Post(() => RefreshTasks(), DispatcherPriority.Background);
    }
    catch (Exception ex)
    {
        StatusText.Text = $"Error: {ex.Message}";
    }
}
```

### Phase 7: Inline Input Animation

Animate inline add/edit fields when they appear.

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private Border CreateInlineAddField(string listName)
{
    var border = new Border
    {
        Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 2),
        Classes = { "inlineInput", "entering" }
    };

    // Trigger animation after render
    Dispatcher.UIThread.Post(() =>
    {
        border.Classes.Remove("entering");
    }, DispatcherPriority.Render);

    // ... rest of existing logic
    return border;
}
```

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Animation duration | 100-200ms | Fast enough to feel snappy, slow enough to be noticeable |
| Easing function | CubicEaseOut for enter, CubicEaseIn for exit | Natural feeling acceleration/deceleration |
| Popup animation | Fade + slide up | Common pattern for dropdown/popup menus |
| List collapse | MaxHeight transition | Avalonia doesn't support `height: auto` animation |
| Task add | Slide down + fade in | Shows where new item came from |
| Task delete | Slide right + fade out | Suggests removal/dismissal |
| Checkbox | Opacity change | Subtle feedback without being distracting |

## Acceptance Criteria

### Popup Animations
- [x] Popup fades in + slides down when opened (150ms)
- [x] Popup fades out + slides up when closed (150ms)

### List Collapse/Expand
- [x] Lists animate height when collapsing (200ms)
- [x] Lists animate height when expanding (200ms)
- [x] Chevron rotates smoothly (200ms)
- [x] Summary text fades in when collapsed

### Task Interactions
- [x] New tasks slide in + fade in from top (150ms)
- [x] Deleted tasks slide out + fade out to right (150ms)
- [x] Checked tasks dim smoothly (150ms)
- [x] Unchecked tasks brighten smoothly (150ms)

### Inline Inputs
- [x] Add task input fades in + slides down (150ms)
- [x] Edit task input fades in (150ms)
- [x] Create list input fades in + slides down (150ms)

### Performance
- [x] No visible jank or stuttering
- [x] Animations don't block user interaction
- [x] App remains responsive during animations

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml` | Add animation styles, update Border classes |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add animation triggers, track elements for animation |

## Verification

```bash
# Build
cd /Users/carlos/self-development/cli-tasker/src/TaskerTray
dotnet build

# Run and test
dotnet run

# Test cases:
# 1. Click tray icon -> popup fades in smoothly
# 2. Click outside -> popup fades out smoothly
# 3. Press Escape -> popup fades out smoothly
# 4. Click chevron -> list collapses with animation
# 5. Click chevron again -> list expands with animation
# 6. Click "+" on list -> input slides in
# 7. Add task -> new task slides in from top
# 8. Delete task -> task slides out to right
# 9. Check task -> task dims smoothly
# 10. Uncheck task -> task brightens smoothly
```

## References

### Internal References
- TaskListPopup.axaml: `src/TaskerTray/Views/TaskListPopup.axaml`
- TaskListPopup.axaml.cs: `src/TaskerTray/Views/TaskListPopup.axaml.cs`
- Collapsible lists feature: `docs/solutions/feature-implementations/collapsible-lists-tray.md`

### External References
- Avalonia Transitions: https://docs.avaloniaui.net/docs/animations/transitions
- Avalonia Keyframe Animations: https://docs.avaloniaui.net/docs/animations/keyframe-animations
