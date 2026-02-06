---
title: "feat: In-Progress Task Status"
type: feat
date: 2026-02-06
---

# In-Progress Task Status

## Overview

Replace the binary `bool IsChecked` with a 3-state `TaskStatus` enum: **pending**, **in-progress**, **done**. This gives at-a-glance awareness of which tasks are actively being worked on vs. waiting in the backlog.

Based on brainstorm: `docs/brainstorms/2026-02-06-in-progress-task-status-brainstorm.md`

## Problem Statement

The current binary checked/unchecked system doesn't distinguish between "haven't started" and "actively working on it." For a personal task manager, knowing what's in-flight is the most important signal — it answers "what am I doing right now?"

## Proposed Solution

### Status Model

New enum in `src/TaskerCore/Models/TaskStatus.cs`:

```csharp
public enum TaskStatus
{
    Pending = 0,
    InProgress = 1,
    Done = 2
}
```

Replace `bool IsChecked` on `TodoTask` with `TaskStatus Status`. SQLite column `is_checked` becomes `status INTEGER DEFAULT 0`.

### CLI Commands

| Command | Effect |
|---------|--------|
| `tasker status pending <ids>` | Set to pending |
| `tasker status in-progress <ids>` | Set to in-progress |
| `tasker status done <ids>` | Set to done |
| `tasker wip <ids>` | Alias → in-progress |
| `tasker check <ids>` | Alias → done |
| `tasker uncheck <ids>` | Alias → pending |

### Visual Treatment

| Status | CLI | Tray |
|--------|-----|------|
| Pending | `[ ]` grey | Grey unchecked checkbox |
| In-progress | `[-]` yellow/amber | Yellow/amber indicator |
| Done | `[x]` green, strikethrough | Green checked, dimmed text |

### Sort Order

1. **In-progress** (active work — top)
2. **Pending** (backlog — middle)
3. **Done** (finished — bottom)

Within each group, existing sort applies (priority, due date, sort_order DESC).

## Technical Approach

### Phase 1: Core Model + Database Migration

**Files:**

| File | Change |
|------|--------|
| `src/TaskerCore/Models/TaskStatus.cs` | NEW — enum definition |
| `src/TaskerCore/Models/TodoTask.cs` | `bool IsChecked` → `TaskStatus Status`, remove `Check()`/`UnCheck()`, add `WithStatus(TaskStatus)` |
| `src/TaskerCore/Data/TaskerDb.cs` | Schema migration: rename `is_checked` → `status`, migrate values `1` → `2` |
| `src/TaskerCore/Data/TodoTaskList.cs` | Update read mapping, `UpdateTask`, sort queries, filter logic |
| `src/TaskerCore/Data/TaskStats.cs` | Add `InProgress` count, rename `Checked` → `Done`, `Unchecked` → `Pending` |
| `src/TaskerCore/Data/JsonMigrator.cs` | Map `IsChecked` bool → status int during JSON migration |

**DB migration SQL** (in `TaskerDb.EnsureCreated` or a migration method):

```sql
-- Step 1: Add new column
ALTER TABLE tasks ADD COLUMN status INTEGER DEFAULT 0;

-- Step 2: Migrate existing data
UPDATE tasks SET status = CASE WHEN is_checked = 1 THEN 2 ELSE 0 END;

-- Step 3: SQLite doesn't support DROP COLUMN before 3.35.0
-- Recreate table without is_checked
CREATE TABLE tasks_new (
    id TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    status INTEGER DEFAULT 0,
    created_at TEXT NOT NULL,
    list_name TEXT NOT NULL REFERENCES lists(name) ON UPDATE CASCADE ON DELETE CASCADE,
    due_date TEXT,
    priority INTEGER,
    tags TEXT,
    is_trashed INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0
);
INSERT INTO tasks_new SELECT id, description, status, created_at, list_name,
    due_date, priority, tags, is_trashed, sort_order FROM tasks;
DROP TABLE tasks;
ALTER TABLE tasks_new RENAME TO tasks;

-- Step 4: Recreate indexes
CREATE INDEX IF NOT EXISTS idx_tasks_list ON tasks(list_name);
CREATE INDEX IF NOT EXISTS idx_tasks_sort ON tasks(list_name, is_trashed, status, sort_order DESC);
```

