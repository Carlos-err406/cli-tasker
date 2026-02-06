---
title: Hide irrelevant list controls when viewing a single filtered list
category: ui-bugs
tags: [avalonia, ux, tray, list-header, conditional-ui]
module: TaskerTray
date: 2026-02-06
severity: low
symptoms:
  - Collapse chevron shown when only one list is visible
  - List drag reorder cursor appears when there's nothing to reorder
  - Unnecessary UI clutter in filtered single-list view
root_cause: >
  AddListHeader rendered the same controls (chevron, drag handlers) regardless
  of whether the user was viewing all lists or a single filtered list. The chevron
  and reorder drag are only meaningful when multiple lists are shown.
related:
  - docs/solutions/ui-bugs/cursor-inheritance-on-interactive-children.md
---

# Hide Irrelevant List Controls in Filtered View

## Problem

When a user selects a specific list from the dropdown filter, the list header still shows the collapse chevron and enables drag-to-reorder. Both are useless in single-list view — there's nothing to collapse into and nothing to reorder against.

## Solution

Add a `showChevron` flag based on `_currentListFilter` and gate the chevron and drag setup:

```csharp
var canReorder = allListNames.Count > 1 && _currentListFilter == null;
var showChevron = _currentListFilter == null;

// Adjust grid columns: skip chevron column when filtered
string columnDef;
if (showChevron)
    columnDef = isDefaultList ? "Auto,*,Auto" : "Auto,*,Auto,Auto";
else
    columnDef = isDefaultList ? "*,Auto" : "*,Auto,Auto";

// Use colOffset = -1 when chevron is absent so existing Grid.SetColumn math works
var colOffset = showChevron ? 0 : -1;

if (canReorder)
{
    headerBorder.Cursor = new Cursor(StandardCursorType.DragMove);
    SetupListDragHandlers(headerBorder, listName);
}

if (showChevron)
{
    // ... chevron button creation and Grid.SetColumn(chevronBtn, colOffset) ...
}
```

The `colOffset = -1` trick means all downstream `Grid.SetColumn(control, colOffset + N)` calls produce the correct column index without changing any other code.

Add left margin to the list title when the chevron is absent to prevent it from sitting flush left:

```csharp
if (!showChevron) headerStack.Margin = new Thickness(4, 0, 0, 0);
```

## Prevention

When building conditional UI in Avalonia code-behind, use a column offset variable rather than duplicating the entire header builder. Gate optional controls with a flag and adjust the grid definition accordingly.
