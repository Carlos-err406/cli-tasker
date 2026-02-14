import type { TaskerDb } from '@tasker/core';
import type { ReminderSyncSettings, SyncStatus } from './types.js';
import { getSettings, saveSettings } from './settings.js';
import { performSync } from './sync-engine.js';

export type { ReminderSyncSettings, SyncableReminder, SyncStatus } from './types.js';
export { getSettings, saveSettings } from './settings.js';

const log: typeof console.log = (...args) =>
  console.log('[REMINDER-SYNC]:', ...args);

let dbRef: TaskerDb | null = null;
let debounceTimer: ReturnType<typeof setTimeout> | null = null;
let pollTimer: ReturnType<typeof setInterval> | null = null;
let currentStatus: SyncStatus = {
  lastSyncAt: null,
  lastError: null,
  eventCount: 0,
};

const DEBOUNCE_MS = 2000;
const POLL_INTERVAL_MS = 60_000; // Check Reminders.app for completions every 60s

export function startReminderSync(db: TaskerDb): void {
  dbRef = db;
  const settings = getSettings();
  log(`initialized (enabled: ${settings.enabled}, calendar: "${settings.listPrefix}")`);
  if (settings.enabled) {
    syncNow(db).then((status) => {
      if (status.lastError) {
        log('initial sync failed:', status.lastError);
      }
    }).catch((err) => {
      log('initial sync error:', err);
    });
    startPolling(db);
  }
}

export function stopReminderSync(): void {
  stopPolling();
  if (debounceTimer) {
    clearTimeout(debounceTimer);
    debounceTimer = null;
  }
  dbRef = null;
  log('stopped');
}

function startPolling(db: TaskerDb): void {
  stopPolling();
  pollTimer = setInterval(() => {
    syncNow(db).catch((err) => {
      log('poll sync error:', err);
    });
  }, POLL_INTERVAL_MS);
}

function stopPolling(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

export function triggerSync(): void {
  if (!dbRef) return;
  const settings = getSettings();
  if (!settings.enabled) return;

  if (debounceTimer) {
    clearTimeout(debounceTimer);
  }

  const db = dbRef;
  debounceTimer = setTimeout(() => {
    debounceTimer = null;
    syncNow(db).catch((err) => {
      log('debounced sync error:', err);
    });
  }, DEBOUNCE_MS);
}

export async function syncNow(db: TaskerDb): Promise<SyncStatus> {
  const settings = getSettings();
  if (!settings.enabled) {
    return currentStatus;
  }
  currentStatus = await performSync(db, settings);
  return currentStatus;
}

export function getSyncStatus(): SyncStatus {
  return currentStatus;
}

export async function updateSettings(
  settings: ReminderSyncSettings,
): Promise<SyncStatus> {
  log('settings updated:', settings);
  saveSettings(settings);

  if (settings.enabled && dbRef) {
    currentStatus = await performSync(dbRef, settings);
    startPolling(dbRef);
  } else {
    stopPolling();
    currentStatus = { lastSyncAt: null, lastError: null, eventCount: 0 };
  }

  return currentStatus;
}
