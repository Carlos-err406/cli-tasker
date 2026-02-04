---
title: add animations and transitions to TaskerTray menu bar app
category: feature-implementations
tags:
  - TaskerTray
  - menu-bar-app
  - popup-ui
  - animations
  - transitions
  - avalonia
  - css-transitions
  - ux-polish
module: src/TaskerTray
symptoms:
  - popup appeared and disappeared abruptly
  - UI felt static and unpolished
  - no visual feedback on user actions (check, delete, add)
  - lists collapsed/expanded without animation
date_solved: 2026-02-04
---

# Animations and Transitions in TaskerTray

## Problem/Feature Request

The TaskerTray popup felt static and abrupt. UI elements appeared and disappeared instantly without visual feedback, making the app feel less polished. Users requested animations to provide:

- Visual continuity between states
- Feedback that actions were registered
- A more premium, native-feeling experience

## Solution Approach

The implementation uses CSS-class based transitions using Avalonia's built-in animation system. All animations are subtle (100-200ms) with appropriate easing functions to keep the app snappy while adding polish.

**Animation Categories:**

1. **Popup open/close**: Fade + slide for smooth appearance/disappearance
2. **List collapse/expand**: Height + opacity transitions for accordion effect
3. **Task add**: Slide-in + fade for new task items
4. **Task delete**: Slide-out + fade before removal
5. **Checkbox toggle**: Opacity transition for visual feedback
6. **Inline inputs**: Fade-in + slide for add/edit fields

## Files Changed

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml` | Added `Window.Styles` section with animation styles, added `x:Name="PopupContent"` and `Classes="popupContent"` to main Border |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Added animation triggers throughout, tracking fields (`_newlyAddedTaskId`, `_taskBorders`), async `HideWithAnimation()` method |

## Code Examples

### 1. Animation Styles (AXAML)

All animation styles are defined in the `Window.Styles` section:

```xml
<!-- src/TaskerTray/Views/TaskListPopup.axaml -->
<Window.Styles>
    <!-- Popup content fade + slide animation -->
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

    <!-- Task item animations -->
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
        <Setter Property="Opacity" Value="0.6"/>
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

    <!-- Chevron rotation animation -->
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
</Window.Styles>
```

### 2. Popup Open/Close Animation

```csharp
// src/TaskerTray/Views/TaskListPopup.axaml.cs
public void ShowAtPosition(PixelPoint position)
{
    _showCount++;
    Position = position;
    CancelInlineEdit();
    RefreshTasks();

    // Reset to hidden state before showing
    PopupContent.Classes.Remove("visible");

    Show();

    // Trigger fade-in animation after render
    Dispatcher.UIThread.Post(() =>
    {
        PopupContent.Classes.Add("visible");
    }, DispatcherPriority.Render);

    // ... macOS activation code ...
}

private async Task HideWithAnimation()
{
    // Trigger fade-out animation
    PopupContent.Classes.Remove("visible");

    // Wait for animation to complete
    await Task.Delay(150);

    Hide();

    // Hide from Dock when popup closes
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        HideFromDock();
    }
}
```

### 3. Task Add Animation

```csharp
private string? _newlyAddedTaskId; // Track newly added task for entrance animation

private void SubmitInlineAdd(string? text, string listName)
{
    // ...validation...
    var task = TodoTask.CreateTodoTask(text.Trim(), listName);
    _newlyAddedTaskId = task.Id; // Track for entrance animation
    // ...save and refresh...
}

private Border CreateTaskItem(TodoTaskViewModel task)
{
    var border = new Border
    {
        Classes = { "taskItem" }
        // ...
    };

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

    return border;
}
```

### 4. Task Delete Animation

```csharp
private Dictionary<string, Border> _taskBorders = new(); // Track borders for animations

private Border CreateTaskItem(TodoTaskViewModel task)
{
    var border = new Border { /* ... */ };
    _taskBorders[task.Id] = border; // Track for animations
    return border;
}

