import type { BrowserWindow } from 'electron';
import type { IPCContext } from './types.js';
import { tasksRegister } from './tasks/main.js';
import { listsRegister } from './lists/main.js';
import { undoRegister } from './undo/main.js';
import { windowRegister } from './window/main.js';
import { reminderRegister } from './reminder/main.js';

export default function registerIPCs(
  ipcMain: Electron.IpcMain,
  widget: BrowserWindow | null,
  ctx: IPCContext,
): void {
  [tasksRegister, listsRegister, undoRegister, windowRegister, reminderRegister].forEach((fn) =>
    fn(ipcMain, widget, ctx),
  );
}
