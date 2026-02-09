import { sqliteTable, text, integer } from 'drizzle-orm/sqlite-core';

export const lists = sqliteTable('lists', {
  name: text('name').primaryKey(),
  isCollapsed: integer('is_collapsed').default(0),
  /** Highest value = most recently created/moved */
  sortOrder: integer('sort_order').default(0),
});
