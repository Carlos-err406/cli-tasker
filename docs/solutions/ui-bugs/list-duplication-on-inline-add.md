# List Duplication When Adding Task via Desktop UI

## Metadata
- **Category**: ui-bugs
- **Module**: TaskerTray
- **Tags**: avalonia, race-condition, lostfocus, event-timing
- **Date**: 2026-02-04

## Symptom

When adding a task through the TaskerTray desktop popup UI:
1. Click "+" to start inline add
2. Type task text
3. Click **outside the window** to close popup
4. Reopen popup
5. **Bug**: The entire list structure appears duplicated (e.g., `listA, listB, listA, listB`)

The duplication only occurred when clicking outside the window, not when clicking inside the window but outside the textbox.

## Root Cause

**Delayed `LostFocus` event causing race condition with popup reopen.**

The `LostFocus` event on the inline TextBox was being dispatched asynchronously by Avalonia. When the user clicked outside the popup window:

1. `Deactivated` event fires → window hides
2. User clicks tray icon to reopen (~1 second later)
3. `ShowAtPosition` is called → increments show count, calls `RefreshTasks()`
4. `BuildTaskList()` starts clearing and rebuilding UI
5. **THEN** the delayed `LostFocus` event fires → calls `SubmitInlineAdd()` → calls `RefreshTasks()` again
6. Two `BuildTaskList()` calls interleave, resulting in 40 children instead of 20

Debug log showing the interleaving:
```
11:13:46.651 [BuildTaskList] Called. Children count before clear: 20
11:13:46.677 [RefreshTasks] Called.    <-- Second call while first BuildTaskList running
11:13:46.677 [BuildTaskList] Called. Children count before clear: 0
11:13:46.691 [BuildTaskList] Done. Final children count: 20
11:13:46.692 [BuildTaskList] After clear: 20   <-- First call clears AFTER second added children
11:13:46.696 [BuildTaskList] Done. Final children count: 40  <-- Duplicated!
```

## Solution

Two-part fix in `TaskListPopup.axaml.cs`:

### 1. Track show count to detect stale events

```csharp
private int _showCount; // Incremented each time window is shown

public void ShowAtPosition(PixelPoint position)
{
    _showCount++; // Invalidate pending LostFocus handlers from previous show
    // ...
}
```

### 2. Capture show count in LostFocus handler closure

```csharp
// In CreateInlineAddField():
var capturedShowCount = _showCount;
textBox.LostFocus += (_, _) =>
{
    // Ignore if this event is from a previous show
    if (capturedShowCount != _showCount)
        return;

    if (!string.IsNullOrWhiteSpace(textBox.Text))
        SubmitInlineAdd(textBox.Text, listName);
    else
        CancelInlineEdit();
};
```

### 3. Save pending task in Deactivated handler

Since `LostFocus` may fire too late (after reopen), save the task immediately when window deactivates:

```csharp
Deactivated += (_, _) =>
{
    SavePendingInlineAdd(); // Save before LostFocus can fire
    CancelInlineEdit();
    Hide();
};

private void SavePendingInlineAdd()
{
    if (_addingToList == null || _activeInlineEditor == null)
        return;

    var text = _activeInlineEditor.Text;
    if (string.IsNullOrWhiteSpace(text))
        return;

    try
    {
        var task = TodoTask.CreateTodoTask(text.Trim(), _addingToList);
        var taskList = new TodoTaskList(_addingToList);
        taskList.AddTodoTask(task);
    }
    catch { }
}
```

## Prevention

When working with Avalonia/WPF event handlers that capture state:

1. **Beware of delayed focus events** - `LostFocus` can fire asynchronously after the control is removed from visual tree
2. **Use generation/epoch counters** to detect stale event handlers from previous UI states
3. **Save state synchronously** in window lifecycle events (`Deactivated`, `Closing`) rather than relying on control events
4. **Add debug logging** when investigating timing issues - log timestamps and state to identify interleaving

## Files Changed

- `src/TaskerTray/Views/TaskListPopup.axaml.cs`
  - Added `_showCount` field
  - Modified `CreateInlineAddField()` to capture and check show count
  - Added `SavePendingInlineAdd()` method
  - Modified `Deactivated` handler to save pending task
  - Modified `ShowAtPosition()` to increment show count
