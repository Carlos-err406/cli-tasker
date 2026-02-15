import {
  getAllListNames,
  createList,
  deleteList,
  renameList,
  reorderList,
  isListCollapsed,
  setListCollapsed,
  isListHideCompleted,
  setListHideCompleted,
  getDefaultList,
  getListIndex,
  getSortedTasks,
  getTrash,
} from '@tasker/core';
import $try from '@utils/try.js';
import type { IPCRegisterFunction } from '../types.js';
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
import { log } from './utils.js';

export const listsRegister: IPCRegisterFunction = (ipcMain, _widget, { db, undo }) => {
  ipcMain.handle(LISTS_GET_ALL, () => {
    log('getAll');
    return $try(() => getAllListNames(db));
  });

  ipcMain.handle(LISTS_CREATE, (_, name: string) => {
    log('create', name);
    return $try(() => {
      createList(db, name);
      return { type: 'success' as const, message: `Created list "${name}"` };
    });
  });

  ipcMain.handle(LISTS_DELETE, (_, name: string) => {
    log('delete', name);
    return $try(() => {
      const tasks = getSortedTasks(db, { listName: name });
      const trashedTasks = getTrash(db, name);
      const wasDefault = getDefaultList(db) === name;
      const originalIndex = getListIndex(db, name);
      deleteList(db, name);
      undo.recordCommand({
        $type: 'deleteList',
        listName: name,
        deletedTasks: tasks,
        trashedTasks,
        wasDefaultList: wasDefault,
        originalIndex,
        executedAt: new Date().toISOString(),
      });
      undo.saveHistory();
      return { type: 'success' as const, message: `Deleted list "${name}"` };
    });
  });

  ipcMain.handle(LISTS_RENAME, (_, oldName: string, newName: string) => {
    log('rename', oldName, newName);
    return $try(() => {
      const wasDefault = getDefaultList(db) === oldName;
      renameList(db, oldName, newName);
      undo.recordCommand({
        $type: 'renameList',
        oldName,
        newName,
        wasDefaultList: wasDefault,
        executedAt: new Date().toISOString(),
      });
      undo.saveHistory();
      return { type: 'success' as const, message: `Renamed "${oldName}" to "${newName}"` };
    });
  });

  ipcMain.handle(LISTS_REORDER, (_, name: string, newIndex: number) => {
    log('reorder', name, newIndex);
    return $try(() => {
      const oldIndex = getListIndex(db, name);
      reorderList(db, name, newIndex);
      undo.recordCommand({
        $type: 'reorderList',
        listName: name,
        oldIndex,
        newIndex,
        executedAt: new Date().toISOString(),
      });
      undo.saveHistory();
    });
  });

  ipcMain.handle(LISTS_IS_COLLAPSED, (_, name: string) => {
    return $try(() => isListCollapsed(db, name));
  });

  ipcMain.handle(LISTS_SET_COLLAPSED, (_, name: string, collapsed: boolean) => {
    return $try(() => setListCollapsed(db, name, collapsed));
  });

  ipcMain.handle(LISTS_IS_HIDE_COMPLETED, (_, name: string) => {
    return $try(() => isListHideCompleted(db, name));
  });

  ipcMain.handle(LISTS_SET_HIDE_COMPLETED, (_, name: string, hide: boolean) => {
    return $try(() => setListHideCompleted(db, name, hide));
  });

  ipcMain.handle(LISTS_GET_DEFAULT, () => {
    return $try(() => getDefaultList(db));
  });
};
