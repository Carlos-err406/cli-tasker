---
title: Reset cursor to default on interactive elements
type: chore
date: 2026-02-05
task: "29c"
---

# Reset Cursor to Default on Interactive Elements

## Overview

After setting `DragMove` cursor on draggable task and list containers, child interactive elements (chevron, add button, ellipsis menu) inherit the drag cursor. These elements should show the default arrow cursor to indicate they're clickable, not draggable.

## Problem Statement

When hovering over buttons inside draggable containers, the cursor shows `DragMove` instead of the default arrow. This confuses users — clicking a button shouldn't suggest a drag operation.

## Proposed Solution

Set `Cursor = new Cursor(StandardCursorType.Arrow)` on each interactive child element to override the parent's `DragMove` cursor.

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add `Cursor = Arrow` to 4 button elements |

## Changes

### 1. Chevron button (list collapse/expand) — line ~392

```csharp
var chevronBtn = new Button
{
    // ... existing properties ...
    Cursor = new Cursor(StandardCursorType.Arrow)
};
```

### 2. Add task button (+) — line ~449

```csharp
var addBtn = new Button
{
    // ... existing properties ...
    Cursor = new Cursor(StandardCursorType.Arrow)
};
```

### 3. List menu button (•••) — line ~469

```csharp
var menuBtn = new Button
{
    // ... existing properties ...
    Cursor = new Cursor(StandardCursorType.Arrow)
};
```

### 4. Task menu button (•••) — line ~1209

```csharp
var menuBtn = new Button
{
    // ... existing properties ...
    Cursor = new Cursor(StandardCursorType.Arrow)
};
```

## Acceptance Criteria

- [x] Chevron button shows default arrow cursor on hover
- [x] Add task (+) button shows default arrow cursor on hover
- [x] List menu (•••) shows default arrow cursor on hover
- [x] Task menu (•••) shows default arrow cursor on hover
- [x] Draggable areas (rest of the row) still show DragMove cursor
- [x] Checkbox shows default cursor (already inherited or native behavior)

## References

- Drag cursor setup: `TaskListPopup.axaml.cs:387` (lists), `TaskListPopup.axaml.cs:1070` (tasks)
- Chevron: `TaskListPopup.axaml.cs:392`
- Add button: `TaskListPopup.axaml.cs:449`
- List menu: `TaskListPopup.axaml.cs:469`
- Task menu: `TaskListPopup.axaml.cs:1209`
