---
title: Interactive child elements inherit DragMove cursor from draggable parent
category: ui-bugs
tags: [avalonia, cursor, drag-drop, inheritance, ux]
module: TaskerTray
date: 2026-02-05
severity: low
symptoms:
  - Buttons inside draggable containers show DragMove cursor instead of default arrow
  - Checkboxes show drag cursor on hover
  - Chevron, add, and menu buttons all show wrong cursor
root_cause: >
  Avalonia child elements inherit their parent's Cursor property. Setting
  DragMove on a parent Border causes all child buttons and checkboxes to
  show the drag cursor instead of the default arrow.
related:
  - docs/solutions/ui-bugs/pointer-pressed-handler-conflict-prevents-drag.md
---

# Interactive Child Elements Inherit DragMove Cursor from Draggable Parent

## Problem

After setting `Cursor = DragMove` on draggable task and list header `Border` elements, all child interactive elements (chevron button, add button, ellipsis menu, checkbox) inherited the drag cursor instead of showing the default arrow.

## Root Cause

In Avalonia, `Cursor` is an inherited property. Child controls that don't explicitly set their own `Cursor` inherit from the nearest parent that does.

## Solution

Set `Cursor = Arrow` explicitly on each interactive child element:

```csharp
var chevronBtn = new Button { ..., Cursor = new Cursor(StandardCursorType.Arrow) };
var addBtn = new Button { ..., Cursor = new Cursor(StandardCursorType.Arrow) };
var menuBtn = new Button { ..., Cursor = new Cursor(StandardCursorType.Arrow) };
var checkbox = new CheckBox { ..., Cursor = new Cursor(StandardCursorType.Arrow) };
```

## Prevention

When setting a custom cursor on a container, always override the cursor on interactive child elements that shouldn't inherit it.
