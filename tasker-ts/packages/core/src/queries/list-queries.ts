/**
 * List management operations.
 */

import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';
import type { ListName } from '../types/task.js';
import type { Task } from '../types/task.js';

const DEFAULT_LIST = 'tasks';

/** Get all list names ordered by sort_order */
export function getAllListNames(db: TaskerDb): string[] {
  const raw = getRawDb(db);
  const rows = raw.prepare('SELECT name FROM lists ORDER BY sort_order').all() as any[];
  const names = rows.map(r => r.name as string);

  // Ensure "tasks" default list is always first
  if (names.length === 0 || !names.includes(DEFAULT_LIST)) {
    return [DEFAULT_LIST, ...names.filter(n => n !== DEFAULT_LIST)];
  }
  return names;
}

/** Check if a list has any non-trashed tasks */
export function listHasTasks(db: TaskerDb, listName: ListName): boolean {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT COUNT(*) as cnt FROM tasks WHERE list_name = ? AND is_trashed = 0').get(listName) as any;
  return row.cnt > 0;
}

/** Check if a list exists */
export function listExists(db: TaskerDb, listName: ListName): boolean {
  if (listName === DEFAULT_LIST) return true;
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT COUNT(*) as cnt FROM lists WHERE name = ?').get(listName) as any;
  return row.cnt > 0;
}

/** Create a new list */
export function createList(db: TaskerDb, listName: ListName): void {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT MAX(sort_order) as max_order FROM lists').get() as any;
  const maxOrder = (row?.max_order as number | null) ?? -1;
  raw.prepare('INSERT INTO lists (name, sort_order) VALUES (?, ?)').run(listName, maxOrder + 1);
}

/** Delete a list (CASCADE deletes all tasks in it) */
export function deleteList(db: TaskerDb, listName: ListName): void {
  const raw = getRawDb(db);
  raw.prepare('DELETE FROM lists WHERE name = ?').run(listName);
}

/** Rename a list (ON UPDATE CASCADE handles tasks) */
export function renameList(db: TaskerDb, oldName: ListName, newName: ListName): void {
  const raw = getRawDb(db);
  raw.prepare('UPDATE lists SET name = ? WHERE name = ?').run(newName, oldName);
}

/** Check if a list is collapsed (for TUI/GUI state) */
export function isListCollapsed(db: TaskerDb, listName: ListName): boolean {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT is_collapsed FROM lists WHERE name = ?').get(listName) as any;
  return row?.is_collapsed === 1;
}

/** Set a list's collapsed state */
export function setListCollapsed(db: TaskerDb, listName: ListName, collapsed: boolean): void {
  const raw = getRawDb(db);
  raw.prepare('UPDATE lists SET is_collapsed = ? WHERE name = ?').run(collapsed ? 1 : 0, listName);
}

/** Reorder a list within the list order */
export function reorderList(db: TaskerDb, listName: ListName, newIndex: number): void {
  const raw = getRawDb(db);
  const allNames = getAllListNames(db);
  const currentIndex = allNames.indexOf(listName);
  if (currentIndex < 0) return;

  const clamped = Math.max(0, Math.min(newIndex, allNames.length - 1));
  if (currentIndex === clamped) return;

  allNames.splice(currentIndex, 1);
  allNames.splice(clamped, 0, listName);

  const run = raw.transaction(() => {
    for (let i = 0; i < allNames.length; i++) {
      raw.prepare('UPDATE lists SET sort_order = ? WHERE name = ?').run(i, allNames[i]);
    }
  });
  run();
}

/** Get the index of a list in the sort order */
export function getListIndex(db: TaskerDb, listName: ListName): number {
  return getAllListNames(db).indexOf(listName);
}
