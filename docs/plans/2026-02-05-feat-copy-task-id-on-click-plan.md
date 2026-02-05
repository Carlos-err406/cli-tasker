---
title: feat: Copy task ID to clipboard when clicking task in TaskerTray
type: feat
date: 2026-02-05
---

# Copy Task ID to Clipboard on Click

## Problem

Users want a quick way to get a task's ID for use with CLI commands. Currently they have to visually find and manually type the ID.

## Solution

When clicking on a task item (not the checkbox or menu), copy the task ID to the clipboard and show brief feedback in the status bar.

## Acceptance Criteria

- [ ] Clicking on a task copies its ID to the clipboard
- [ ] Status bar shows confirmation (e.g., "Copied: abc")
- [ ] Checkbox clicks still toggle completion (not copy)
- [ ] Menu button clicks still open menu (not copy)

## Implementation

### TaskListPopup.axaml.cs

Add `PointerPressed` handler to the task border:

```csharp
// In CreateTaskItem()
border.PointerPressed += async (sender, e) =>
{
    // Only handle left click on the border itself (not bubbled from checkbox/menu)
    if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
    {
        await CopyTaskIdToClipboard(task.Id);
    }
};

private async Task CopyTaskIdToClipboard(string taskId)
{
    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    if (clipboard != null)
    {
        await clipboard.SetTextAsync(taskId);
        StatusText.Text = $"Copied: {taskId}";
    }
}
```

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add click handler to task border, add clipboard helper |
