import type { LogEntry } from '../../lib/log-buffer.js';
import { LOGS_GET_HISTORY, LOGS_CLEAR } from './channels.js';

export const logsInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [LOGS_GET_HISTORY]: (() => ipcRenderer.invoke(LOGS_GET_HISTORY)) as () => Promise<LogEntry[]>,
  [LOGS_CLEAR]: (() => ipcRenderer.invoke(LOGS_CLEAR)) as () => Promise<void>,
});
