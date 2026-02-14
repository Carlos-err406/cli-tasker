import fs from 'node:fs';
import path from 'node:path';
import { app } from 'electron';
import type { ReminderSyncSettings } from './types.js';
import { DEFAULT_SETTINGS } from './types.js';

const SETTINGS_FILE = 'reminder-sync.json';

function getUserDataDir(): string {
  try {
    return app.getPath('userData');
  } catch {
    return path.join(
      process.env['HOME'] || process.env['APPDATA'] || '.',
      '.tasker-desktop',
    );
  }
}

export function getSettings(): ReminderSyncSettings {
  try {
    const filePath = path.join(getUserDataDir(), SETTINGS_FILE);
    if (fs.existsSync(filePath)) {
      const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
      return {
        enabled: typeof data.enabled === 'boolean' ? data.enabled : DEFAULT_SETTINGS.enabled,
        listPrefix: typeof data.listPrefix === 'string' ? data.listPrefix : DEFAULT_SETTINGS.listPrefix,
      };
    }
  } catch {
    // ignore
  }
  return { ...DEFAULT_SETTINGS };
}

export function saveSettings(settings: ReminderSyncSettings): void {
  try {
    const dir = getUserDataDir();
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }
    fs.writeFileSync(
      path.join(dir, SETTINGS_FILE),
      JSON.stringify(settings, null, 2),
    );
  } catch {
    // ignore
  }
}
