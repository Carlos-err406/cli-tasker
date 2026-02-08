/**
 * Key-value config storage operations.
 */

import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';

const DEFAULT_LIST_KEY = 'default_list';
const DEFAULT_LIST_VALUE = 'tasks';

/** Get a config value by key */
export function getConfig(db: TaskerDb, key: string): string | null {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT value FROM config WHERE key = ?').get(key) as { value: string } | undefined;
  return row?.value ?? null;
}

/** Set a config value */
export function setConfig(db: TaskerDb, key: string, value: string): void {
  const raw = getRawDb(db);
  raw.prepare('INSERT OR REPLACE INTO config (key, value) VALUES (?, ?)').run(key, value);
}

/** Get the default list name */
export function getDefaultList(db: TaskerDb): string {
  return getConfig(db, DEFAULT_LIST_KEY) ?? DEFAULT_LIST_VALUE;
}

/** Set the default list name */
export function setDefaultList(db: TaskerDb, listName: string): void {
  setConfig(db, DEFAULT_LIST_KEY, listName);
}
