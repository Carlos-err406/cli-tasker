---
title: JSON to SQLite Storage Migration
date: 2026-02-06
category: database-issues
tags: [sqlite, migration, storage, concurrency, wal, microsoft-data-sqlite]
module: TaskerCore.Data
symptoms:
  - JSON files don't support concurrent CLI + tray access
  - No atomic writes (data loss risk on crash)
  - No query capability (full load into memory)
  - Separate trash file adds complexity
  - Backup requires file copy (not atomic)
severity: major-refactoring
---

# JSON to SQLite Storage Migration

## Problem

cli-tasker stored all data in JSON files (`all-tasks.json`, `all-tasks.trash.json`, `config.json`, `undo-history.json`). This caused:

1. **Concurrent access conflicts** — CLI and TaskerTray both read/write the same files
2. **No atomic operations** — crash mid-write could corrupt data (happened 2026-02-05)
3. **No query capability** — entire file loaded into memory for every operation
4. **Trash overhead** — separate `.trash.json` file duplicated the storage pattern
5. **Fragile backups** — file copy isn't atomic; backup of inconsistent state possible

## Solution

Migrated to a single SQLite database (`tasker.db`) with WAL mode using `Microsoft.Data.Sqlite` (raw ADO.NET, no ORM).

### Why raw SQL instead of an ORM

Evaluated `sqlite-net-pcl` but rejected it — `TodoTask` and `TaskList` are immutable C# records without parameterless constructors, which ORMs require. Raw SQL maps cleanly to records via `SqliteDataReader`.

### Schema

```sql
lists   (name TEXT PK, is_collapsed INTEGER, sort_order INTEGER)
tasks   (id TEXT PK, description, is_checked, created_at, list_name FK,
         due_date, priority, tags, is_trashed, sort_order)
config  (key TEXT PK, value TEXT)
undo_history (id INTEGER PK AUTOINCREMENT, stack_type TEXT, command_json TEXT, created_at TEXT)
```

Key decisions:
- **Trash as flag** (`is_trashed`) instead of separate table/file
- **Tags as JSON string** — simple `["tag1", "tag2"]` in TEXT column
- **FK cascades** — `ON UPDATE CASCADE ON DELETE CASCADE` on `tasks.list_name` eliminates manual cascade logic for list rename/delete
- **WAL mode** — enables concurrent reads from CLI + tray without locks

### Key implementation patterns

**TaskerDb** — connection manager with typed helpers:
```csharp
public T? ExecuteScalar<T>(string sql, params (string name, object? value)[] parameters)
{
    var result = cmd.ExecuteScalar();
    if (result is null or DBNull) return default;
    var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
    return (T)Convert.ChangeType(result, targetType);
}
```

**Sort order convention** — highest `sort_order` = newest/top:
```csharp
// Display: ORDER BY sort_order DESC (newest first)
// BumpSortOrder: MAX(sort_order) + 1 (move to top)
// ReorderTask: query DESC, assign count - 1 - i for write-back
```

**Auto-migration** — `JsonMigrator.MigrateIfNeeded()` runs in `TaskerServices` constructor:
1. Detects old JSON files
2. Imports into SQLite within a transaction
3. Renames to `.bak` only after commit
4. Handles both list-first format and legacy flat `TodoTask[]` format

**Backup** — uses `SqliteConnection.BackupDatabase()` hot backup API instead of file copy.

## Bugs Encountered

### 1. ExecuteScalar nullable type crash

`Convert.ChangeType()` doesn't support `Nullable<T>`. SQLite returns `Int64`, but casting to `long?` threw `InvalidCastException`.

**Fix:** Unwrap nullable before conversion:
```csharp
var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
```

### 2. ReorderTask index inversion

`ReorderTask` queried tasks in ASC order internally, but display uses DESC. Indices were flipped — moving a task "down" actually moved it "up".

**Fix:** Query DESC in ReorderTask, and assign sort_orders in reverse:
```csharp
// Query: ORDER BY sort_order DESC (matching display order)
// Write-back: sort_order = count - 1 - i (index 0 gets highest value)
```

### 3. Backup tests checking for JSON files

Old tests looked for `.backup.json` files and read their text content. SQLite backups produce `.backup.db` binary files.

**Fix:** Rewrote backup tests to verify SQLite backup/restore behavior (add task, backup, delete task, restore, verify task exists).

## Prevention

- **Always use `Nullable.GetUnderlyingType()`** when wrapping `Convert.ChangeType` in generic methods
- **Document sort order convention** in CLAUDE.md and enforce it: one direction (DESC) for all queries and reorder logic
- **When changing storage format**, update backup/restore tests first — they reveal format assumptions fastest
- **Transaction-first migration**: import all data, commit, then rename old files — never rename before confirming success

## Test Strategy

- `TaskerServices.CreateInMemory()` — fast, isolated in-memory SQLite for unit tests
- `TaskerServices(testDir)` — file-based for backup tests that need real `.backup.db` files
- Tests using `SetDefault` must be in `[Collection("IsolatedTests")]` for sequential execution
- Final result: **102 tests passing** (7 new migration tests, 10 rewritten tests)

## Files Changed

| File | Change |
|------|--------|
| `TaskerCore.csproj` | Added Microsoft.Data.Sqlite v10.0.2 |
| `Data/TaskerDb.cs` | NEW: SQLite connection manager |
| `Data/JsonMigrator.cs` | NEW: auto-migration from JSON |
| `Data/TodoTaskList.cs` | Rewritten: JSON arrays to direct SQL |
| `Config/AppConfig.cs` | Rewritten: config table |
| `Undo/UndoManager.cs` | Rewritten: undo_history table |
| `Backup/BackupManager.cs` | Rewritten: SQLite hot backup API |
| `Backup/BackupConfig.cs` | Extension: `.backup.json` to `.backup.db` |
| `StoragePaths.cs` | Added `DatabasePath` |
| `TaskerServices.cs` | Added `Db`, `CreateInMemory()`, `IDisposable` |

## Related

- [Atomic Writes and Rolling Backups](../data-safety/atomic-writes-and-rolling-backups.md)
- [Test Isolation Prevention Strategies](../testing/test-isolation-prevention-strategies.md)
- [SQLite Migration Plan](../../plans/2026-02-06-refactor-sqlite-storage-migration-plan.md)
- [SQLite Migration Brainstorm](../../brainstorms/2026-02-06-sqlite-storage-migration-brainstorm.md)
