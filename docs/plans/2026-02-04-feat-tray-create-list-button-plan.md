---
title: "feat: Add Create List Button to TaskerTray"
type: feat
date: 2026-02-04
---

# Add Create List Button to TaskerTray

## Overview

Add a "+" button in the TaskerTray popup header (next to the list filter dropdown) that allows users to create new lists directly from the tray app without needing the CLI.

## Problem Statement / Motivation

Currently, users must use the CLI (`tasker lists create <name>`) to create new lists. The recent refactoring (v2.7.0) added support for empty lists as first-class entities, but the tray app has no way to create them. This forces users to context-switch to the terminal for a simple operation.

## Proposed Solution

Add a "+" button in the header bar next to the list filter dropdown. When clicked, it shows an inline input field at the top of the task list area for entering the new list name. The pattern follows the existing inline task add/edit implementation.

**UI Flow:**
1. User clicks "+" button in header
2. Inline input field appears at top of task list with placeholder "New list name..."
3. User types list name and presses Enter (or clicks away)
4. List is created and appears in the list (empty lists now persist)
5. On error (invalid name, already exists), show error in status bar

## Technical Approach

### Phase 1: Add UI State for Creating List

Add state variable to track when creating a new list:

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
// Existing state
private string? _addingToList;
private string? _editingTaskId;

// New state
private bool _creatingNewList;
```

### Phase 2: Add "+" Button to Header (AXAML)

Modify header grid to accommodate a new button:

**File: `src/TaskerTray/Views/TaskListPopup.axaml`**

```xml
<!-- Header - modify ColumnDefinitions -->
<Grid ColumnDefinitions="*,Auto,Auto">
    <TextBlock Text="Tasker" ... />

    <!-- Existing list filter button at column 1 -->
    <Button Grid.Column="1" x:Name="ListFilterButton" ... />

    <!-- NEW: Create list button at column 2 -->
    <Button Grid.Column="2"
            x:Name="CreateListButton"
            Content="+"
            Width="28"
            Height="28"
            FontSize="16"
            Padding="0"
            Margin="6,0,0,0"
            Background="Transparent"
            Foreground="#888"
            CornerRadius="6"
            ToolTip.Tip="Create new list"
            Click="OnCreateListClick"/>
</Grid>
```

### Phase 3: Implement Click Handler and Inline Input

**File: `src/TaskerTray/Views/TaskListPopup.axaml.cs`**

```csharp
private void OnCreateListClick(object? sender, RoutedEventArgs e)
{
    // No-op if already in create mode (handles double-click)
    if (_creatingNewList) return;

    CancelInlineEdit();  // Discards any pending task add/edit
    _creatingNewList = true;
    BuildTaskList();
}

private Border CreateInlineListNameField()
{
    var border = new Border
    {
        Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8),
        Margin = new Thickness(4, 4, 4, 8)
    };

    var textBox = new TextBox
    {
        Watermark = "New list name...",
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = 13,
        Foreground = Brushes.White,
        AcceptsReturn = false,  // Single line only
        MaxLength = 50          // Prevent overflow
    };

    textBox.KeyDown += (_, e) =>
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitNewListName(textBox.Text);
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineEdit();
        }
    };

    // CRITICAL: Apply show count pattern to prevent race condition
    var capturedShowCount = _showCount;
    textBox.LostFocus += (_, _) =>
    {
        if (capturedShowCount != _showCount)
            return;

        if (!string.IsNullOrWhiteSpace(textBox.Text))
            SubmitNewListName(textBox.Text);
        else
            CancelInlineEdit();
    };

    _activeInlineEditor = textBox;
    border.Child = textBox;

    Avalonia.Threading.Dispatcher.UIThread.Post(() => textBox.Focus(),
        Avalonia.Threading.DispatcherPriority.Background);

    return border;
}

