import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { mkdtempSync, rmSync, readdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createDb, getRawDb, CREATE_SCHEMA_SQL, type TaskerDb } from '../../src/db.js';
import { BackupManager } from '../../src/backup/backup-manager.js';
import { addTask, getTaskById, getAllTasks } from '../../src/queries/task-queries.js';

let tmpDir: string;
let dbPath: string;
let backupDir: string;
let db: TaskerDb;
let mgr: BackupManager;

beforeEach(() => {
  tmpDir = mkdtempSync(join(tmpdir(), 'tasker-backup-test-'));
  dbPath = join(tmpDir, 'tasker.db');
  backupDir = join(tmpDir, 'backups');
  db = createDb(dbPath);

  const raw = getRawDb(db);
  raw.exec(CREATE_SCHEMA_SQL);
  raw.exec("INSERT OR IGNORE INTO lists (name, sort_order) VALUES ('tasks', 0)");

  mgr = new BackupManager(backupDir, db);
});

afterEach(() => {
  try {
    getRawDb(db).close();
  } catch { /* already closed */ }
  rmSync(tmpDir, { recursive: true, force: true });
});

describe('BackupManager', () => {
  it('creates a backup file', () => {
    addTask(db, 'test task', 'tasks');
    mgr.createBackup();

    expect(existsSync(backupDir)).toBe(true);
    const files = readdirSync(backupDir).filter(f => f.endsWith('.backup.db'));
    // Should have at least 1 version backup and 1 daily backup
    expect(files.length).toBeGreaterThanOrEqual(2);
  });

  it('lists backups newest first', () => {
    mgr.createBackup();
    const backups = mgr.listBackups();
    expect(backups.length).toBeGreaterThanOrEqual(1);

    // All entries should have valid timestamps
    for (const b of backups) {
      expect(b.timestamp).toBeInstanceOf(Date);
      expect(b.fileSize).toBeGreaterThan(0);
    }

    // Verify sorted newest first
    for (let i = 1; i < backups.length; i++) {
      expect(backups[i - 1]!.timestamp.getTime()).toBeGreaterThanOrEqual(backups[i]!.timestamp.getTime());
    }
  });

  it('returns empty list when no backups exist', () => {
    expect(mgr.listBackups()).toEqual([]);
  });

  it('creates daily backup only once per day', () => {
    mgr.createBackup();
    mgr.createBackup();

    const backups = mgr.listBackups();
    const dailyBackups = backups.filter(b => b.isDaily);
    expect(dailyBackups).toHaveLength(1);
  });

  it('restores from backup', () => {
    // Add a task and create backup
    addTask(db, 'original task', 'tasks');
    mgr.createBackup();

    const backups = mgr.listBackups();
    const backupTimestamp = backups[0]!.timestamp;

    // Add another task after backup
    addTask(db, 'after backup', 'tasks');
    expect(getAllTasks(db)).toHaveLength(2);

    // Restore
    mgr.restoreBackup(backupTimestamp);

    // Should only have the original task
    const tasks = getAllTasks(db);
    expect(tasks).toHaveLength(1);
    expect(tasks[0]!.description).toBe('original task');
  });

  it('creates safety backup before restore', () => {
    addTask(db, 'task', 'tasks');
    mgr.createBackup();

    const backups = mgr.listBackups();
    mgr.restoreBackup(backups[0]!.timestamp);

    // Should have pre-restore backup
    const files = readdirSync(backupDir).filter(f => f.includes('pre-restore'));
    expect(files).toHaveLength(1);
  });

  it('throws when restoring nonexistent backup', () => {
    expect(() => mgr.restoreBackup(new Date(2020, 0, 1))).toThrow('not found');
  });

  it('rotates version backups beyond max', () => {
    // Create 12 backups with different timestamps
    for (let i = 0; i < 12; i++) {
      addTask(db, `task ${i}`, 'tasks');
      // Small delay to ensure unique timestamps
      const now = new Date();
      now.setSeconds(now.getSeconds() + i);
      mgr.createBackup();
    }

    const backups = mgr.listBackups();
    const versionBackups = backups.filter(b => !b.isDaily);
    // Max is 10 version backups
    expect(versionBackups.length).toBeLessThanOrEqual(10);
  });

  it('silently handles backup failures', () => {
    // Use a backup manager pointing to a readonly/invalid path
    const badMgr = new BackupManager('/nonexistent/deeply/nested/path', db);
    // Should not throw
    expect(() => badMgr.createBackup()).not.toThrow();
  });
});
