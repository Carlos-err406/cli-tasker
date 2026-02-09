import $try from '@utils/try.js';
import type { IPCRegisterFunction } from '../types.js';
import {
  UNDO_UNDO,
  UNDO_REDO,
  UNDO_CAN_UNDO,
  UNDO_CAN_REDO,
  UNDO_RELOAD,
} from './channels.js';
import { log } from './utils.js';

export const undoRegister: IPCRegisterFunction = (ipcMain, _widget, { undo }) => {
  ipcMain.handle(UNDO_UNDO, () => {
    log('undo');
    return $try(() => undo.undo());
  });

  ipcMain.handle(UNDO_REDO, () => {
    log('redo');
    return $try(() => undo.redo());
  });

  ipcMain.handle(UNDO_CAN_UNDO, () => {
    return $try(() => undo.canUndo);
  });

  ipcMain.handle(UNDO_CAN_REDO, () => {
    return $try(() => undo.canRedo);
  });

  ipcMain.handle(UNDO_RELOAD, () => {
    log('reload history');
    return $try(() => undo.reloadHistory());
  });
};
