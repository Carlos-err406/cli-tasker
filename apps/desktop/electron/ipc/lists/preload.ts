import type { TryResult } from '@utils/try.js';
import {
  LISTS_GET_ALL,
  LISTS_CREATE,
  LISTS_DELETE,
  LISTS_RENAME,
  LISTS_REORDER,
  LISTS_IS_COLLAPSED,
  LISTS_SET_COLLAPSED,
  LISTS_IS_HIDE_COMPLETED,
  LISTS_SET_HIDE_COMPLETED,
  LISTS_GET_DEFAULT,
} from './channels.js';

export const listsInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [LISTS_GET_ALL]: (() =>
    ipcRenderer.invoke(LISTS_GET_ALL)) as () => TryResult<string[]>,

  [LISTS_CREATE]: ((name: string) =>
    ipcRenderer.invoke(LISTS_CREATE, name)) as (
    name: string,
  ) => TryResult<{ type: 'success'; message: string }>,

  [LISTS_DELETE]: ((name: string) =>
    ipcRenderer.invoke(LISTS_DELETE, name)) as (
    name: string,
  ) => TryResult<{ type: 'success'; message: string }>,

  [LISTS_RENAME]: ((oldName: string, newName: string) =>
    ipcRenderer.invoke(LISTS_RENAME, oldName, newName)) as (
    oldName: string,
    newName: string,
  ) => TryResult<{ type: 'success'; message: string }>,

  [LISTS_REORDER]: ((name: string, newIndex: number) =>
    ipcRenderer.invoke(LISTS_REORDER, name, newIndex)) as (
    name: string,
    newIndex: number,
  ) => TryResult<void>,

  [LISTS_IS_COLLAPSED]: ((name: string) =>
    ipcRenderer.invoke(LISTS_IS_COLLAPSED, name)) as (
    name: string,
  ) => TryResult<boolean>,

  [LISTS_SET_COLLAPSED]: ((name: string, collapsed: boolean) =>
    ipcRenderer.invoke(LISTS_SET_COLLAPSED, name, collapsed)) as (
    name: string,
    collapsed: boolean,
  ) => TryResult<void>,

  [LISTS_IS_HIDE_COMPLETED]: ((name: string) =>
    ipcRenderer.invoke(LISTS_IS_HIDE_COMPLETED, name)) as (
    name: string,
  ) => TryResult<boolean>,

  [LISTS_SET_HIDE_COMPLETED]: ((name: string, hide: boolean) =>
    ipcRenderer.invoke(LISTS_SET_HIDE_COMPLETED, name, hide)) as (
    name: string,
    hide: boolean,
  ) => TryResult<void>,

  [LISTS_GET_DEFAULT]: (() =>
    ipcRenderer.invoke(LISTS_GET_DEFAULT)) as () => TryResult<string>,
});