private void SubmitNewListName(string? name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        CancelInlineEdit();
        return;
    }

    try
    {
        ListManager.CreateList(name.Trim());
        CancelInlineEdit();
        RefreshTasks();
        StatusText.Text = $"Created list '{name.Trim()}'";
    }
    catch (Exception ex)
    {
        StatusText.Text = $"Error: {ex.Message}";
        CancelInlineEdit();
    }
}
```

### Phase 4: Update BuildTaskList to Show Inline Input

Modify `BuildTaskList()` to insert the create list input at the top when `_creatingNewList` is true:

```csharp
private void BuildTaskList()
{
    TaskListPanel.Children.Clear();

    // Show create list input at top if active
    if (_creatingNewList)
    {
        TaskListPanel.Children.Add(CreateInlineListNameField());
    }

    // ... rest of existing logic unchanged ...
}
```

### Phase 5: Update CancelInlineEdit

Add `_creatingNewList` to the cancel method:

```csharp
private void CancelInlineEdit()
{
    _activeInlineEditor = null;
    _editingTaskId = null;
    _addingToList = null;
    _creatingNewList = false; // NEW
}
```

### Phase 6: Update SavePendingInlineAdd (Optional)

If you want to save the pending list name when popup closes (like tasks), add handling:

```csharp
private void SavePendingInlineAdd()
{
    // Existing task add logic...

    // NEW: Handle pending list creation
    if (_creatingNewList && _activeInlineEditor != null)
    {
        var name = _activeInlineEditor.Text;
        if (!string.IsNullOrWhiteSpace(name))
        {
            try { ListManager.CreateList(name.Trim()); }
            catch { /* Silently fail */ }
        }
    }
}
```

## Design Decisions

Based on spec analysis, these behaviors are defined:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Case sensitivity | Case-sensitive | Matches existing file system behavior |
| Max name length | 50 characters | Reasonable UI limit, prevents overflow |
| Conflicting inline state | Discard silently | Matches existing task add behavior |
| Double-click "+" | No-op (already in create mode) | Prevents confusing state |
| Post-creation view | Stay on current view | Least surprising behavior |
| AcceptsReturn | false (single line) | List names are single line |
| Reserved names | Only "tasks" protected | Matches CLI behavior |

## Acceptance Criteria

### Core Functionality
- [x] "+" button appears in header bar next to list filter dropdown
- [x] Clicking "+" shows inline input field at top of task list
- [x] Enter key submits the list name
- [x] Escape key cancels creation
- [x] Clicking outside saves (if valid name) or cancels (if empty)
- [x] Success: new list appears in list immediately
- [x] Popup close saves pending list name (like tasks)

### Error Handling
- [x] Invalid name (spaces, special chars) shows error in status bar
- [x] Duplicate name shows error in status bar
- [x] Names > 50 chars show error in status bar

### Edge Cases
- [x] Double-clicking "+" does nothing extra (no-op if already in create mode)
- [x] Clicking "+" while adding/editing task cancels task edit, starts list create
- [x] Single-line input only (no newlines accepted)
- [x] No race condition bugs (show count pattern applied)

### Additional Features (Added)
- [x] Delete list option via "..." menu on list headers (non-default lists only)

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerTray/Views/TaskListPopup.axaml` | Add "+" button to header grid |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add state, handlers, inline input creation |

## References

### Internal References
- Inline add pattern: `src/TaskerTray/Views/TaskListPopup.axaml.cs:239-298`
- Show count race condition fix: `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`
- List creation API: `src/TaskerCore/Data/ListManager.cs:43-57`

### Exceptions to Handle
- `InvalidListNameException` - "Use only letters, numbers, hyphens, and underscores"
- `ListAlreadyExistsException` - "List 'x' already exists"

## Verification

```bash
# Build
cd /Users/carlos/self-development/cli-tasker/src/TaskerTray
dotnet build

# Run and test
dotnet run

# Test cases:
# 1. Click "+", type "mylist", press Enter -> list created
# 2. Click "+", type "my list" (space), press Enter -> error shown
# 3. Click "+", type "tasks", press Enter -> error (already exists)
# 4. Click "+", type "test", click outside -> list created
# 5. Click "+", press Escape -> cancelled, no list created
# 6. Click "+", type "test2", close popup -> list created on close
# 7. Double-click "+" -> only one input field appears
# 8. Click "+", then click per-list "+" button -> task add replaces list create
# 9. Paste very long text -> truncated at 50 chars
# 10. Paste multi-line text -> only first line used
```
