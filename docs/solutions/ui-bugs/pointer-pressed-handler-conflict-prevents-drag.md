---
title: Task dragging broken on text area after removing drag handles
category: ui-bugs
tags: [avalonia, event-handling, pointer-events, drag-drop, handler-conflict, registration-order]
module: TaskerTray
date: 2026-02-05
severity: high
symptoms:
  - Task drag-and-drop only works in empty space, not on text area
  - Dragging by task description fails silently
  - List header dragging works but task dragging does not
root_cause: >
  Two PointerPressed handlers on the same Border element conflicted.
  The clipboard-copy handler (registered first) set e.Handled = true,
  preventing the drag handler (registered second) from firing in the
  content area.
related:
  - docs/solutions/ui-bugs/avalonia-drag-drop-unreliable-file-watcher-refresh.md
---

# Task Dragging Broken on Text Area After Removing Drag Handles

## Problem

After removing grip handle dots from task items (to make the entire row draggable), task dragging only worked in empty padding areas of the border — not on the text content area. List header dragging worked fine.

## Investigation

The task `Border` had two `PointerPressed` handlers:

1. **Clipboard handler** (registered first, line ~1062) — copies task ID on click in the content area
2. **Drag handler** (registered second, via `SetupTaskDragHandlers`) — sets up pending drag state

The clipboard handler checked if the click was in the content area (`pos.X > checkboxWidth && pos.X < borderWidth - menuWidth`) and set `e.Handled = true`. In Avalonia, once an event is marked `Handled`, subsequent handlers registered with default `handledEventsToo: false` are skipped — even on the same element.

List headers worked because they had no clipboard handler — only the drag handler.

## Root Cause

**Event handler registration order + `e.Handled` conflict.** When two handlers compete for the same `PointerPressed` event on the same element, the first handler's `e.Handled = true` blocks the second handler from firing.

## Solution

Separate the two interactions by event type:

- **`PointerPressed`** → always sets up pending drag (registered first)
- **`PointerReleased`** → copies task ID to clipboard, but only if no drag occurred

```csharp
// 1. Register drag handler FIRST
SetupTaskDragHandlers(border, task);

// 2. Register clipboard handler on PointerReleased (not PointerPressed)
border.PointerReleased += async (sender, e) =>
{
    // Skip if a drag just occurred
    if (_isDragging || _state == PopupState.DroppingInProgress) return;

    var pos = e.GetPosition(border);
    var checkboxWidth = 40;
    var menuWidth = 40;
    var borderWidth = border.Bounds.Width;

    if (pos.X > checkboxWidth && pos.X < borderWidth - menuWidth)
    {
        await CopyTaskIdToClipboard(task.Id);
    }
};
```

## Key Lesson

**When migrating from handle-based drag to container-based drag, audit all existing event handlers on the container.** Grip handles had their own isolated `PointerPressed` — moving drag to the parent `Border` creates conflicts with any other `PointerPressed` handlers already registered there.

**General rule for Avalonia:** If two interactions share the same element, use different event phases:
- `PointerPressed` for drag setup (needs immediate response)
- `PointerReleased` for click actions (only if drag didn't happen)

## Prevention

- When attaching multiple pointer handlers to the same element, verify they don't conflict via `e.Handled`
- Prefer separating drag (press) from click actions (release) on draggable elements
- Test drag from all visual areas of the element, not just empty space
