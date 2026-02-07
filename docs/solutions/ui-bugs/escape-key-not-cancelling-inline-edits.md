---
title: Escape Key Not Cancelling Inline Add/Edit in Tray Popup
category: ui-bugs
tags:
  - TaskerTray
  - inline-editing
  - keyboard-events
  - event-bubbling
  - avalonia
module: src/TaskerTray
date: 2026-02-06
severity: high
symptoms:
  - pressing Escape while adding a task leaves the TextBox visible
  - pressing Escape while renaming a task leaves the TextBox visible
  - pressing Escape during inline edit closes the entire popup instead of just cancelling the edit
  - pressing Escape during task rename could submit the edit via LostFocus instead of cancelling it
root_cause: CancelInlineEdit() clears state variables but does not rebuild the UI, and missing e.Handled allows event bubbling to window-level handler
---

# Escape Key Not Cancelling Inline Add/Edit in Tray Popup

## Problem

Pressing Escape while in an inline add or rename TextBox in the tray popup did not properly cancel the operation. Three distinct issues:

1. **TextBox stayed visible** — `CancelInlineEdit()` cleared state variables (`_activeInlineEditor`, `_editingTaskId`, `_addingToList`) but never called `BuildTaskList()` to rebuild the UI without the TextBox.

2. **Escape closed the popup** — Without `e.Handled = true`, the Escape KeyDown event bubbled from the TextBox to the window-level `OnKeyDown` handler. Since `CancelInlineEdit()` already set `_activeInlineEditor = null`, the window handler skipped the "cancel edit" branch and fell through to `HideWithAnimation()`.

3. **Cancelled edit got submitted anyway** — `CreateInlineEditField()` had no `submitted` guard variable. When Escape triggered `CancelInlineEdit()` and focus shifted, the `LostFocus` handler fired and called `SubmitInlineEdit()`, saving the edit the user intended to cancel.

## Root Cause

The codebase had the correct Escape handling pattern in two places (`CreateInlineListRenameField` and `CreateInlineListNameField`) but it was incomplete in two others (`CreateInlineAddField` and `CreateInlineEditField`).

| Method | `e.Handled` | `submitted` guard | `BuildTaskList()` | `capturedShowCount` |
|--------|:-----------:|:------------------:|:------------------:|:-------------------:|
| `CreateInlineListRenameField` | missing | yes | yes | yes |
| `CreateInlineListNameField` | missing | yes | yes | yes |
| `CreateInlineAddField` | missing | yes | **missing** | yes |
| `CreateInlineEditField` | missing | **missing entirely** | **missing** | **missing** |

## Files Changed

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Fixed all 4 inline editor Escape handlers |

## Solution

### 1. Added `BuildTaskList()` and `e.Handled` to `CreateInlineAddField` Escape handler

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;      // NEW: prevent bubble to window handler
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();       // NEW: rebuild UI to remove TextBox
}
```

### 2. Added full guard pattern to `CreateInlineEditField`

This was the worst case — missing all protective patterns:

```csharp
var submitted = false;  // NEW: local flag to prevent double submission

textBox.KeyDown += (_, e) =>
{
    if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
    {
        e.Handled = true;
        if (!submitted)         // NEW: guard
        {
            submitted = true;   // NEW: prevent LostFocus
            SubmitInlineEdit(task.Id, textBox.Text);
        }
    }
    else if (e.Key == Key.Escape)
    {
        e.Handled = true;       // NEW: prevent bubble
        submitted = true;       // NEW: prevent LostFocus from submitting
        CancelInlineEdit();
        BuildTaskList();        // NEW: rebuild UI
    }
};

var capturedShowCount = _showCount;  // NEW: stale session guard
textBox.LostFocus += (_, _) =>
{
    if (capturedShowCount != _showCount)  // NEW: ignore stale events
        return;

    if (submitted)                        // NEW: ignore if already handled
        return;

    submitted = true;
    if (!string.IsNullOrWhiteSpace(textBox.Text))
    {
        SubmitInlineEdit(task.Id, textBox.Text);
    }
    else
    {
        CancelInlineEdit();
        BuildTaskList();
    }
};
```

### 3. Added `e.Handled` to list rename and list name handlers

Both `CreateInlineListRenameField` and `CreateInlineListNameField` already had `BuildTaskList()` and `submitted` guards but were missing `e.Handled = true`, allowing the event to bubble.

## Key Patterns

### The Inline Editor Cancel Pattern

Every `Create*Field()` method should follow this canonical pattern for Escape:

```csharp
e.Handled = true;       // 1. Stop event from bubbling to window handler
submitted = true;       // 2. Prevent LostFocus from submitting
CancelInlineEdit();     // 3. Clear state variables
BuildTaskList();        // 4. Rebuild UI to remove TextBox
```

All four steps are required. `CancelInlineEdit()` alone only clears state — it does not touch the visual tree.

### Why `BuildTaskList()` not `RefreshTasks()`

`RefreshTasks()` reloads data from SQLite then rebuilds. `BuildTaskList()` is UI-only — it reads the state variables (now cleared by `CancelInlineEdit()`) and renders accordingly. Since cancelling doesn't change data, `BuildTaskList()` is correct.

### The Event Bubbling Chain

Without `e.Handled = true`, pressing Escape in a TextBox follows this path:

1. TextBox `KeyDown` fires, calls `CancelInlineEdit()` (sets `_activeInlineEditor = null`)
2. Event bubbles to Window `OnKeyDown`
3. Window checks `_activeInlineEditor != null` — it's null now, skips that branch
4. Falls through to clear search or `HideWithAnimation()` — popup closes

Setting `e.Handled = true` stops the event at step 1.

## Testing

This is a UI-only fix in the Avalonia tray app. Manual verification:
- Escape during inline add dismisses TextBox, popup stays open
- Escape during task rename dismisses TextBox without saving, popup stays open
- Escape during list rename dismisses field, popup stays open
- Escape with no active editor still closes popup (existing behavior preserved)
- Escape with active search still clears search first (existing behavior preserved)

## Related Documentation

- [Task Duplication on Blur Outside Window](task-duplication-on-blur-outside-window.md) — introduced the `_inlineAddSubmitted` and `submitted` patterns
- [List Duplication on Inline Add](list-duplication-on-inline-add.md) — introduced the `_showCount` / `capturedShowCount` pattern
- [Drag Drop Unreliable File Watcher Refresh](avalonia-drag-drop-unreliable-file-watcher-refresh.md) — related generation counter patterns
- [Tray Search Filter](../feature-implementations/tray-search-filter.md) — documents the 3-level Escape handling (editor > search > close)
