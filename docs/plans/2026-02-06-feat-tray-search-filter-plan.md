---
title: "feat: Add search/filter to tray popup"
type: feat
date: 2026-02-06
reviewed: true
---

# feat: Add search/filter to tray popup

## Overview

Add an always-visible search TextBox to the tray popup that filters tasks in real-time using `LIKE '%query%'` on the task description. The search is list-agnostic (always searches all lists regardless of current list filter).

## Brainstorm Reference

`docs/brainstorms/2026-02-06-tray-search-filter-brainstorm.md`

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Search scope | List-agnostic (all lists) | The point of search is to find things fast |
| Query method | `LIKE '%query%'` on description | Tags/metadata already inline in description |
| Case sensitivity | Case-insensitive via `COLLATE NOCASE` | Clean SQL, no function call on every row |
| UI placement | Always-visible TextBox below header | No toggle state, no animation, self-documenting (Spotlight/Alfred pattern) |
| Filtering | Real-time with 200ms debounce via `DispatcherTimer` | Fast, fires on UI thread, no thread-safety concerns |
| Escape behavior | Clear search text if non-empty, otherwise close popup | Simple 2-level: editor → search text → popup |
| Search persistence | Reset on popup close | Clear text in `ShowAtPosition()` |
| Empty results | Global "No results" message, hide empty lists | Cleaner than per-list empty states |
| Result count | Footer shows "X matching" | Simple, sufficient context |
| LIKE escaping | Escape `%` and `_` in user input | Prevent LIKE pattern injection |
| Keyboard shortcut | Cmd+K focuses search TextBox | Standard search shortcut (VS Code, Slack, etc.) |
| Data layer | Separate `SearchTasks()` method | One method, one responsibility (DHH review) |

### Dropped from v1 (YAGNI)

- Search highlights (bold matched text)
- Slide-in animation for search bar
- Magnifying glass toggle icon
- Disable drag during search
- Min query length

## Acceptance Criteria

- [x] Search TextBox visible below header with "Search all tasks..." watermark
- [x] Real-time filtering as user types (200ms debounce)
- [x] Case-insensitive `LIKE '%query%' COLLATE NOCASE` on description column with `%`/`_` escaping
- [x] Results grouped by list, empty lists hidden during search
- [x] Footer shows "X matching" count when search has text
- [x] Escape clears search text when non-empty; closes popup when empty
- [x] Search text cleared when popup reopens
- [x] Cmd+K focuses the search TextBox
- [x] Task operations (check, delete, move) work in search results and re-filter

## Technical Approach

### 1. Data Layer — `TodoTaskList.cs`

Add a dedicated `SearchTasks()` static method (separate from existing `GetFilteredTasks`):

```csharp
// TodoTaskList.cs — new static method
public static List<TodoTask> SearchTasks(TaskerServices services, string query)
{
    var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    var sql = $"SELECT {TaskSelectColumns} FROM tasks WHERE is_trashed = 0 AND description LIKE @search ESCAPE '\\' COLLATE NOCASE ORDER BY sort_order DESC";
    var tasks = services.Db.Query(sql, ReadTask, ("@search", $"%{escaped}%"));

    // Reuse existing sort logic: active by priority/due/created, done by completed_at
    var today = DateOnly.FromDateTime(DateTime.Today);
    var active = tasks.Where(t => t.Status != TaskStatus.Done)
        .OrderBy(t => StatusSortOrder(t.Status))
        .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
        .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
        .ThenByDescending(t => t.CreatedAt)
        .ToList();
    var done = tasks.Where(t => t.Status == TaskStatus.Done)
        .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
        .ToList();
    return [..active, ..done];
}
```

### 2. UI Layout — `TaskListPopup.axaml`

Change the main Grid from 3 rows to 4 rows and add a search TextBox row:

```xml
<Grid RowDefinitions="Auto,Auto,*,Auto">
    <!-- Row 0: Header (unchanged) -->
    <!-- Row 1: Search bar (NEW) -->
    <Border Grid.Row="1" Background="#2A2A2A" Padding="12,6">
        <TextBox x:Name="SearchTextBox"
                 Watermark="Search all tasks..."
                 Background="Transparent"
                 BorderThickness="0"
                 FontSize="13"
                 Foreground="White"/>
    </Border>
    <!-- Row 2: ScrollViewer (was Row 1) -->
    <!-- Row 3: Footer (was Row 2) -->
</Grid>
```

