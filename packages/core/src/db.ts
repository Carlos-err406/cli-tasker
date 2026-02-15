import Database from 'better-sqlite3';
import { drizzle } from 'drizzle-orm/better-sqlite3';
import * as schema from './schema/index.js';
import { join } from 'node:path';
import { homedir } from 'node:os';
import { mkdirSync } from 'node:fs';

export type TaskerDb = ReturnType<typeof drizzle<typeof schema>>;

/** Returns the platform-appropriate default database path */
export function getDefaultDbPath(): string {
  const platform = process.platform;
  let dir: string;

  if (platform === 'darwin') {
    dir = join(homedir(), 'Library', 'Application Support', 'cli-tasker');
  } else if (platform === 'win32') {
    dir = join(process.env['APPDATA'] ?? join(homedir(), 'AppData', 'Roaming'), 'cli-tasker');
  } else {
    // Linux / other
    dir = join(process.env['XDG_DATA_HOME'] ?? join(homedir(), '.local', 'share'), 'cli-tasker');
  }

  return join(dir, 'tasker.db');
}

/** The raw SQL to create the schema from scratch (for new databases and tests) */
export const CREATE_SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS lists (
    name TEXT PRIMARY KEY,
    is_collapsed INTEGER DEFAULT 0,
    hide_completed INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS "tasks" (
    id TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    status INTEGER DEFAULT 0,
    created_at TEXT NOT NULL,
    list_name TEXT NOT NULL REFERENCES lists(name) ON UPDATE CASCADE ON DELETE CASCADE,
    due_date TEXT,
    priority INTEGER,
    tags TEXT,
    is_trashed INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0,
    completed_at TEXT,
    parent_id TEXT REFERENCES tasks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_tasks_list_name ON tasks(list_name);
CREATE INDEX IF NOT EXISTS idx_tasks_is_trashed ON tasks(is_trashed);
CREATE INDEX IF NOT EXISTS idx_tasks_sort ON tasks(status, priority, due_date, sort_order);
CREATE INDEX IF NOT EXISTS idx_tasks_parent_id ON tasks(parent_id);

CREATE TABLE IF NOT EXISTS task_dependencies (
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    blocks_task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id, blocks_task_id),
    CHECK (task_id != blocks_task_id)
);

CREATE TABLE IF NOT EXISTS task_relations (
    task_id_1 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    task_id_2 TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id_1, task_id_2),
    CHECK (task_id_1 < task_id_2),
    CHECK (task_id_1 != task_id_2)
);

CREATE TABLE IF NOT EXISTS config (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS undo_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stack_type TEXT NOT NULL CHECK(stack_type IN ('undo', 'redo')),
    command_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_undo_stack_type ON undo_history(stack_type);
`;

/**
 * Create a Drizzle database connection with proper pragmas.
 * If no path is given, uses the platform default.
 * Pass ':memory:' for in-memory databases (tests).
 */
export function createDb(path?: string): TaskerDb {
  const dbPath = path ?? getDefaultDbPath();

  // Ensure directory exists for file-based databases
  if (dbPath !== ':memory:') {
    const dir = dbPath.substring(0, dbPath.lastIndexOf('/'));
    if (dir) {
      mkdirSync(dir, { recursive: true });
    }
  }

  const sqlite = new Database(dbPath);

  // Set pragmas — must happen on every connection
  sqlite.pragma('journal_mode = WAL');
  sqlite.pragma('foreign_keys = ON');
  sqlite.pragma('busy_timeout = 5000');

  // Ensure schema exists (idempotent — all statements use IF NOT EXISTS)
  sqlite.exec(CREATE_SCHEMA_SQL);
  sqlite.exec(`INSERT OR IGNORE INTO lists (name, sort_order) VALUES ('tasks', 0)`);

  // Migrations for existing databases (safe to re-run — ALTER TABLE errors are caught)
  try { sqlite.exec(`ALTER TABLE lists ADD COLUMN hide_completed INTEGER DEFAULT 0`); } catch { /* column already exists */ }

  return drizzle(sqlite, { schema });
}

/**
 * Create an in-memory database with schema applied. For tests.
 */
export function createTestDb(): TaskerDb {
  const db = createDb(':memory:');
  const raw = (db as any).$client as Database.Database;
  raw.exec(CREATE_SCHEMA_SQL);

  // Ensure default "tasks" list exists
  raw.exec(`INSERT OR IGNORE INTO lists (name, sort_order) VALUES ('tasks', 0)`);

  return db;
}

/** Sleep utility for retry logic */
function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

/**
 * Retry wrapper with exponential backoff for SQLITE_BUSY errors.
 * Wraps write operations that may fail under concurrent access.
 */
export async function withRetry<T>(fn: () => T, maxRetries = 3): Promise<T> {
  for (let i = 0; i < maxRetries; i++) {
    try {
      return fn();
    } catch (err: unknown) {
      const isBusy = err instanceof Error && 'code' in err && (err as any).code === 'SQLITE_BUSY';
      if (isBusy && i < maxRetries - 1) {
        await sleep(100 * Math.pow(2, i)); // 100ms, 200ms, 400ms
        continue;
      }
      throw err;
    }
  }
  throw new Error('withRetry: max retries exceeded');
}

/**
 * Get the raw Database instance from a Drizzle instance.
 * Useful for operations not supported by Drizzle (backup, raw exec, etc).
 */
export function getRawDb(db: TaskerDb): Database.Database {
  return (db as any).$client as Database.Database;
}

/**
 * Get the file path of the database.
 * libsql's db.name returns '' instead of the path, so we use pragma.
 */
export function getDbPath(db: TaskerDb): string {
  const raw = getRawDb(db);
  const list = raw.pragma('database_list') as Array<{ file: string }>;
  return list[0]?.file ?? '';
}
