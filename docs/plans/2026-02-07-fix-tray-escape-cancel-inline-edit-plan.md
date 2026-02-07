---
title: "fix: Escape key not cancelling inline edit in TaskerTray"
type: fix
date: 2026-02-07
---

# fix: Escape key not cancelling inline edit in TaskerTray

## Overview

Pressing Escape while in an inline edit field (rename, add task, list rename, create list) in the TaskerTray popup hides the entire popup instead of cancelling the edit. The fix is to mark the Escape `KeyEventArgs` as handled and rebuild the task list UI.

## Problem Statement / Motivation

When a user presses Escape during inline editing in the tray popup, they expect to cancel the edit and return to the normal task view. Instead, the popup disappears entirely, forcing them to reopen it. This makes the rename/add workflow frustrating.

## Root Cause

The bug is a **missing `e.Handled = true`** combined with a **missing `BuildTaskList()` call** in the TextBox `KeyDown` handlers for Escape.

**Event flow when Escape is pressed during inline edit:**

1. TextBox's `KeyDown` fires first (bubbling order: child → parent)
2. `CancelInlineEdit()` runs — sets `_activeInlineEditor = null`, `_editingTaskId = null`
3. Event is **not** marked as handled → bubbles up to window
4. Window's `OnKeyDown` (line 158) checks `e.Key == Key.Escape`
5. Checks `_activeInlineEditor != null` → **false** (already cleared in step 2)
6. Falls through to `HideWithAnimation()` → **popup disappears**

**Affected methods (4 total):**

| Method | Line | Missing `e.Handled` | Missing `BuildTaskList()` |
|--------|------|---------------------|--------------------------|
| `CreateInlineEditField` (task rename) | 849 | Yes | Yes |
| `CreateInlineAddField` (task add) | 774 | Yes | Yes |
| `CreateInlineListRenameField` (list rename) | 700 | Yes | No (has it) |
| `CreateInlineListNameField` (create list) | 953 | Yes | No (has it) |

**Comparison with working methods:** `CreateInlineListRenameField` and `CreateInlineListNameField` already call `BuildTaskList()` after cancel — but even they are missing `e.Handled = true`, so they also hide the popup.

## Proposed Solution

Add `e.Handled = true` to all four Escape handlers, and add `BuildTaskList()` to the two that are missing it.

## Technical Approach

### Fix 1: `CreateInlineEditField` (task rename) — line 849

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

### Fix 2: `CreateInlineAddField` (task add) — line 774

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

### Fix 3: `CreateInlineListRenameField` (list rename) — line 700

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

### Fix 4: `CreateInlineListNameField` (create list) — line 953

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

### Note on `LostFocus` interaction

When `CancelInlineEdit()` clears `_activeInlineEditor` and `BuildTaskList()` rebuilds the panel, the old TextBox is removed from the visual tree, triggering its `LostFocus` handler. In `CreateInlineEditField`, the `LostFocus` handler calls `SubmitInlineEdit` if text is non-empty — which would submit the rename we're trying to cancel. However, `SubmitInlineEdit` checks `_editingTaskId` which is already `null` (cleared by `CancelInlineEdit`), so it won't find the task to rename. For `CreateInlineAddField` and `CreateInlineListNameField`, the `submitted` flag (set before `CancelInlineEdit()`) prevents `LostFocus` from double-submitting.

## Acceptance Criteria

- [x] Pressing Escape during task rename cancels the edit, popup stays open
- [x] Pressing Escape during task add cancels the add, popup stays open
- [x] Pressing Escape during list rename cancels the rename, popup stays open
- [x] Pressing Escape during create list cancels the creation, popup stays open
- [x] Pressing Escape when not editing still hides the popup (existing behavior preserved)
- [x] The `LostFocus` handler does not submit the cancelled edit

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add `e.Handled = true` + `BuildTaskList()` to 4 Escape handlers |

## Tests to Write

This is a UI event-handling bug in Avalonia — the fix is mechanical and well-scoped. Manual testing is appropriate:

1. Open TaskerTray popup
2. Right-click a task → Edit → press Escape → verify popup stays, edit is cancelled
3. Click "+" to add task → press Escape → verify popup stays, add is cancelled
4. Right-click list header → Rename list → press Escape → verify popup stays, rename is cancelled
5. Click "New List" → press Escape → verify popup stays, creation is cancelled
6. With no editor active, press Escape → verify popup hides (existing behavior)

## References

- `TaskListPopup.axaml.cs:158-177` — Window-level `OnKeyDown` Escape handling
- `TaskListPopup.axaml.cs:842-853` — Task rename TextBox KeyDown (primary bug)
- `TaskListPopup.axaml.cs:763-778` — Task add TextBox KeyDown
- `TaskListPopup.axaml.cs:689-706` — List rename TextBox KeyDown
- `TaskListPopup.axaml.cs:942-958` — Create list TextBox KeyDown
- `TaskListPopup.axaml.cs:1066-1073` — `CancelInlineEdit()` method