**Detection:** Check if `is_checked` column exists via `PRAGMA table_info(tasks)`. If it does, run migration.

**TodoTask record change:**

```csharp
// Before
public record TodoTask(string Id, string Description, bool IsChecked, ...)
{
    public TodoTask Check() => this with { IsChecked = true };
    public TodoTask UnCheck() => this with { IsChecked = false };
}

// After
public record TodoTask(string Id, string Description, TaskStatus Status, ...)
{
    public TodoTask WithStatus(TaskStatus status) => this with { Status = status };

    // Backward compat computed property for JSON migration
    [JsonIgnore]
    public bool IsChecked => Status == TaskStatus.Done;
}
```

**Sort query change** (in `TodoTaskList`):

```sql
-- Before
ORDER BY is_checked ASC, sort_order DESC

-- After: in-progress (1) first, then pending (0), then done (2)
ORDER BY CASE status WHEN 1 THEN 0 WHEN 0 THEN 1 WHEN 2 THEN 2 END ASC,
         sort_order DESC
```

### Phase 2: Status Command + Undo

**Files:**

| File | Change |
|------|--------|
| `AppCommands/StatusCommand.cs` | NEW — generic `tasker status <status> <ids>` command |
| `AppCommands/CheckCommand.cs` | Become thin aliases delegating to `SetStatus` |
| `src/TaskerCore/Undo/Commands/SetStatusCommand.cs` | NEW — stores `OldStatus` + `NewStatus` |
| `src/TaskerCore/Undo/Commands/CheckTaskCommand.cs` | REMOVE (replaced by SetStatusCommand) |
| `src/TaskerCore/Undo/Commands/UncheckTaskCommand.cs` | REMOVE (replaced by SetStatusCommand) |
| `src/TaskerCore/Undo/IUndoableCommand.cs` | Remove check/uncheck, add `set-status` type discriminator |
| `Program.cs` | Register `status` and `wip` commands |

**SetStatusCommand:**

```csharp
public record SetStatusCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public required TaskStatus OldStatus { get; init; }
    public required TaskStatus NewStatus { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;
    public string Description => $"Status: {TaskId} → {NewStatus}";

    public void Execute()
    {
        var taskList = new TodoTaskList();
        taskList.SetStatus(TaskId, NewStatus, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        taskList.SetStatus(TaskId, OldStatus, recordUndo: false);
    }
}
```

**TodoTaskList new method:**

```csharp
public TaskResult SetStatus(string taskId, TaskStatus status, bool recordUndo = true)
{
    var task = GetTodoTaskById(taskId);
    if (task == null) return new TaskResult.NotFound(taskId);
    if (task.Status == status) return new TaskResult.NoChange(taskId);

    if (recordUndo)
    {
        var cmd = new SetStatusCommand
        {
            TaskId = taskId,
            OldStatus = task.Status,
            NewStatus = status
        };
        _services.Undo.RecordCommand(cmd);
    }

    CreateBackup();
    UpdateTask(task.WithStatus(status));
    BumpSortOrder(taskId, task.ListName);

    if (recordUndo) _services.Undo.SaveHistory();
    return new TaskResult.Success($"Set {taskId} to {status}");
}
```

**Undo history migration:** Clear existing undo history during DB migration since `CheckTaskCommand`/`UncheckTaskCommand` types will no longer deserialize. This is safe — undo history is ephemeral.

**TaskResult.NoChange:** New result variant for idempotent status set (already in target state). Display as info, not error.

### Phase 3: CLI Display + Filtering

**Files:**

| File | Change |
|------|--------|
| `AppCommands/ListCommand.cs` | Update checkbox rendering, add `--status` filter option |
| `AppCommands/GetCommand.cs` | Update JSON output (`status: "in-progress"`) and human-readable display |
| `AppCommands/SystemCommand.cs` | Update stats display for 3 states |
| `Output.cs` | Add status formatting helpers if needed |

**Checkbox rendering:**

