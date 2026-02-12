import { IPC } from './ipc.js';

export function hideWindow() {
  return IPC['window:hide']();
}

export function showWindow() {
  return IPC['window:show']();
}

export function toggleDevTools() {
  return IPC['window:toggleDevTools']();
}

export function quitApp() {
  return IPC['app:quit']();
}

export function openExternal(url: string) {
  return IPC['shell:openExternal'](url);
}
