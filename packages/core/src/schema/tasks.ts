import { sqliteTable, text, integer, index } from 'drizzle-orm/sqlite-core';
import type { TaskStatus } from '../types/task-status.js';
import type { Priority } from '../types/priority.js';
import { lists } from './lists.js';

export const tasks = sqliteTable('tasks', {
  id: text('id').primaryKey(),
  description: text('description').notNull(),
  status: integer('status').$type<TaskStatus>().default(0),
  createdAt: text('created_at').notNull(),
  listName: text('list_name').notNull().references(() => lists.name, {
    onUpdate: 'cascade',
    onDelete: 'cascade',
  }),
  dueDate: text('due_date'),
  priority: integer('priority').$type<Priority>(),
  /** JSON array of strings, stored as TEXT */
  tags: text('tags'),
  isTrashed: integer('is_trashed').default(0),
  /** Highest value = newest. Display uses ORDER BY sort_order DESC */
  sortOrder: integer('sort_order').default(0),
  /** Frozen at completion time for "X days late" calculation */
  completedAt: text('completed_at'),
  parentId: text('parent_id').references((): any => tasks.id, {
    onDelete: 'cascade',
  }),
}, (table) => [
  index('idx_tasks_list_name').on(table.listName),
  index('idx_tasks_is_trashed').on(table.isTrashed),
  index('idx_tasks_sort').on(table.status, table.priority, table.dueDate, table.sortOrder),
  index('idx_tasks_parent_id').on(table.parentId),
]);
