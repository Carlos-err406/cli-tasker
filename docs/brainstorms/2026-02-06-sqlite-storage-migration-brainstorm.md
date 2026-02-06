# Brainstorm: Migrate to SQLite-Based Storage

**Date:** 2026-02-06
**Task:** #e2d

## What We're Building

Replace all JSON file-based persistence (tasks, trash, config, undo history) with a single SQLite database. This unifies storage, improves query performance, enables future mobile app integration, and allows in-memory SQLite for tests.

## Why This Approach

- **Mobile app planned** — SQLite is natively supported on iOS/Android, making sync or shared storage feasible
- **Future-proofing** — queries, filtering, and sorting at the database level instead of in-memory LINQ over deserialized JSON
- **Test simplicity** — in-memory SQLite replaces temp directory + file cleanup pattern
- **Single file** — one `.db` file instead of 5+ JSON files

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Scope | Everything in SQLite | Tasks, trash, config, undo, backups — single source of truth |
| Undo storage | JSON blob column | Preserves existing polymorphic serialization, minimal change to undo system |
| Migration | Auto-migrate on first run | If JSON files exist but no .db, import and rename old files to .bak |
| Backup strategy | SQLite backup API | Hot, consistent backups via `sqlite3_backup_init` |
| Test isolation | In-memory SQLite | `new SqliteConnection("Data Source=:memory:")` — no temp dirs needed |
| ORM | Microsoft.Data.Sqlite (raw) | Lightweight, no EF overhead for a simple schema |

## Schema (Conceptual)

```sql
-- Core data
CREATE TABLE lists (
    name TEXT PRIMARY KEY,
    is_collapsed INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0
);

CREATE TABLE tasks (
    id TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    is_checked INTEGER DEFAULT 0,
    created_at TEXT NOT NULL,
    list_name TEXT NOT NULL REFERENCES lists(name),
    due_date TEXT,
    priority INTEGER,
    tags TEXT,  -- JSON array
    is_trashed INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0
);

-- Config
CREATE TABLE config (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Undo
CREATE TABLE undo_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stack_type TEXT NOT NULL,  -- 'undo' or 'redo'
    command_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);

-- Backups metadata
CREATE TABLE backup_metadata (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    type TEXT NOT NULL,  -- 'version', 'daily', 'pre-restore'
    file_path TEXT NOT NULL
);
```

## Migration Strategy

1. On startup, check if `all-tasks.json` exists AND `tasker.db` does NOT
2. If so: create SQLite db, import all data from JSON files
3. Rename old files to `.bak` (recoverable)
4. Existing `DeserializeWithMigration` logic handles old-format JSON → new-format before import

## Resolved Questions

| Question | Decision |
|----------|----------|
| Trash model | Flag on tasks table (is_trashed column). Single table, restore = flip a bit. |
| Task ordering | Explicit sort_order integer column. Supports drag-to-reorder directly. |
| Journal mode | WAL mode for concurrent reads (tray + CLI). |
| Change detection | Refresh-on-show is sufficient. No file watching or change notifications needed. |
