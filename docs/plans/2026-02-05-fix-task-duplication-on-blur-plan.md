---
title: Fix task duplication when clicking outside tray window
type: fix
date: 2026-02-05
---

# Fix Task Duplication on Blur Outside Window

When creating a task via inline add and clicking outside the TaskerTray window to close it, the task is created twice with different IDs.

## Acceptance Criteria

- [ ] Tasks are created only once when clicking outside window to close
- [ ] Tasks are created only once when pressing Enter
- [ ] Tasks are created only once when pressing Escape (should cancel, not create)
- [ ] Clicking inside window but outside textbox still works correctly

## Root Cause

Two independent submission paths fire without coordination:

1. **`TextBox.LostFocus`** → `SubmitInlineAdd()` (line ~576)
2. **`Window.Deactivated`** → `SavePendingInlineAdd()` (line ~47)

When clicking outside the window, both events fire and each creates a task.

## Solution

Apply the `submitted` flag pattern already used in `CreateInlineListNameField()` to `CreateInlineAddField()`.

### CreateInlineAddField() fix

```csharp
private Border CreateInlineAddField(string listName)
{
    // ... existing setup code ...

    var submitted = false;  // ADD: Local flag to prevent double submission

    textBox.KeyDown += (_, e) =>
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (!submitted)  // ADD: Guard
            {
                submitted = true;
                SubmitInlineAdd(textBox.Text, listName);
            }
        }
        else if (e.Key == Key.Escape)
        {
            submitted = true;  // ADD: Mark as handled
            CancelInlineEdit();
        }
    };

    var capturedShowCount = _showCount;
    textBox.LostFocus += (_, _) =>
    {
        if (capturedShowCount != _showCount)
            return;

        if (submitted)  // ADD: Guard against double submission
            return;

        submitted = true;  // ADD: Mark as submitted
        if (!string.IsNullOrWhiteSpace(textBox.Text))
        {
            SubmitInlineAdd(textBox.Text, listName);
        }
        else
        {
            CancelInlineEdit();
        }
    };

    // ... rest of method ...
}
```

### SavePendingInlineAdd() - already protected

The `SavePendingInlineAdd()` method checks `_activeInlineEditor != null` before proceeding. Since `SubmitInlineAdd()` calls `CancelInlineEdit()` which sets `_activeInlineEditor = null`, subsequent calls to `SavePendingInlineAdd()` will early-return.

However, there's a timing edge case where both could read non-null before either clears it. The `submitted` flag in the closure handles this.

## References

- Related solution: `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`
- Pattern source: `CreateInlineListNameField()` lines 718-755
- Files to modify: `src/TaskerTray/Views/TaskListPopup.axaml.cs`
