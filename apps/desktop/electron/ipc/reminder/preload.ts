import type { ReminderSyncSettings, SyncStatus } from '../../lib/reminder-sync/types.js';
import type { TryResult } from '@utils/try.js';
import {
  REMINDER_GET_SETTINGS,
  REMINDER_SET_SETTINGS,
  REMINDER_SYNC_NOW,
  REMINDER_GET_STATUS,
} from './channels.js';

export const reminderInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [REMINDER_GET_SETTINGS]: (() =>
    ipcRenderer.invoke(REMINDER_GET_SETTINGS)) as () => TryResult<ReminderSyncSettings>,

  [REMINDER_SET_SETTINGS]: ((settings: ReminderSyncSettings) =>
    ipcRenderer.invoke(REMINDER_SET_SETTINGS, settings)) as (
    settings: ReminderSyncSettings,
  ) => TryResult<SyncStatus>,

  [REMINDER_SYNC_NOW]: (() =>
    ipcRenderer.invoke(REMINDER_SYNC_NOW)) as () => TryResult<SyncStatus>,

  [REMINDER_GET_STATUS]: (() =>
    ipcRenderer.invoke(REMINDER_GET_STATUS)) as () => TryResult<SyncStatus>,
});
