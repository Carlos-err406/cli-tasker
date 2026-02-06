---
title: add search/filter to TaskerTray popup
category: feature-implementations
tags:
  - TaskerTray
  - menu-bar-app
  - popup-ui
  - search
  - filter
  - sqlite
  - LIKE-query
  - debounce
  - avalonia
module: src/TaskerTray
symptoms:
  - user needed to find tasks quickly across multiple lists
  - no way to filter tasks by keyword in tray popup
  - scrolling through all lists was slow when many tasks existed
date_solved: 2026-02-06
---

# Search/Filter in TaskerTray Popup

## Problem/Feature Request

The TaskerTray popup displayed all tasks grouped by list, but provided no way to quickly find a specific task. Users with many tasks across multiple lists had to visually scan or scroll through everything. A search/filter capability was needed to locate tasks by keyword in real-time.

Related task: backlog task `04c` — "filters in tray app"

## Solution Approach

The implementation adds an always-visible search TextBox below the header that:

1. **Filters in real-time**: 200ms debounce via `DispatcherTimer` (fires on UI thread, no thread-safety concerns)
2. **Is list-agnostic**: Always searches across all lists regardless of the current list filter
3. **Uses SQL LIKE with escaping**: `LIKE '%query%' ESCAPE '\' COLLATE NOCASE` prevents LIKE pattern injection from `%` and `_` in user input
4. **Hides empty lists during search**: Only lists with matching tasks are shown
5. **Resets on popup reopen**: Search text cleared in `ShowAtPosition()` so each popup open starts fresh

### Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Search bar visibility | Always visible (not toggled) | Eliminates toggle state, animation, and 2-level escape handling. Spotlight/Alfred pattern. |
| Data layer method | Separate `SearchTasks()` static method | One method, one responsibility. Doesn't thread search through `GetFilteredTasks()`. |
| Case sensitivity | `COLLATE NOCASE` | Clean SQL, no `LOWER()` function call per row |
| Timer type | `DispatcherTimer` | Fires on Avalonia UI thread, eliminating cross-thread issues |
| Keyboard shortcut | Cmd+K | Standard search shortcut (VS Code, Slack, etc.) |
| Escape behavior | 3-level: editor → search text → close popup | Simple, predictable |

## Files Changed

| File | Changes |
|------|---------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Added static `SearchTasks()` method with LIKE escaping and COLLATE NOCASE |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Added search TextBox row, changed Grid from 3 to 4 rows |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Added debounce handler, search-aware refresh, escape/Cmd+K handling, clear on show |
| `tests/TaskerCore.Tests/Data/SearchTasksTests.cs` | 8 unit tests for search behavior |

## Code Examples

### 1. Data Layer — SearchTasks()

Dedicated static method with LIKE wildcard escaping:

```csharp
// src/TaskerCore/Data/TodoTaskList.cs
public static List<TodoTask> SearchTasks(TaskerServices services, string query)
{
    var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    var tasks = services.Db.Query(
        $"SELECT {TaskSelectColumns} FROM tasks WHERE is_trashed = 0 AND description LIKE @search ESCAPE '\\' COLLATE NOCASE ORDER BY sort_order DESC",
        ReadTask,
        ("@search", $"%{escaped}%"));

    // Reuse existing sort: active by status/priority/due/created, done by completed_at
    var today = DateOnly.FromDateTime(DateTime.Today);
    var active = tasks
        .Where(t => t.Status != TaskStatus.Done)
        .OrderBy(t => StatusSortOrder(t.Status))
        .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99)
        .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))
        .ThenByDescending(t => t.CreatedAt)
        .ToList();
    var done = tasks
        .Where(t => t.Status == TaskStatus.Done)
        .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
        .ToList();
    return [..active, ..done];
}

public static List<TodoTask> SearchTasks(string query) => SearchTasks(TaskerServices.Default, query);
```

### 2. UI Layout — Always-Visible Search Bar

