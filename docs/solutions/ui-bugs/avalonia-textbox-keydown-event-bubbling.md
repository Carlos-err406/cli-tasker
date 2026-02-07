---
title: avalonia-textbox-keydown-event-bubbling
category: ui-bugs
tags:
  - keyboard-handling
  - event-propagation
  - inline-editing
  - tasker-tray
  - avalonia
severity: high
module: TaskerTray
symptoms:
  - Pressing Escape in inline edit field closes entire popup instead of cancelling edit
  - Escape during task rename hides popup window
  - Escape during task add hides popup window
  - Escape during list rename hides popup window
  - Escape during create list hides popup window
date_solved: 2026-02-07
---

# Avalonia TextBox KeyDown Event Bubbling Hides Popup

## Problem

In TaskerTray, pressing Escape while in any inline edit field (task rename, task add, list rename, create list) hid the entire popup instead of cancelling the edit. The user had to reopen the tray popup after every cancelled edit.

## Root Cause

Avalonia uses **bubbling** event routing for `KeyDown` — child handlers fire before parent handlers. The bug was a two-part failure:

1. **Missing `e.Handled = true`** in all four TextBox `KeyDown` Escape handlers
2. **Missing `BuildTaskList()`** in two of the four handlers (task rename, task add)

### Event flow (buggy):

```
TextBox.KeyDown (Escape)
  → CancelInlineEdit()          ← sets _activeInlineEditor = null
  → e.Handled NOT set           ← event continues bubbling
  ↓
Window.OnKeyDown (Escape)
  → if (_activeInlineEditor != null)  ← FALSE (already null!)
  → falls through to HideWithAnimation()  ← popup disappears
```

The shared state (`_activeInlineEditor`) was cleared by the child handler before the parent handler could check it. Without `e.Handled = true`, the parent received the event and made an incorrect decision based on stale state.

## Solution

Add `e.Handled = true` to all four TextBox Escape handlers, and add `BuildTaskList()` to the two missing it.

### Pattern (all 4 handlers now follow this):

```csharp
else if (e.Key == Key.Escape)
{
    e.Handled = true;       // FIRST: stop bubbling before clearing state
    submitted = true;        // prevent LostFocus from submitting
    CancelInlineEdit();      // clear shared state
    BuildTaskList();         // rebuild UI to remove edit field
}
```

### Affected methods in `TaskListPopup.axaml.cs`:

| Method | Fix |
|--------|-----|
| `CreateInlineEditField` (task rename) | Added `e.Handled`, `BuildTaskList()`, LostFocus guard |
| `CreateInlineAddField` (task add) | Added `e.Handled`, `BuildTaskList()` |
| `CreateInlineListRenameField` (list rename) | Added `e.Handled` (already had `BuildTaskList()`) |
| `CreateInlineListNameField` (create list) | Added `e.Handled` (already had `BuildTaskList()`) |

### LostFocus guard (task rename only):

When `BuildTaskList()` removes the TextBox from the visual tree, `LostFocus` fires. The rename handler's `LostFocus` would call `SubmitInlineEdit` if text was non-empty — submitting the edit we're trying to cancel. Added a guard:

```csharp
textBox.LostFocus += (_, _) =>
{
    if (_editingTaskId == null) return;  // Already cancelled by Escape
    // ... rest of handler
};
```

The add and list handlers already had `submitted` flag guards that prevented this.

## Prevention

When adding new inline editors to `TaskListPopup.axaml.cs`:

1. **Always set `e.Handled = true` before `CancelInlineEdit()`** — prevents event bubbling to the window handler
2. **Always call `BuildTaskList()` after cancel** — removes the edit field from the visual tree
3. **Guard `LostFocus` against already-cancelled state** — removing a TextBox triggers LostFocus
4. **Use `submitted` flag** — prevents double submission from concurrent event paths (LostFocus + Deactivated)

### Code review red flag:

```csharp
// BAD: state cleared before marking handled
CancelInlineEdit();
// e.Handled not set → event bubbles with stale state

// GOOD: mark handled first
e.Handled = true;
CancelInlineEdit();
BuildTaskList();
```

## Related

- `docs/solutions/ui-bugs/task-duplication-on-blur-outside-window.md` — Double submission from LostFocus + Deactivated race
- `docs/solutions/ui-bugs/list-duplication-on-inline-add.md` — Stale LostFocus events causing list duplication
- `docs/solutions/ui-bugs/pointer-pressed-handler-conflict-prevents-drag.md` — Another `e.Handled` conflict between event handlers
- `docs/plans/2026-02-06-feat-tray-undo-redo-keyboard-shortcuts-plan.md` — Keyboard shortcut handling deferring to active TextBox
