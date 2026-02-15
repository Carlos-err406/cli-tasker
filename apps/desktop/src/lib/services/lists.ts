import { IPC } from './ipc.js';

async function unwrap<T>(promise: Promise<[{ message: string } | null, T | null]>): Promise<T> {
  const [err, data] = await promise;
  if (err) throw new Error(err.message);
  return data as T;
}

export async function getAllLists(): Promise<string[]> {
  return unwrap(IPC['lists:getAll']());
}

export async function createList(name: string) {
  return unwrap(IPC['lists:create'](name));
}

export async function deleteList(name: string) {
  return unwrap(IPC['lists:delete'](name));
}

export async function renameList(oldName: string, newName: string) {
  return unwrap(IPC['lists:rename'](oldName, newName));
}

export async function reorderList(name: string, newIndex: number) {
  return unwrap(IPC['lists:reorder'](name, newIndex));
}

export async function isListCollapsed(name: string): Promise<boolean> {
  return unwrap(IPC['lists:isCollapsed'](name));
}

export async function setListCollapsed(name: string, collapsed: boolean) {
  return unwrap(IPC['lists:setCollapsed'](name, collapsed));
}

export async function isListHideCompleted(name: string): Promise<boolean> {
  return unwrap(IPC['lists:isHideCompleted'](name));
}

export async function setListHideCompleted(name: string, hide: boolean) {
  return unwrap(IPC['lists:setHideCompleted'](name, hide));
}

export async function getDefaultList(): Promise<string> {
  return unwrap(IPC['lists:getDefault']());
}
