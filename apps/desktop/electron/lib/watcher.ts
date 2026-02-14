import { BrowserWindow } from 'electron';
import { watch } from 'chokidar';
import { getDefaultDbPath } from '@tasker/core';
import { triggerSync } from './reminder-sync/index.js';

let watcher: ReturnType<typeof watch> | null = null;

export function startDbWatcher(getWindow: () => BrowserWindow | null): void {
  const dbPath = getDefaultDbPath();

  watcher = watch(dbPath, {
    persistent: true,
    ignoreInitial: true,
    // Debounce rapid changes (e.g. multiple writes in a transaction)
    awaitWriteFinish: {
      stabilityThreshold: 300,
      pollInterval: 100,
    },
  });

  watcher.on('change', () => {
    const win = getWindow();
    if (win && !win.isDestroyed()) {
      win.webContents.send('db:changed');
    }
    triggerSync();
  });
}

export function stopDbWatcher(): void {
  watcher?.close();
  watcher = null;
}
