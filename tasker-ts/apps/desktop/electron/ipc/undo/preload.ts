import type { TryResult } from '@utils/try.js';
import {
  UNDO_UNDO,
  UNDO_REDO,
  UNDO_CAN_UNDO,
  UNDO_CAN_REDO,
  UNDO_RELOAD,
} from './channels.js';

export const undoInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [UNDO_UNDO]: (() =>
    ipcRenderer.invoke(UNDO_UNDO)) as () => TryResult<string | null>,

  [UNDO_REDO]: (() =>
    ipcRenderer.invoke(UNDO_REDO)) as () => TryResult<string | null>,

  [UNDO_CAN_UNDO]: (() =>
    ipcRenderer.invoke(UNDO_CAN_UNDO)) as () => TryResult<boolean>,

  [UNDO_CAN_REDO]: (() =>
    ipcRenderer.invoke(UNDO_CAN_REDO)) as () => TryResult<boolean>,

  [UNDO_RELOAD]: (() =>
    ipcRenderer.invoke(UNDO_RELOAD)) as () => TryResult<void>,
});
