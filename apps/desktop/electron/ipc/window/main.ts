import { app, BrowserWindow } from 'electron';
import type { IPCRegisterFunction } from '../types.js';
import {
  WINDOW_HIDE,
  WINDOW_SHOW,
  WINDOW_TOGGLE_DEV_TOOLS,
  APP_QUIT,
} from './channels.js';
import { log } from './utils.js';

export const windowRegister: IPCRegisterFunction = (ipcMain, _widget, _ctx) => {
  ipcMain.handle(WINDOW_HIDE, (event) => {
    log('hide');
    const win = BrowserWindow.fromWebContents(event.sender);
    if (win) win.hide();
  });

  ipcMain.handle(WINDOW_SHOW, (event) => {
    log('show');
    const win = BrowserWindow.fromWebContents(event.sender);
    if (win) {
      win.show();
      win.focus();
    }
  });

  ipcMain.handle(WINDOW_TOGGLE_DEV_TOOLS, (event) => {
    log('toggleDevTools');
    const win = BrowserWindow.fromWebContents(event.sender);
    if (win) win.webContents.toggleDevTools();
  });

  ipcMain.handle(APP_QUIT, () => {
    log('quit');
    app.quit();
  });
};