```csharp
// Before (ListCommand.cs:124)
var checkbox = td.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";

// After
var checkbox = td.Status switch
{
    TaskStatus.Done => "[green][[x]][/]",
    TaskStatus.InProgress => "[yellow][[-]][/]",
    TaskStatus.Pending => "[grey][[ ]][/]",
    _ => "[grey][[ ]][/]"
};
```

**Filtering:**

```csharp
// Backward compat aliases
// -c → done only
// -u → pending + in-progress
// New explicit: --status in-progress
var statusOption = new Option<string?>("--status", "Filter by status: pending, in-progress, done");
```

**JSON output** (`GetCommand.cs`):

```csharp
// Before
isChecked = task.IsChecked,

// After
status = task.Status switch
{
    TaskStatus.Pending => "pending",
    TaskStatus.InProgress => "in-progress",
    TaskStatus.Done => "done",
    _ => "pending"
},
```

### Phase 4: TUI Updates

**Files:**

| File | Change |
|------|--------|
| `Tui/TuiRenderer.cs` | 3-state checkbox + strikethrough only for done |
| `Tui/TuiApp.cs` | Space key cycles: pending → in-progress → done → pending |

**TUI checkbox:**

```csharp
// Before (TuiRenderer.cs:95)
var checkbox = task.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";

// After
var checkbox = task.Status switch
{
    TaskStatus.Done => "[green][[x]][/]",
    TaskStatus.InProgress => "[yellow][[-]][/]",
    _ => "[grey][[ ]][/]"
};
```

**Space key cycling** in TuiApp.cs:

```csharp
// Before: toggle checked/unchecked
// After: cycle pending → in-progress → done → pending
var nextStatus = task.Status switch
{
    TaskStatus.Pending => TaskStatus.InProgress,
    TaskStatus.InProgress => TaskStatus.Done,
    TaskStatus.Done => TaskStatus.Pending,
    _ => TaskStatus.Pending
};
todoTaskList.SetStatus(task.Id, nextStatus);
```

### Phase 5: Tray Updates

**Files:**

| File | Change |
|------|--------|
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | `bool IsChecked` → `TaskStatus Status` observable, toggle logic |
| `src/TaskerTray/Converters/CheckedToForegroundConverter.cs` | Rename → `StatusToForegroundConverter`, handle 3 states |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Update checkbox indicator, add right-click for in-progress |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Bind to Status, update indicator template |

**Tray checkbox UX:**
- **Click** = toggle done (preserves quick check-off gesture)
- **Right-click** = set in-progress (secondary action)
- Checkbox icon: `[ ]` grey, `[-]` yellow, `[x]` green

**StatusToForegroundConverter:**

```csharp
public object Convert(object? value, ...)
{
    return value switch
    {
        TaskStatus.Done => new SolidColorBrush(Color.Parse("#666666")),
        TaskStatus.InProgress => new SolidColorBrush(Color.Parse("#FFFFFF")),
        _ => new SolidColorBrush(Color.Parse("#FFFFFF"))
    };
}
```

## Acceptance Criteria

### Functional
- [ ] `tasker status in-progress <ids>` marks tasks as in-progress
- [ ] `tasker status done <ids>` marks tasks as done
- [ ] `tasker status pending <ids>` resets tasks to pending
- [ ] `tasker wip <ids>` is alias for in-progress
- [ ] `tasker check <ids>` is alias for done (backward compat)
- [ ] `tasker uncheck <ids>` is alias for pending (backward compat)
- [ ] Setting same status is idempotent (no error, info message)
- [ ] Batch operations work: `tasker wip abc def ghi`
- [ ] Undo/redo works for all status transitions
- [ ] In-progress tasks sort above pending, pending above done
- [ ] `tasker list -u` shows pending + in-progress
- [ ] `tasker list -c` shows done only
- [ ] `tasker get --json` outputs `"status": "in-progress"` (string)
- [ ] `tasker system status` shows 3-state counts

### Visual
- [ ] CLI: `[ ]` grey, `[-]` yellow, `[x]` green strikethrough
- [ ] TUI: Same rendering, Space cycles states
- [ ] Tray: Click toggles done, right-click sets in-progress

