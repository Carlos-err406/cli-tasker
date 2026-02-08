import { describe, it, expect, afterEach } from 'vitest';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createDb, createTestDb, getRawDb, CREATE_SCHEMA_SQL, withRetry } from '../src/db.js';

describe('createDb', () => {
  let tmpDir: string | null = null;

  afterEach(() => {
    if (tmpDir) {
      rmSync(tmpDir, { recursive: true, force: true });
      tmpDir = null;
    }
  });

  it('creates an in-memory database', () => {
    const db = createDb(':memory:');
    const raw = getRawDb(db);
    expect(raw.name).toBe(':memory:');
    raw.close();
  });

  it('creates a file-based database and parent directories', () => {
    tmpDir = mkdtempSync(join(tmpdir(), 'tasker-db-test-'));
    const dbPath = join(tmpDir, 'nested', 'dir', 'tasker.db');

    const db = createDb(dbPath);
    const raw = getRawDb(db);

    expect(existsSync(dbPath)).toBe(true);
    expect(raw.pragma('journal_mode', { simple: true })).toBe('wal');
    expect(raw.pragma('foreign_keys', { simple: true })).toBe(1);
    raw.close();
  });

  it('sets foreign keys on', () => {
    const db = createDb(':memory:');
    const raw = getRawDb(db);
    // WAL is requested but in-memory databases report 'memory' instead
    expect(raw.pragma('foreign_keys', { simple: true })).toBe(1);
    raw.close();
  });
});

describe('createTestDb', () => {
  it('creates schema tables', () => {
    const db = createTestDb();
    const raw = getRawDb(db);

    const tables = raw.prepare(
      "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name",
    ).all() as Array<{ name: string }>;

    const tableNames = tables.map(t => t.name);
    expect(tableNames).toContain('tasks');
    expect(tableNames).toContain('lists');
    expect(tableNames).toContain('task_dependencies');
    expect(tableNames).toContain('task_relations');
    expect(tableNames).toContain('config');
    expect(tableNames).toContain('undo_history');
  });

  it('creates default "tasks" list', () => {
    const db = createTestDb();
    const raw = getRawDb(db);
    const row = raw.prepare("SELECT name FROM lists WHERE name = 'tasks'").get() as any;
    expect(row).not.toBeUndefined();
    expect(row.name).toBe('tasks');
  });
});

describe('getRawDb', () => {
  it('returns the underlying better-sqlite3 instance', () => {
    const db = createTestDb();
    const raw = getRawDb(db);
    expect(typeof raw.prepare).toBe('function');
    expect(typeof raw.exec).toBe('function');
  });
});

describe('withRetry', () => {
  it('returns the result on success', async () => {
    const result = await withRetry(() => 42);
    expect(result).toBe(42);
  });

  it('retries on SQLITE_BUSY and succeeds', async () => {
    let attempts = 0;
    const result = await withRetry(() => {
      attempts++;
      if (attempts < 3) {
        const err = new Error('database is locked') as any;
        err.code = 'SQLITE_BUSY';
        throw err;
      }
      return 'ok';
    }, 3);

    expect(result).toBe('ok');
    expect(attempts).toBe(3);
  });

  it('throws after max retries', async () => {
    const err = new Error('database is locked') as any;
    err.code = 'SQLITE_BUSY';

    await expect(withRetry(() => { throw err; }, 2)).rejects.toThrow('database is locked');
  });

  it('throws non-BUSY errors immediately', async () => {
    let attempts = 0;
    await expect(withRetry(() => {
      attempts++;
      throw new Error('unrelated error');
    }, 3)).rejects.toThrow('unrelated error');

    expect(attempts).toBe(1);
  });
});

describe('CREATE_SCHEMA_SQL', () => {
  it('is idempotent (can run twice without error)', () => {
    const db = createTestDb();
    const raw = getRawDb(db);
    // Should not throw
    raw.exec(CREATE_SCHEMA_SQL);
  });
});
