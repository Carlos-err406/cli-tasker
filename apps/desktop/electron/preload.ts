import { contextBridge, ipcRenderer } from 'electron';
import { tasksInvokerFactory } from './ipc/tasks/preload.js';
import { listsInvokerFactory } from './ipc/lists/preload.js';
import { undoInvokerFactory } from './ipc/undo/preload.js';
import { windowInvokerFactory } from './ipc/window/preload.js';

contextBridge.exposeInMainWorld('ipc', {
  ...tasksInvokerFactory(ipcRenderer),
  ...listsInvokerFactory(ipcRenderer),
  ...undoInvokerFactory(ipcRenderer),
  ...windowInvokerFactory(ipcRenderer),
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
});
