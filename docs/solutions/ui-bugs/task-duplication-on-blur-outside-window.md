---
title: Task duplication when clicking outside TaskerTray window
category: ui-bugs
tags: [avalonia, race-condition, lostfocus, deactivated, event-timing, double-submission]
module: TaskerTray
symptom: Tasks created twice with different IDs when clicking outside window to close
root_cause: Two independent submission paths (LostFocus and Deactivated) both fire without coordination
date: 2026-02-05
related: [list-duplication-on-inline-add.md]
---

# Task Duplication When Clicking Outside Window

## Symptom

When creating a task via inline add in TaskerTray:
1. Click "+" to start inline add
2. Type task text
3. Click **outside the window** to close popup
4. Reopen popup
5. **Bug**: The task appears twice with different IDs

## Root Cause

Two independent code paths both create tasks without coordinating:

1. **`TextBox.LostFocus`** → `SubmitInlineAdd()`
2. **`Window.Deactivated`** → `SavePendingInlineAdd()`

When clicking outside the window:
- Both events fire (order varies by timing)
- Each path creates a new task with a unique GUID-based ID
- Result: duplicate tasks

### Why This Happened

The `SavePendingInlineAdd()` method was added to fix a *different* bug (list duplication from delayed LostFocus events - see related doc). It ensured tasks were saved immediately on window deactivation. However, this created a new race condition where both paths could execute.

The local `submitted` flag in `CreateInlineAddField()` only protected against double-submission within that method's closures, but `SavePendingInlineAdd()` is a separate code path that didn't check or set this flag.

## Solution

Use a **class-level flag** that both submission paths check and set.

### 1. Add class-level flag

```csharp
// In field declarations
private bool _inlineAddSubmitted; // Prevents double submission between Deactivated and LostFocus
```

### 2. Reset flag when starting new inline add

```csharp
private void StartInlineAdd(string listName)
{
    CancelInlineEdit();
    _addingToList = listName;
    _inlineAddSubmitted = false; // Reset for new inline add
    // ...
}
```

### 3. Check and set flag in LostFocus handler

```csharp
textBox.LostFocus += (_, _) =>
{
    if (capturedShowCount != _showCount)
        return;

    // Guard against double submission
    if (submitted || _inlineAddSubmitted)
        return;

    submitted = true;
    _inlineAddSubmitted = true;

    if (!string.IsNullOrWhiteSpace(textBox.Text))
        SubmitInlineAdd(textBox.Text, listName);
    else
        CancelInlineEdit();
};
```

### 4. Set flag in SavePendingInlineAdd()

```csharp
private void SavePendingInlineAdd()
{
    if (_activeInlineEditor == null)
        return;

    var text = _activeInlineEditor.Text;
    if (string.IsNullOrWhiteSpace(text))
        return;

    // Mark as submitted to prevent LostFocus from creating duplicate
    _inlineAddSubmitted = true;

    // ... rest of method
}
```

## Why Two Flags?

- **Local `submitted` flag**: Guards within the same closure (KeyDown Enter vs LostFocus)
- **Class-level `_inlineAddSubmitted`**: Guards across different code paths (LostFocus vs Deactivated/SavePendingInlineAdd)

Both are needed because:
1. Local flag is captured in closure - survives across async event timing within that inline add session
2. Class-level flag coordinates between completely separate event handlers

## Prevention

When adding "save on close" behavior to handle delayed events:

1. **Always coordinate with existing save paths** - Don't assume the primary path won't also fire
2. **Use a shared flag** at the appropriate scope (class-level for cross-method coordination)
3. **Reset flags** when starting new operations
4. **Document the coordination** - Comment why the flag exists

### Pattern for Safe Multi-Path Submission

```csharp
private bool _operationSubmitted;

void StartOperation()
{
    _operationSubmitted = false;  // Reset
}

void PathA_Handler()
{
    if (_operationSubmitted) return;
    _operationSubmitted = true;
    DoOperation();
}

void PathB_Handler()
{
    if (_operationSubmitted) return;
    _operationSubmitted = true;
    DoOperation();
}
```

## Related Documentation

- [List Duplication on Inline Add](list-duplication-on-inline-add.md) - The fix that introduced `SavePendingInlineAdd()` and indirectly caused this bug
- [Avalonia Drag-Drop Unreliable](avalonia-drag-drop-unreliable-file-watcher-refresh.md) - Similar pattern of protecting pending operations

## Files Changed

- `src/TaskerTray/Views/TaskListPopup.axaml.cs`
  - Added `_inlineAddSubmitted` field
  - Modified `StartInlineAdd()` to reset flag
  - Modified `CreateInlineAddField()` LostFocus handler to check flag
  - Modified `SavePendingInlineAdd()` to set flag

## Key Insight

When fixing a race condition by adding a new code path, you may create a *new* race condition between the old and new paths. Always consider: "What happens if both the original path AND my new path execute?"
