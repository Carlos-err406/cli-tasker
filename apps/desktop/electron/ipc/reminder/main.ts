import {
  getSettings,
  updateSettings,
  syncNow,
  getSyncStatus,
} from '../../lib/reminder-sync/index.js';
import type { ReminderSyncSettings } from '../../lib/reminder-sync/index.js';
import $try from '@utils/try.js';
import type { IPCRegisterFunction } from '../types.js';
import {
  REMINDER_GET_SETTINGS,
  REMINDER_SET_SETTINGS,
  REMINDER_SYNC_NOW,
  REMINDER_GET_STATUS,
} from './channels.js';
import { log } from './utils.js';

export const reminderRegister: IPCRegisterFunction = (ipcMain, _widget, { db }) => {
  ipcMain.handle(REMINDER_GET_SETTINGS, () => {
    log('getSettings');
    return $try(() => getSettings(db));
  });

  ipcMain.handle(REMINDER_SET_SETTINGS, (_, settings: ReminderSyncSettings) => {
    log('setSettings', settings);
    return $try(() => updateSettings(db, settings));
  });

  ipcMain.handle(REMINDER_SYNC_NOW, () => {
    log('syncNow');
    return $try(() => syncNow(db));
  });

  ipcMain.handle(REMINDER_GET_STATUS, () => {
    log('getStatus');
    return $try(() => getSyncStatus());
  });
};
