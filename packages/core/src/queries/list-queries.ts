/**
 * List management operations.
 */

import { eq, and, count, max } from 'drizzle-orm';
import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';
import type { ListName } from '../types/task.js';
import { lists } from '../schema/lists.js';
import { tasks } from '../schema/tasks.js';

const DEFAULT_LIST = 'tasks';
const VALID_NAME_RE = /^[a-zA-Z0-9_-]+$/;

/** Check if a list name is valid (letters, numbers, hyphens, underscores) */
export function isValidListName(name: string): boolean {
  return name.length > 0 && VALID_NAME_RE.test(name);
}

/** Get all list names ordered by sort_order */
export function getAllListNames(db: TaskerDb): string[] {
  const rows = db.select({ name: lists.name }).from(lists).orderBy(lists.sortOrder).all();
  const names = rows.map(r => r.name);

  // Ensure "tasks" default list is always first
  if (names.length === 0 || !names.includes(DEFAULT_LIST)) {
    return [DEFAULT_LIST, ...names.filter(n => n !== DEFAULT_LIST)];
  }
  return names;
}

/** Check if a list has any non-trashed tasks */
export function listHasTasks(db: TaskerDb, listName: ListName): boolean {
  const row = db.select({ cnt: count() }).from(tasks).where(and(eq(tasks.listName, listName), eq(tasks.isTrashed, 0))).get();
  return (row?.cnt ?? 0) > 0;
}

/** Check if a list exists */
export function listExists(db: TaskerDb, listName: ListName): boolean {
  if (listName === DEFAULT_LIST) return true;
  const row = db.select({ cnt: count() }).from(lists).where(eq(lists.name, listName)).get();
  return (row?.cnt ?? 0) > 0;
}

/** Create a new list */
export function createList(db: TaskerDb, listName: ListName): void {
  const row = db.select({ maxOrder: max(lists.sortOrder) }).from(lists).get();
  const maxOrder = row?.maxOrder ?? -1;
  db.insert(lists).values({ name: listName, sortOrder: maxOrder + 1 }).run();
}

/** Delete a list (CASCADE deletes all tasks in it) */
export function deleteList(db: TaskerDb, listName: ListName): void {
  db.delete(lists).where(eq(lists.name, listName)).run();
}

/** Rename a list (ON UPDATE CASCADE handles tasks) */
export function renameList(db: TaskerDb, oldName: ListName, newName: ListName): void {
  db.update(lists).set({ name: newName }).where(eq(lists.name, oldName)).run();
}

/** Check if a list is collapsed (for TUI/GUI state) */
export function isListCollapsed(db: TaskerDb, listName: ListName): boolean {
  const row = db.select({ isCollapsed: lists.isCollapsed }).from(lists).where(eq(lists.name, listName)).get();
  return row?.isCollapsed === 1;
}

/** Set a list's collapsed state */
export function setListCollapsed(db: TaskerDb, listName: ListName, collapsed: boolean): void {
  db.update(lists).set({ isCollapsed: collapsed ? 1 : 0 }).where(eq(lists.name, listName)).run();
}

/** Check if a list hides completed tasks */
export function isListHideCompleted(db: TaskerDb, listName: ListName): boolean {
  const row = db.select({ hideCompleted: lists.hideCompleted }).from(lists).where(eq(lists.name, listName)).get();
  return row?.hideCompleted === 1;
}

/** Set a list's hide-completed state */
export function setListHideCompleted(db: TaskerDb, listName: ListName, hide: boolean): void {
  db.update(lists).set({ hideCompleted: hide ? 1 : 0 }).where(eq(lists.name, listName)).run();
}

/** Reorder a list within the list order */
export function reorderList(db: TaskerDb, listName: ListName, newIndex: number): void {
  const allNames = getAllListNames(db);
  const currentIndex = allNames.indexOf(listName);
  if (currentIndex < 0) return;

  const clamped = Math.max(0, Math.min(newIndex, allNames.length - 1));
  if (currentIndex === clamped) return;

  allNames.splice(currentIndex, 1);
  allNames.splice(clamped, 0, listName);

  // Use raw transaction for batch update efficiency
  const raw = getRawDb(db);
  const run = raw.transaction(() => {
    for (let i = 0; i < allNames.length; i++) {
      db.update(lists).set({ sortOrder: i }).where(eq(lists.name, allNames[i]!)).run();
    }
  });
  run();
}

/** Get the index of a list in the sort order */
export function getListIndex(db: TaskerDb, listName: ListName): number {
  return getAllListNames(db).indexOf(listName);
}
