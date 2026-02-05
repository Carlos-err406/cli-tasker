---
title: Avalonia drag-and-drop unreliable - pending drags invalidated by file watcher refreshes
category: ui-bugs
tags: [avalonia, drag-drop, macos, file-watcher, event-handling, pointer-capture, generation-counter, state-machine]
module: TaskerTray
symptom: Drag operations would not start consistently, requiring multiple attempts to initiate a drag
root_cause: File watcher refreshes invalidated event handlers during the "pending drag" phase (mouse pressed but movement threshold not yet reached), combined with grip handle hit area being too small (12px)
date: 2026-02-05
---

# Avalonia Drag-and-Drop Unreliable - File Watcher Refresh During Pending Drag

## Problem

Drag-and-drop in TaskerTray wouldn't start reliably. Users had to attempt multiple drag operations before one would actually initiate. The drag gesture was inconsistent and frustrating to use.

## Symptoms

- Clicking on grip handle and moving mouse didn't start drag
- Required multiple attempts to initiate a drag
- No visual feedback when press was registered
- Problem appeared intermittently

## Root Cause

Three interconnected issues caused the unreliability:

### 1. Refresh Protection Gap

The generation ID pattern was designed to invalidate stale event handlers when the UI rebuilds. However, it only protected *active* drags (`_state == PopupState.Dragging`), not *pending* drags.

A **pending drag** occurs when the user has pressed down on the grip handle but hasn't moved past the 5-pixel threshold yet. If a file watcher refresh triggered during this window, the pending drag state would be wiped out.

```
Timeline of failure:
1. User presses grip handle → _dragStartPoint set, pending drag begins
2. File watcher detects external change
3. RefreshTasks() called → only checks for active drag (not pending)
4. BuildTaskList() called → _generationId incremented, all handlers invalidated
5. User moves mouse past threshold → handler check fails (stale generation)
6. Drag silently fails to start
```

### 2. Small Hit Area

The grip handle (6-dot pattern) was implemented as a 12px `StackPanel`. This made the clickable area too small and required precise targeting.

### 3. No Press Feedback

When users pressed on the grip handle, there was no visual acknowledgment. Without feedback, users couldn't tell if their press registered.

## Solution

### 1. Extend Refresh Protection to Pending Drags

Check for `_dragStartPoint.HasValue` in addition to active drag states:

```csharp
public void RefreshTasks()
{
    // Queue refresh if drag operation is in progress OR pending
    if (_state == PopupState.Dragging ||
        _state == PopupState.DroppingInProgress ||
        _dragStartPoint.HasValue)  // NEW: protect pending drags
    {
        _state = PopupState.RefreshPending;
        return;
    }
    DoRefreshTasks();
}
```

### 2. Increase Grip Handle Hit Area

Wrap the visual dots in a larger transparent container:

```csharp
private Panel CreateGripHandle()
{
    var outerPanel = new Panel
    {
        Width = 20,  // Was 12px - now larger hit area
        MinHeight = 24,
        Background = Brushes.Transparent,  // Still captures input
        Margin = new Thickness(0, 0, 4, 0),
        Classes = { "gripHandle" }
    };

    // Inner visual dots panel
    var dotsPanel = new StackPanel { /* ... dots ... */ };
    outerPanel.Children.Add(dotsPanel);
    return outerPanel;
}
```

### 3. Add Immediate Visual Feedback

Highlight grip dots immediately on press to confirm registration:

```csharp
gripHandle.PointerPressed += (sender, e) =>
{
    // Visual feedback - highlight dots while pressed
    foreach (var dot in gripHandle.GetVisualDescendants()
        .OfType<Border>()
        .Where(b => b.Classes.Contains("gripDot")))
    {
        dot.Background = new SolidColorBrush(Color.Parse("#0A84FF"));
    }

    // Store reference for cleanup
    _pendingGripHandle = gripHandle;
    // ... rest of handler
};
```

### 4. Proper Cleanup with Visual Reset

Reset grip handle color when pending drag is cleared:

```csharp
private void ClearPendingDrag()
{
    // Reset grip handle color
    if (_pendingGripHandle != null)
    {
        foreach (var dot in _pendingGripHandle.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("gripDot")))
        {
            dot.Background = new SolidColorBrush(Color.Parse("#666"));
        }
    }

    _dragStartPoint = null;
    _pendingGripHandle = null;

    // Process queued refresh if needed
    if (_state == PopupState.RefreshPending && !_isDragging)
    {
        _state = PopupState.Idle;
        DoRefreshTasks();
    }
}
```

## Prevention Strategies

### The Three Laws of Dynamic UI Event Handling

1. **Every dynamically attached handler must capture and check a generation counter**
2. **State machines must protect ALL phases, including pending/pre-threshold states**
3. **External data changes must be suspended or queued during multi-event operations**

### Checklist for Future Avalonia Drag-Drop

- [ ] Generation counter incremented on every UI rebuild
- [ ] Handler captures generation ID in closure
- [ ] Handler validates generation before executing
- [ ] State machine covers pending, active, and completing phases
- [ ] RefreshUI() checks for pending operations, not just active
- [ ] Hit targets are at least 20x24px
- [ ] Visual feedback on press (not just hover)
- [ ] Cleanup resets all visual state
- [ ] File watcher suspended during operations

### Hit Target Guidelines

| Element Type | Minimum Size |
|--------------|--------------|
| Touch targets | 44x44 pts |
| Mouse targets | 24x24 pts |
| Grip handles | 20x24 pts |

## Related Documentation

- [List Duplication on Inline Add](../ui-bugs/list-duplication-on-inline-add.md) - Same generation counter pattern
- [Animations and Transitions](../feature-implementations/tray-animations-transitions.md) - CSS transition patterns
- [Collapsible Lists](../feature-implementations/collapsible-lists-tray.md) - State persistence

## Files Modified

- `src/TaskerTray/Views/TaskListPopup.axaml.cs` - Core fix implementation
- `src/TaskerTray/Views/TaskListPopup.axaml` - Hover indicator styles

## Key Insight

The critical oversight was only protecting **active** operations. The generation counter pattern was already in place and working correctly for active drags. The bug existed in the gap between "user pressed" and "drag threshold crossed" - a window of only 5 pixels of mouse movement, but long enough for a file watcher event to slip through.

**Always protect pending states, not just active states.**
