import { sqliteTable, text, primaryKey, check } from 'drizzle-orm/sqlite-core';
import { sql } from 'drizzle-orm';
import { tasks } from './tasks.js';

export const taskDependencies = sqliteTable('task_dependencies', {
  taskId: text('task_id').notNull().references(() => tasks.id, { onDelete: 'cascade' }),
  blocksTaskId: text('blocks_task_id').notNull().references(() => tasks.id, { onDelete: 'cascade' }),
}, (table) => [
  primaryKey({ columns: [table.taskId, table.blocksTaskId] }),
  check('no_self_block', sql`${table.taskId} != ${table.blocksTaskId}`),
]);
