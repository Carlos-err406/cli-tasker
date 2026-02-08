---
title: Avalonia CheckBox Internal Padding Prevents Centering of Adjacent Elements
category: ui-bugs
tags:
  - avalonia
  - checkbox
  - layout
  - centering
  - padding
  - tray
module: TaskerTray
symptoms:
  - Element below or beside CheckBox appears offset to the right
  - HorizontalAlignment.Center on sibling elements doesn't visually center under the check square
  - CheckBox takes up more width than the visible check indicator
date_solved: 2026-02-07
files_changed:
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
---

# Avalonia CheckBox Internal Padding Prevents Centering

## Problem

When placing a TextBlock below a CheckBox in a vertical StackPanel with `HorizontalAlignment.Center`, the text appeared offset to the right instead of centered under the visible check square.

## Root Cause

Avalonia's CheckBox control has internal padding and a `MinWidth` for its content/label area, even when no content is set. The visible check square is on the left, but the control's actual bounds extend further right to accommodate label text. When sibling elements center within the StackPanel, they center relative to the full CheckBox width (including invisible label space), not the visible square.

## Solution

Strip the CheckBox's internal padding and minimum width:

```csharp
var checkbox = new CheckBox
{
    Padding = new Thickness(0),   // Remove internal content padding
    MinWidth = 0,                 // Remove minimum width for label area
    Margin = new Thickness(0),    // No external margin (put on parent instead)
    // ...
};
```

Then place the right margin on the parent container instead:

```csharp
var checkboxColumn = new StackPanel
{
    Margin = new Thickness(0, 0, 10, 0),  // Spacing to next column
    HorizontalAlignment = HorizontalAlignment.Center
};
checkboxColumn.Children.Add(checkbox);
checkboxColumn.Children.Add(idLabel);  // Now centers correctly
```

## Key Insight

When centering elements relative to an Avalonia CheckBox, always set `Padding = 0` and `MinWidth = 0` on the CheckBox. Its default styling reserves space for a label that may not exist.

## Cross-References

- [Avalonia TextBox Swallows Enter Key](./avalonia-textbox-swallows-enter-key.md) — another Avalonia control internal behavior gotcha
- [Avalonia TextBox KeyDown Event Bubbling](./avalonia-textbox-keydown-event-bubbling.md) — event routing gotcha
