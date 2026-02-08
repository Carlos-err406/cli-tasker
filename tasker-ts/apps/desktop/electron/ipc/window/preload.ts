import {
  WINDOW_HIDE,
  WINDOW_SHOW,
  WINDOW_TOGGLE_DEV_TOOLS,
} from './channels.js';

export const windowInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [WINDOW_HIDE]: () => ipcRenderer.invoke(WINDOW_HIDE),
  [WINDOW_SHOW]: () => ipcRenderer.invoke(WINDOW_SHOW),
  [WINDOW_TOGGLE_DEV_TOOLS]: () => ipcRenderer.invoke(WINDOW_TOGGLE_DEV_TOOLS),
});
