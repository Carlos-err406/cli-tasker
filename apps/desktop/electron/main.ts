import { app, ipcMain } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDb, getDefaultDbPath, UndoManager } from '@tasker/core';
import registerIPCs from './ipc/register.js';
import { createTray, getPopupWindow, openPopupWithSearch } from './lib/tray.js';
import { startDbWatcher, stopDbWatcher } from './lib/watcher.js';
import { startReminderSync, stopReminderSync } from './lib/reminder-sync/index.js';
import { startDueDateNotifier, stopDueDateNotifier } from './lib/due-date-notifier.js';
import { initLogCapture } from './lib/log-buffer.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Use a different app name in dev to avoid single-instance conflict with installed app
if (process.env['VITE_DEV_SERVER_URL']) {
  app.setName('Tasker (Dev)');
  app.setPath('userData', path.join(app.getPath('appData'), 'Tasker (Dev)'));
}

process.env['APP_ROOT'] = path.join(__dirname, '..');
process.env['VITE_PUBLIC'] = process.env['VITE_DEV_SERVER_URL']
  ? path.join(process.env['APP_ROOT'], 'public')
  : path.join(process.env['APP_ROOT'], 'dist');

// Capture logs before anything else runs
initLogCapture();

// Prevent multiple instances
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    const popup = getPopupWindow();
    if (popup) {
      popup.show();
      popup.focus();
    }
  });

  app.whenReady().then(() => {
    console.log('[tasker-desktop] App ready, initializing...');
    // Hide from dock on macOS (menu bar app)
    if (process.platform === 'darwin') {
      app.dock.hide();
    }

    // Initialize database and undo manager
    const dbPath = getDefaultDbPath();
    const db = createDb(dbPath);
    const undo = new UndoManager(db);

    // Register IPC handlers with shared context
    registerIPCs(ipcMain, null, { db, undo });

    // Create the tray icon
    console.log('[tasker-desktop] Creating tray icon...');
    createTray(db);
    console.log('[tasker-desktop] Tray created. Look for icon in menu bar.');

    // Watch for external database changes
    startDbWatcher(getPopupWindow);

    // Start reminder sync (reads settings, syncs if enabled)
    startReminderSync(db);

    // Start due-date notifications (reads settings, polls if enabled)
    startDueDateNotifier(db, {
      onNotificationClick: (searchQuery) => openPopupWithSearch(searchQuery),
    });
  });

  app.on('window-all-closed', () => {
    // Don't quit on window close - tray app stays running
  });

  app.on('before-quit', () => {
    stopDbWatcher();
    stopReminderSync();
    stopDueDateNotifier();
  });
}
