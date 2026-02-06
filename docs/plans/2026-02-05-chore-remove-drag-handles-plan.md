---
title: Remove drag handles, drag from entire container
type: chore
date: 2026-02-05
task: "c22"
---

# Remove Drag Handles, Drag from Entire Container

## Overview

Remove the 6-dot grip handles from task and list items in TaskerTray. Instead, make the entire container draggable. This saves horizontal space and feels more natural.

## Problem Statement

The grip handles (6-dot pattern) take up ~20px of width on each task and list header. On the narrow TaskerTray popup, this is a significant amount of space. Users expect to drag from anywhere on the item, not just a small handle.

## Proposed Solution

1. Remove `CreateGripHandle()` and its callers
2. Attach drag pointer events to the task/list container `Border` instead of the grip handle
3. Remove XAML styles for grip handle visibility
4. Keep the existing 5px threshold to distinguish drag from click

## Technical Approach

### Files to Modify

| File | Change |
|------|--------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Remove grip handle, attach drag to container |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Remove grip handle visibility styles |

### Changes in `TaskListPopup.axaml.cs`

#### 1. Remove `CreateGripHandle()` (lines 1592-1641)

Delete the entire method.

#### 2. Update task item building

Where the grip handle is added to the task row, remove it. The task `Border` already wraps the content - attach `SetupTaskDragHandlers()` to the task's outer `Border` instead of the grip handle.

#### 3. Update `SetupTaskDragHandlers()` (line 1646)

Change from attaching to grip handle to attaching to the task container border:

```csharp
// Before: grip.PointerPressed += ...
// After: border.PointerPressed += ...
```

The 5px movement threshold already prevents accidental drags when clicking checkboxes or buttons.

#### 4. Update `SetupListDragHandlers()` (line 1885)

Same change for list headers - attach to the list header border instead of grip handle.

#### 5. Remove grip handle hover styling

Remove the accent color hover effect that was specific to grip dots.

### Changes in `TaskListPopup.axaml`

Remove the styles that control grip handle visibility on hover (lines 62-81 for tasks, 122-140 for lists).

### Visual Change

**Before:**
```
⠿ | [x] Task description here    #tag    •••
```

**After:**
```
[x] Task description here    #tag    •••
```

The entire row is now the drag target.

## Cursor Feedback

Change cursor to `Hand` on hover for task/list containers to indicate they're draggable:

```csharp
border.Cursor = new Cursor(StandardCursorType.Hand);
```

## Acceptance Criteria

- [x] Grip handles removed from task items
- [x] Grip handles removed from list headers
- [x] Dragging works from anywhere on a task row
- [x] Dragging works from anywhere on a list header
- [x] 5px threshold still prevents accidental drags when clicking
- [x] Checkbox clicks still work (not intercepted by drag)
- [x] Menu button clicks still work (not intercepted by drag)
- [x] Drag ghost and drop indicator still appear correctly
- [x] Undo/redo for reorder still works
- [x] XAML grip handle styles removed

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| Click checkbox | Toggles check, no drag |
| Click menu button | Opens menu, no drag |
| Click + tiny movement (<5px) | Treated as click, no drag |
| Click + 5px+ movement | Starts drag |
| Drag task to new position | Reorder works as before |
| Drag list header to new position | List reorder works as before |

## References

- Grip handle creation: `TaskListPopup.axaml.cs:1592-1641`
- Task drag handler setup: `TaskListPopup.axaml.cs:1646-1669`
- List drag handler setup: `TaskListPopup.axaml.cs:1885-1908`
- XAML grip styles: `TaskListPopup.axaml:62-81, 122-140`
- Drag state machine: `TaskListPopup.axaml.cs:26-27`
