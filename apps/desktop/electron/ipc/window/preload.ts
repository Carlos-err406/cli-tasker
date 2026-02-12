import {
  WINDOW_HIDE,
  WINDOW_SHOW,
  WINDOW_TOGGLE_DEV_TOOLS,
  APP_QUIT,
  SHELL_OPEN_EXTERNAL,
} from './channels.js';

export const windowInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [WINDOW_HIDE]: () => ipcRenderer.invoke(WINDOW_HIDE),
  [WINDOW_SHOW]: () => ipcRenderer.invoke(WINDOW_SHOW),
  [WINDOW_TOGGLE_DEV_TOOLS]: () => ipcRenderer.invoke(WINDOW_TOGGLE_DEV_TOOLS),
  [APP_QUIT]: () => ipcRenderer.invoke(APP_QUIT),
  [SHELL_OPEN_EXTERNAL]: (url: string) => ipcRenderer.invoke(SHELL_OPEN_EXTERNAL, url),
});
