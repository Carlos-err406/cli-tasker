import { getConfig, setConfig } from '@tasker/core';
import type { TaskerDb } from '@tasker/core';
import type { ReminderSyncSettings } from './types.js';
import { DEFAULT_SETTINGS } from './types.js';

const CONFIG_KEY = 'desktop.reminder_sync';

export function getSettings(db: TaskerDb): ReminderSyncSettings {
  try {
    const raw = getConfig(db, CONFIG_KEY);
    if (raw) {
      const data = JSON.parse(raw);
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

export function saveSettings(db: TaskerDb, settings: ReminderSyncSettings): void {
  setConfig(db, CONFIG_KEY, JSON.stringify(settings));
}
