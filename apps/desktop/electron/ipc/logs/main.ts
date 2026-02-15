import { getLogHistory, clearLogs, onLog } from '../../lib/log-buffer.js';
import { getPopupWindow } from '../../lib/tray.js';
import type { IPCRegisterFunction } from '../types.js';
import { LOGS_GET_HISTORY, LOGS_CLEAR, LOGS_ENTRY } from './channels.js';

export const logsRegister: IPCRegisterFunction = (ipcMain) => {
  ipcMain.handle(LOGS_GET_HISTORY, () => getLogHistory());
  ipcMain.handle(LOGS_CLEAR, () => clearLogs());

  onLog((entry) => {
    const win = getPopupWindow();
    if (win && !win.isDestroyed()) {
      win.webContents.send(LOGS_ENTRY, entry);
    }
  });
};
