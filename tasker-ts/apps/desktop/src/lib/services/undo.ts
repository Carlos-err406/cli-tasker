import { IPC } from './ipc.js';

async function unwrap<T>(promise: Promise<[{ message: string } | null, T | null]>): Promise<T> {
  const [err, data] = await promise;
  if (err) throw new Error(err.message);
  return data as T;
}

export async function undo(): Promise<string | null> {
  return unwrap(IPC['undo:undo']());
}

export async function redo(): Promise<string | null> {
  return unwrap(IPC['undo:redo']());
}

export async function canUndo(): Promise<boolean> {
  return unwrap(IPC['undo:canUndo']());
}

export async function canRedo(): Promise<boolean> {
  return unwrap(IPC['undo:canRedo']());
}

export async function reloadUndoHistory(): Promise<void> {
  return unwrap(IPC['undo:reload']());
}
