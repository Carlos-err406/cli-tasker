/**
 * Key-value config storage operations.
 */

import { eq } from 'drizzle-orm';
import type { TaskerDb } from '../db.js';
import { config } from '../schema/index.js';

const DEFAULT_LIST_KEY = 'default_list';
const DEFAULT_LIST_VALUE = 'tasks';

/** Get a config value by key */
export function getConfig(db: TaskerDb, key: string): string | null {
  const row = db.select({ value: config.value }).from(config).where(eq(config.key, key)).get();
  return row?.value ?? null;
}

/** Set a config value */
export function setConfig(db: TaskerDb, key: string, value: string): void {
  db.insert(config).values({ key, value }).onConflictDoUpdate({ target: config.key, set: { value } }).run();
}

/** Get the default list name */
export function getDefaultList(db: TaskerDb): string {
  return getConfig(db, DEFAULT_LIST_KEY) ?? DEFAULT_LIST_VALUE;
}

/** Set the default list name */
export function setDefaultList(db: TaskerDb, listName: string): void {
  setConfig(db, DEFAULT_LIST_KEY, listName);
}