```xml
<!-- src/TaskerTray/Views/TaskListPopup.axaml -->
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

### 3. Debounce Handler

```csharp
// src/TaskerTray/Views/TaskListPopup.axaml.cs
private string? _searchQuery;
private DispatcherTimer? _searchDebounceTimer;

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

### 4. Search-Aware Refresh

```csharp
private void DoRefreshTasks()
{
    List<TodoTask> tasks;
    if (_searchQuery != null)
    {
        tasks = TodoTaskList.SearchTasks(_searchQuery); // list-agnostic
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

### 5. Escape and Cmd+K Handling

```csharp
// In OnKeyDown():
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

## How It Works

1. **User types in search box**: `OnSearchTextChanged` fires, starts a 200ms debounce timer
2. **Timer fires**: Sets `_searchQuery` and calls `DoRefreshTasks()`
3. **DoRefreshTasks branches**: If `_searchQuery != null`, calls `SearchTasks()` (all lists). Otherwise, normal filtered refresh.
4. **BuildTaskList skips empty lists**: During search, lists with no matching tasks are hidden entirely
5. **Footer updates**: Shows "X matching" during search instead of normal status
6. **Escape clears search**: If search text is non-empty, first Escape clears it. Second Escape closes popup.
7. **Popup reopen resets**: `ShowAtPosition()` clears search text and stops debounce timer

## Key Patterns Used

### LIKE Wildcard Escaping

SQLite LIKE treats `%` and `_` as wildcards. User input must escape these:

```csharp
var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
// SQL uses ESCAPE '\' clause to define the escape character
```

### DispatcherTimer for UI-Thread Debounce

Using `DispatcherTimer` instead of `System.Threading.Timer` ensures the tick handler runs on the Avalonia UI thread. No `Dispatcher.UIThread.InvokeAsync()` needed, no cross-thread exceptions.

### Separate Static Method for Search

Following DHH's "one method, one responsibility" principle: `SearchTasks()` doesn't share parameters or code paths with `GetFilteredTasks()`. This keeps both methods simple and independently testable.

### Always-Visible Search Bar

An always-visible TextBox eliminates toggle state (`_searchActive`), toggle/close methods, animation classes, and complex escape handling. The 30px vertical cost is worth the ~60 lines of saved complexity.

## Testing

8 unit tests in `SearchTasksTests.cs`:

- `SearchTasks_ReturnsMatchingTasks` — basic keyword matching
- `SearchTasks_IsCaseInsensitive` — lower/upper/mixed case all return same results
- `SearchTasks_SearchesAcrossAllLists` — results span multiple lists
- `SearchTasks_NoMatch_ReturnsEmpty` — nonexistent query returns empty list
- `SearchTasks_EscapesLikeWildcards` — `%` and `_` treated as literals, not wildcards
- `SearchTasks_PreservesSortOrder_ActiveThenDone` — active tasks before done tasks
- `SearchTasks_ExcludesTrashedTasks` — deleted tasks not in results
- `SearchTasks_MatchesTagsInDescription` — `#shopping` tag searchable

## Related Documentation

- [Collapsible Lists](./collapsible-lists-tray.md) — BuildTaskList pattern this feature integrates with
- [Done Tasks Sort by Completion Time](./done-tasks-sort-by-completion-time.md) — Sort order reused in SearchTasks
- [Task Metadata Inline System](./task-metadata-inline-system.md) — Tags stored inline in description, searchable via LIKE
- [Tray Animations & Transitions](./tray-animations-transitions.md) — Popup animation patterns
- [Hide Irrelevant Controls in Filtered View](../ui-bugs/hide-irrelevant-controls-in-filtered-view.md) — Similar list-hiding pattern during search
- [Test Isolation Strategies](../testing/test-isolation-prevention-strategies.md) — In-memory test pattern used
- [Task Teleportation on Status Change](../ui-bugs/task-teleportation-on-status-change.md) — _generationId pattern for stale UI prevention