Update all existing `Grid.Row` references: ScrollViewer from 1→2, Footer from 2→3.

### 3. Behavior — `TaskListPopup.axaml.cs`

**New fields:**

```csharp
private string? _searchQuery;
private DispatcherTimer? _searchDebounceTimer;
```

**Wire up in constructor or InitializeComponent:**

```csharp
SearchTextBox.TextChanged += OnSearchTextChanged;
```

**Debounce handler:**

```csharp
private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
{
    _searchDebounceTimer?.Stop();

    var text = SearchTextBox.Text;
    var query = string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    _searchDebounceTimer.Tick += (_, _) =>
    {
        _searchDebounceTimer!.Stop();
        _searchQuery = query;
        DoRefreshTasks();
    };
    _searchDebounceTimer.Start();
}
```

**Update `DoRefreshTasks()`:**

```csharp
private void DoRefreshTasks()
{
    List<TodoTask> tasks;
    if (_searchQuery != null)
    {
        // Search mode: list-agnostic
        tasks = TodoTaskList.SearchTasks(TaskerServices.Default, _searchQuery);
    }
    else
    {
        var taskList = new TodoTaskList(_currentListFilter);
        tasks = taskList.GetSortedTasks();
    }

    _tasks.Clear();
    foreach (var task in tasks)
        _tasks.Add(new TodoTaskViewModel(task));

    BuildTaskList();
    UpdateStatus();
}
```

**Update `BuildTaskList()` — hide empty lists during search:**

```csharp
// In the list iteration loop:
if (_searchQuery != null && !tasksByList.ContainsKey(listName))
    continue;  // skip lists with no matching tasks

// After loop, if no results:
if (_searchQuery != null && _tasks.Count == 0)
{
    var noResults = new TextBlock
    {
        Text = $"No results for \"{_searchQuery}\"",
        Foreground = new SolidColorBrush(Color.Parse("#666")),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 40, 0, 0)
    };
    TaskListPanel.Children.Add(noResults);
}
```

**Update `UpdateStatus()`:**

```csharp
if (_searchQuery != null)
    StatusText.Text = $"{_tasks.Count} matching";
else
    // ... existing status logic
```

**Update `OnKeyDown()` — Escape and Cmd+K:**

```csharp
if (e.Key == Key.Escape)
{
    if (_activeInlineEditor != null)
    {
        CancelInlineEdit();
        e.Handled = true;
    }
    else if (!string.IsNullOrEmpty(SearchTextBox.Text))
    {
        SearchTextBox.Text = "";
        SearchTextBox.Focus();
        e.Handled = true;
    }
    else
    {
        _ = HideWithAnimation();
    }
}
else if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
{
    SearchTextBox.Focus();
    SearchTextBox.SelectAll();
    e.Handled = true;
}
```

**Clear search on popup show (`ShowAtPosition`):**

```csharp
public void ShowAtPosition(PixelPoint position)
{
    _showCount++;
    SearchTextBox.Text = "";  // clear search
    _searchQuery = null;
    _searchDebounceTimer?.Stop();
    // ... existing code
}
```

## Files to Modify

| File | Change |
|------|--------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Add static `SearchTasks()` method |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Add search TextBox row, update Grid rows |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Debounce, refresh integration, escape/Cmd+K, clear on show |

## Files NOT Modified

- No schema changes (no migration needed)
- No CLI changes
- No model changes
- No undo system changes

## Testing Strategy

Unit tests in `TaskerCore.Tests`:
- `SearchTasks` returns only matching tasks
- `SearchTasks` is case-insensitive
- `SearchTasks` searches across all lists (not filtered by list)
- `SearchTasks` with LIKE wildcards (`%`, `_`) in query treats them as literals
- `SearchTasks` preserves sort order (active by priority, done by completed_at)
- `SearchTasks` with whitespace-only query: caller should pass null (not tested at SQL level)

Manual testing for tray:
- Type query → results filter in real-time
- Escape with text → clears text
- Escape with empty search → closes popup
- Cmd+K → focuses search box
- Close and reopen popup → search text cleared
- Check/delete/move tasks during search → results update
- Inline add during search → works, search stays visible
- Click task checkbox, then press Escape → clears search (not stuck)
