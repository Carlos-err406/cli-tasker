import type { BrowserWindow } from 'electron';
import type { TaskerDb, UndoManager } from '@tasker/core';
import type { tasksInvokerFactory } from './tasks/preload.js';
import type { listsInvokerFactory } from './lists/preload.js';
import type { undoInvokerFactory } from './undo/preload.js';
import type { windowInvokerFactory } from './window/preload.js';

export interface IPCContext {
  db: TaskerDb;
  undo: UndoManager;
}

export type IPCRegisterFunction = (
  ipcMain: Electron.IpcMain,
  widget: BrowserWindow | null,
  ctx: IPCContext,
) => void;

export type IPC = ReturnType<typeof tasksInvokerFactory> &
  ReturnType<typeof listsInvokerFactory> &
  ReturnType<typeof undoInvokerFactory> &
  ReturnType<typeof windowInvokerFactory> & {
    onDbChanged: (callback: () => void) => () => void;
  };
