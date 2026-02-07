# Key Models and Schema

## TodoTask (Immutable Record)

```csharp
public record TodoTask(
    string Id,              // 3-char GUID prefix
    string Description,     // May contain newlines; last line can be metadata
    TaskStatus Status,      // Pending=0, InProgress=1, Done=2
    DateTime CreatedAt,
    string ListName,
    DateOnly? DueDate = null,
    Priority? Priority = null,
    string[]? Tags = null,
    DateTime? CompletedAt = null,   // Auto-set when status → Done
    string? ParentId = null)        // Subtask parent reference
```

All mutation methods return new instances (functional style): `WithStatus()`, `Rename()`, `MoveToList()`, `SetParent()`, `SetDueDate()`, `SetPriority()`, `SetTags()`, and their `Clear*()` counterparts.

## TaskStatus Enum

```csharp
public enum TaskStatus { Pending = 0, InProgress = 1, Done = 2 }
```

## SQLite Schema

```sql
lists (name TEXT PK, is_collapsed INTEGER, sort_order INTEGER)

tasks (id TEXT PK, description TEXT, status INTEGER, created_at TEXT,
       list_name TEXT FK→lists ON UPDATE/DELETE CASCADE,
       due_date TEXT, priority INTEGER, tags TEXT,
       is_trashed INTEGER, sort_order INTEGER,
       completed_at TEXT,
       parent_id TEXT FK→tasks(id) ON DELETE CASCADE)

task_dependencies (
    task_id TEXT FK→tasks ON DELETE CASCADE,
    blocks_task_id TEXT FK→tasks ON DELETE CASCADE,
    PRIMARY KEY (task_id, blocks_task_id),
    CHECK (task_id != blocks_task_id))

config (key TEXT PK, value TEXT)
undo_history (id INTEGER PK, stack_type TEXT, command_json TEXT, created_at TEXT)
```

Storage path: `~/Library/Application Support/cli-tasker/tasker.db` (WAL mode for concurrent access).

## Result Types

Data layer methods return `TaskResult` (Success | NotFound | NoChange | Error) instead of calling output methods directly. Batch operations return `BatchTaskResult`. See `src/TaskerCore/Results/TaskResult.cs`.
