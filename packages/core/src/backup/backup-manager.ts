/**
 * Manages automatic backup creation, rotation, and restoration.
 * Uses WAL checkpoint + file copy for backups.
 */

import { copyFileSync, existsSync, mkdirSync, readdirSync, statSync, unlinkSync } from 'node:fs';
import { join, basename } from 'node:path';
import type { TaskerDb } from '../db.js';
import { getRawDb, getDbPath } from '../db.js';
import type { UndoManager } from '../undo/undo-manager.js';

const MAX_VERSION_BACKUPS = 10;
const MAX_DAILY_BACKUP_DAYS = 7;
const BACKUP_EXT = '.backup.db';
const DAILY_PREFIX = 'daily.';
const PRE_RESTORE_PREFIX = 'pre-restore.';
const TS_FORMAT_RE = /^tasker\.(\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})\.backup\.db$/;
const DAILY_FORMAT_RE = /^tasker\.daily\.(\d{4}-\d{2}-\d{2})\.backup\.db$/;

export interface BackupInfo {
  filePath: string;
  timestamp: Date;
  isDaily: boolean;
  fileSize: number;
}

/** Format a date as yyyy-MM-ddTHH-mm-ss (filesystem-safe) */
function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}-${pad(d.getMinutes())}-${pad(d.getSeconds())}`;
}

/** Format a date as yyyy-MM-dd */
function formatDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export class BackupManager {
  private backupDir: string;
  private db: TaskerDb;

  constructor(backupDir: string, db: TaskerDb) {
    this.backupDir = backupDir;
    this.db = db;
  }

  /** Create a backup before modifications. Silently fails. */
  createBackup(): void {
    try {
      this.ensureDir();
      const now = new Date();
      this.backupTo(this.versionPath(now));
      this.createDailyIfNeeded(now);
      this.rotate();
    } catch {
      // Backup failures should not block operations
    }
  }

  /** List available backups, newest first */
  listBackups(): BackupInfo[] {
    if (!existsSync(this.backupDir)) return [];

    const backups: BackupInfo[] = [];
    for (const name of readdirSync(this.backupDir)) {
      if (!name.endsWith(BACKUP_EXT)) continue;
      const info = this.parseBackupFile(join(this.backupDir, name));
      if (info) backups.push(info);
    }

    return backups.sort((a, b) => b.timestamp.getTime() - a.timestamp.getTime());
  }

  /** Restore from a specific backup. Creates a safety backup first. */
  restoreBackup(timestamp: Date, undoManager?: UndoManager): void {
    const backupPath = this.findByTimestamp(timestamp);
    if (!backupPath) throw new Error(`Backup from ${timestamp.toISOString()} not found`);

    // Safety backup before restore
    this.backupTo(this.preRestorePath(new Date()));

    // Restore
    this.restoreFrom(backupPath);

    // Clear undo history
    undoManager?.clearHistory();
  }

  private ensureDir(): void {
    if (!existsSync(this.backupDir)) {
      mkdirSync(this.backupDir, { recursive: true });
    }
  }

  private backupTo(dest: string): void {
    const raw = getRawDb(this.db);
    // Flush WAL to main database file, then copy synchronously
    raw.pragma('wal_checkpoint(TRUNCATE)');
    const dbPath = getDbPath(this.db);
    copyFileSync(dbPath, dest);
  }

  private restoreFrom(source: string): void {
    const raw = getRawDb(this.db);
    // Attach the backup, copy all tables into main, then detach
    const safePath = source.replace(/'/g, "''");
    raw.exec(`ATTACH DATABASE '${safePath}' AS backup_src`);
    try {
      const tables = raw.prepare(
        "SELECT name FROM backup_src.sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'",
      ).all() as Array<{ name: string }>;

      const doRestore = raw.transaction(() => {
        for (const { name } of tables) {
          raw.exec(`DELETE FROM main."${name}"`);
          raw.exec(`INSERT INTO main."${name}" SELECT * FROM backup_src."${name}"`);
        }
      });
      doRestore();
    } finally {
      raw.exec('DETACH DATABASE backup_src');
    }
  }

  private createDailyIfNeeded(now: Date): void {
    const path = this.dailyPath(now);
    if (existsSync(path)) return;
    this.backupTo(path);
  }

  private rotate(): void {
    if (!existsSync(this.backupDir)) return;
    this.rotateVersionBackups();
    this.rotateDailyBackups();
  }

  private rotateVersionBackups(): void {
    const files = readdirSync(this.backupDir)
      .filter(f => f.endsWith(BACKUP_EXT) && !f.includes(DAILY_PREFIX) && !f.includes(PRE_RESTORE_PREFIX))
      .map(f => ({ name: f, info: this.parseBackupFile(join(this.backupDir, f)) }))
      .filter((x): x is { name: string; info: BackupInfo } => x.info !== null)
      .sort((a, b) => b.info.timestamp.getTime() - a.info.timestamp.getTime());

    for (const backup of files.slice(MAX_VERSION_BACKUPS)) {
      this.tryDelete(backup.info.filePath);
    }
  }

  private rotateDailyBackups(): void {
    const cutoff = new Date();
    cutoff.setDate(cutoff.getDate() - MAX_DAILY_BACKUP_DAYS);

    const files = readdirSync(this.backupDir)
      .filter(f => f.includes(DAILY_PREFIX) && f.endsWith(BACKUP_EXT))
      .map(f => ({ name: f, info: this.parseBackupFile(join(this.backupDir, f)) }))
      .filter((x): x is { name: string; info: BackupInfo } => x.info !== null && x.info.timestamp < cutoff);

    for (const backup of files) {
      this.tryDelete(backup.info.filePath);
    }
  }

  private tryDelete(path: string): void {
    try { unlinkSync(path); } catch { /* ignore */ }
  }

  private findByTimestamp(timestamp: Date): string | null {
    if (!existsSync(this.backupDir)) return null;

    const vPath = this.versionPath(timestamp);
    if (existsSync(vPath)) return vPath;

    const dPath = this.dailyPath(timestamp);
    if (existsSync(dPath)) return dPath;

    return null;
  }

  private parseBackupFile(filePath: string): BackupInfo | null {
    const name = basename(filePath);

    const vMatch = TS_FORMAT_RE.exec(name);
    if (vMatch) {
      const ts = vMatch[1]!.replace(/T(\d{2})-(\d{2})-(\d{2})$/, 'T$1:$2:$3');
      const d = new Date(ts);
      if (!isNaN(d.getTime())) {
        const stat = statSync(filePath);
        return { filePath, timestamp: d, isDaily: false, fileSize: stat.size };
      }
    }

    const dMatch = DAILY_FORMAT_RE.exec(name);
    if (dMatch) {
      const d = new Date(dMatch[1]! + 'T00:00:00');
      if (!isNaN(d.getTime())) {
        const stat = statSync(filePath);
        return { filePath, timestamp: d, isDaily: true, fileSize: stat.size };
      }
    }

    return null;
  }

  private versionPath(d: Date): string {
    return join(this.backupDir, `tasker.${formatTimestamp(d)}${BACKUP_EXT}`);
  }

  private dailyPath(d: Date): string {
    return join(this.backupDir, `tasker.${DAILY_PREFIX}${formatDate(d)}${BACKUP_EXT}`);
  }

  private preRestorePath(d: Date): string {
    return join(this.backupDir, `tasker.${PRE_RESTORE_PREFIX}${formatTimestamp(d)}${BACKUP_EXT}`);
  }
}
