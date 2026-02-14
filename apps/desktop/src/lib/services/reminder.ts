import type { ReminderSyncSettings, SyncStatus } from '../../../electron/lib/reminder-sync/types.js';
import { IPC } from './ipc.js';

async function unwrap<T>(promise: Promise<[{ message: string } | null, T | null]>): Promise<T> {
  const [err, data] = await promise;
  if (err) throw new Error(err.message);
  return data as T;
}

export async function getReminderSettings(): Promise<ReminderSyncSettings> {
  return unwrap(IPC['reminder:getSettings']());
}

export async function setReminderSettings(settings: ReminderSyncSettings): Promise<SyncStatus> {
  return unwrap(IPC['reminder:setSettings'](settings));
}

export async function syncRemindersNow(): Promise<SyncStatus> {
  return unwrap(IPC['reminder:syncNow']());
}

export async function getReminderSyncStatus(): Promise<SyncStatus> {
  return unwrap(IPC['reminder:getStatus']());
}
