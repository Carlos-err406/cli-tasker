import fs from 'node:fs';
import path from 'node:path';
import { app } from 'electron';
import { getConfig, setConfig } from '@tasker/core';
import type { TaskerDb } from '@tasker/core';

const log: typeof console.log = (...args) =>
  console.log('[MIGRATE]:', ...args);

interface SettingsMapping {
  file: string;
  configKey: string;
  defaults: Record<string, unknown>;
}

const SETTINGS_MAP: SettingsMapping[] = [
  {
    file: 'window-position.json',
    configKey: 'desktop.window_position',
    defaults: {},
  },
  {
    file: 'reminder-sync.json',
    configKey: 'desktop.reminder_sync',
    defaults: { enabled: false, listPrefix: 'Tasker' },
  },
  {
    file: 'due-date-notifier.json',
    configKey: 'desktop.due_date_notifier',
    defaults: { enabled: true },
  },
];

/**
 * One-time migration: reads old JSON settings files from userData,
 * writes their values into the SQLite config table, then deletes the files.
 */
export function migrateJsonSettings(db: TaskerDb): void {
  let userDataDir: string;
  try {
    userDataDir = app.getPath('userData');
  } catch {
    return; // Can't locate userData, skip migration
  }

  for (const { file, configKey, defaults } of SETTINGS_MAP) {
    try {
      // Already migrated?
      if (getConfig(db, configKey) !== null) continue;

      const filePath = path.join(userDataDir, file);
      if (fs.existsSync(filePath)) {
        const raw = fs.readFileSync(filePath, 'utf8');
        const data = { ...defaults, ...JSON.parse(raw) };
        setConfig(db, configKey, JSON.stringify(data));
        fs.unlinkSync(filePath);
        log(`migrated ${file} → ${configKey}`);
      }
    } catch (err) {
      log(`failed to migrate ${file}:`, err);
    }
  }
}
