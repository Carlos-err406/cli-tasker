import { app, ipcMain } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDb, getDefaultDbPath, UndoManager } from '@tasker/core';
import registerIPCs from './ipc/register.js';
import { createTray, getPopupWindow } from './lib/tray.js';
import { startDbWatcher, stopDbWatcher } from './lib/watcher.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

process.env['APP_ROOT'] = path.join(__dirname, '..');
process.env['VITE_PUBLIC'] = process.env['VITE_DEV_SERVER_URL']
  ? path.join(process.env['APP_ROOT'], 'public')
  : path.join(process.env['APP_ROOT'], 'dist');

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
    createTray();
    console.log('[tasker-desktop] Tray created. Look for icon in menu bar.');

    // Watch for external database changes
    startDbWatcher(getPopupWindow);
  });

  app.on('window-all-closed', () => {
    // Don't quit on window close - tray app stays running
  });

  app.on('before-quit', () => {
    stopDbWatcher();
  });
}
