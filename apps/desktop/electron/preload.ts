import { contextBridge, ipcRenderer } from 'electron';
import { tasksInvokerFactory } from './ipc/tasks/preload.js';
import { listsInvokerFactory } from './ipc/lists/preload.js';
import { undoInvokerFactory } from './ipc/undo/preload.js';
import { windowInvokerFactory } from './ipc/window/preload.js';
import { reminderInvokerFactory } from './ipc/reminder/preload.js';
import { logsInvokerFactory } from './ipc/logs/preload.js';
import { LOGS_ENTRY } from './ipc/logs/channels.js';

contextBridge.exposeInMainWorld('ipc', {
  homePath: process.env.HOME ?? '/tmp',
  ...tasksInvokerFactory(ipcRenderer),
  ...listsInvokerFactory(ipcRenderer),
  ...undoInvokerFactory(ipcRenderer),
  ...windowInvokerFactory(ipcRenderer),
  ...reminderInvokerFactory(ipcRenderer),
  ...logsInvokerFactory(ipcRenderer),
  onLogEntry: (callback: (entry: import('./lib/log-buffer.js').LogEntry) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, entry: import('./lib/log-buffer.js').LogEntry) => callback(entry);
    ipcRenderer.on(LOGS_ENTRY, handler);
    return () => {
      ipcRenderer.removeListener(LOGS_ENTRY, handler);
    };
  },
  onDbChanged: (callback: () => void) => {
    ipcRenderer.on('db:changed', callback);
    return () => {
      ipcRenderer.removeListener('db:changed', callback);
    };
  },
  onPopupHidden: (callback: () => void) => {
    ipcRenderer.on('popup:hidden', callback);
    return () => {
      ipcRenderer.removeListener('popup:hidden', callback);
    };
  },
  onPopupShown: (callback: () => void) => {
    ipcRenderer.on('popup:shown', callback);
    return () => {
      ipcRenderer.removeListener('popup:shown', callback);
    };
  },
  onSetSearch: (callback: (query: string) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, query: string) => callback(query);
    ipcRenderer.on('set-search', handler);
    return () => {
      ipcRenderer.removeListener('set-search', handler);
    };
  },
});