private async void OnDeleteTaskClicked(TodoTaskViewModel task)
{
    // Animate out before deleting
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
    // ...error handling...
}
```

### 5. Checkbox Toggle Animation

```csharp
private async void OnCheckboxClicked(TodoTaskViewModel task, CheckBox checkbox)
{
    // Visual feedback on the task border
    if (_taskBorders.TryGetValue(task.Id, out var border))
    {
        if (checkbox.IsChecked == true)
            border.Classes.Add("checked");
        else
            border.Classes.Remove("checked");
    }

    // ...save state...

    // Delayed refresh to allow animation to complete
    await Task.Delay(150);
    Dispatcher.UIThread.Post(() => RefreshTasks(), DispatcherPriority.Background);
}
```

### 6. List Collapse Animation

```csharp
private void BuildTaskList()
{
    foreach (var listName in allListNames)
    {
        var isCollapsed = TodoTaskList.IsListCollapsed(listName);
        AddListHeader(listName, isCollapsed);

        // Create container panel for this list's tasks (for collapse animation)
        var tasksPanel = new StackPanel
        {
            Classes = { "listTasks" },
            ClipToBounds = true // Critical for MaxHeight animation
        };
        if (isCollapsed)
        {
            tasksPanel.Classes.Add("collapsed");
        }
        // ...add tasks to panel...
    }
}

private void AddListHeader(string listName, bool isCollapsed = false)
{
    var chevronBtn = new Button
    {
        Content = "▼",
        Classes = { "chevron" }
    };
    if (isCollapsed)
    {
        chevronBtn.Classes.Add("collapsed");
    }
    // ...
}
```

### 7. Inline Input Animation

```csharp
private Border CreateInlineAddField(string listName)
{
    var border = new Border
    {
        Classes = { "inlineInput", "entering" }
    };

    // Trigger entrance animation after render
    Dispatcher.UIThread.Post(() =>
    {
        border.Classes.Remove("entering");
    }, DispatcherPriority.Render);

    // ...create TextBox...
    return border;
}
```

## How It Works

### Animation Timing

| Animation | Duration | Easing | Purpose |
|-----------|----------|--------|---------|
| Popup open/close | 150ms | CubicEaseOut | Smooth appearance |
| List collapse/expand | 200ms | CubicEaseInOut | Accordion effect |
| Chevron rotation | 200ms | CubicEaseInOut | Visual indicator |
| Task add | 150ms | CubicEaseOut | Show origin of new item |
| Task delete | 150ms | CubicEaseOut | Slide-out dismissal |
| Checkbox toggle | 150ms | CubicEaseOut | Subtle feedback |
| Inline input | 150ms | CubicEaseOut | Smooth appearance |

### Class-Based Transitions

The pattern uses CSS-like class toggling:

1. **Define base state** with `Transitions` property
2. **Define target state** with additional class selector
3. **Toggle class** in code to trigger animation
4. **Use `Dispatcher.UIThread.Post`** to trigger after render

### Critical Implementation Details

1. **`ClipToBounds = true`**: Required on StackPanel containers for MaxHeight animation to clip content during collapse

2. **`DispatcherPriority.Render`**: Use this priority when adding/removing classes to trigger animations after the element is rendered

3. **Async methods for exit animations**: Use `async` with `Task.Delay` to wait for animation before removing elements

4. **Track elements for animations**: Use dictionaries like `_taskBorders` to reference elements when triggering animations programmatically

## Key Patterns Used

### Entrance Animation Pattern

```csharp
// 1. Add "entering" class on creation (starts in hidden state)
border.Classes.Add("entering");

// 2. Post to UI thread to remove class after render (triggers animation)
Dispatcher.UIThread.Post(() =>
{
    border.Classes.Remove("entering");
}, DispatcherPriority.Render);
```

### Exit Animation Pattern

```csharp
// 1. Add exit class to trigger animation
border.Classes.Add("exiting");

// 2. Wait for animation to complete
await Task.Delay(150);

// 3. Remove element from DOM/data
```

### Avalonia Transition Types

- `DoubleTransition`: For numeric properties (Opacity, MaxHeight)
- `TransformOperationsTransition`: For transforms (translateX, translateY, rotate)

## Testing Checklist

- [ ] Click tray icon -> popup fades in smoothly
- [ ] Click outside -> popup fades out smoothly
- [ ] Press Escape -> popup fades out smoothly
- [ ] Click chevron -> list collapses with animation
- [ ] Click chevron again -> list expands with animation
- [ ] Chevron rotates smoothly during collapse/expand
- [ ] Click "+" on list -> input slides in
- [ ] Add task -> new task slides in from top
- [ ] Delete task -> task slides out to right
- [ ] Check task -> task dims smoothly
- [ ] Uncheck task -> task brightens smoothly
- [ ] Animations don't block user interaction
- [ ] No visible jank or stuttering

## Related Documentation

- [Collapsible Lists](./collapsible-lists-tray.md) - Related feature with chevron animation
- [Animations Plan](../../plans/2026-02-04-feat-tray-animations-transitions-plan.md) - Original implementation plan
- [Avalonia Transitions](https://docs.avaloniaui.net/docs/animations/transitions) - Official documentation