### Data
- [ ] DB migration: `is_checked` → `status` with value mapping (0→0, 1→2)
- [ ] JSON migration: `IsChecked` bool → status int
- [ ] Existing undo history cleared during migration (type incompatibility)
- [ ] All existing tests updated and passing

## Surface Area

| Layer | File | Lines to Change |
|-------|------|-----------------|
| Model | `TodoTask.cs:5-13` | Replace `IsChecked` parameter |
| Model | `TodoTask.cs:38-40` | Replace `Check()`/`UnCheck()` |
| Enum | `TaskStatus.cs` | NEW |
| Schema | `TaskerDb.cs:72-83` | Column rename + migration |
| Data | `TodoTaskList.cs:165-183` | `UpdateTask` SQL |
| Data | `TodoTaskList.cs:231-284` | Replace `CheckTask`/`UncheckTask` with `SetStatus` |
| Data | `TodoTaskList.cs` sort queries | Status-based ordering |
| Data | `TaskStats.cs:6-12` | Add `InProgress` field |
| Data | `JsonMigrator.cs` | Map bool → int for status |
| CLI | `StatusCommand.cs` | NEW |
| CLI | `CheckCommand.cs:8-47` | Delegate to SetStatus |
| CLI | `ListCommand.cs:124` | 3-state checkbox |
| CLI | `ListCommand.cs:15-37` | Filter options |
| CLI | `GetCommand.cs:59-73` | JSON status string |
| CLI | `GetCommand.cs:75-91` | Human-readable status |
| CLI | `SystemCommand.cs` | Stats display |
| CLI | `Program.cs` | Register new commands |
| TUI | `TuiRenderer.cs:95` | 3-state checkbox |
| TUI | `TuiRenderer.cs:108-133` | Strikethrough only for done |
| TUI | `TuiApp.cs` | Space key cycling |
| Tray | `TodoTaskViewModel.cs:32` | `bool _isChecked` → `TaskStatus` |
| Tray | `TodoTaskViewModel.cs:120-141` | Toggle → status cycle |
| Tray | `CheckedToForegroundConverter.cs` | Rename + 3-state |
| Tray | `TaskListPopup.axaml.cs` | Right-click handler |
| Tray | `TaskListPopup.axaml` | Bind status indicator |
| Undo | `SetStatusCommand.cs` | NEW |
| Undo | `CheckTaskCommand.cs` | REMOVE |
| Undo | `UncheckTaskCommand.cs` | REMOVE |
| Undo | `IUndoableCommand.cs:9-10` | Update type registrations |

## Edge Cases

- **Idempotent status set:** `tasker wip abc` when abc is already in-progress → `TaskResult.NoChange` info message
- **Batch mixed results:** Some tasks succeed, some not found → per-task feedback (existing pattern)
- **Migration with undo history:** Clear undo/redo stacks — old `check`/`uncheck` command types won't deserialize
- **Filter interaction:** `-u` = pending + in-progress (anything not done), `-c` = done only
- **New task default:** Tasks start as `Pending` (status = 0), same as current unchecked default
- **TUI cycling:** Space key: pending → in-progress → done → pending (3-step cycle)

## Implementation Order

1. **TaskStatus enum + TodoTask model** (foundation — everything depends on this)
2. **DB migration** (schema must exist before data layer changes)
3. **TodoTaskList + SetStatus method** (core data operations)
4. **SetStatusCommand + undo registration** (undo must work before CLI)
5. **StatusCommand + aliases** (CLI surface)
6. **ListCommand + GetCommand display** (visual output)
7. **TUI updates** (TuiRenderer + TuiApp)
8. **Tray updates** (ViewModel + converter + AXAML)

## References

- Brainstorm: `docs/brainstorms/2026-02-06-in-progress-task-status-brainstorm.md`
- SQLite migration pattern: `docs/solutions/database-issues/json-to-sqlite-storage-migration.md`
- Undo system pattern: `src/TaskerCore/Undo/Commands/CheckTaskCommand.cs`
- Current schema: `src/TaskerCore/Data/TaskerDb.cs:62-96`
