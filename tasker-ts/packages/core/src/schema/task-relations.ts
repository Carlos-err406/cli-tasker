import { sqliteTable, text, primaryKey, check } from 'drizzle-orm/sqlite-core';
import { sql } from 'drizzle-orm';
import { tasks } from './tasks.js';

export const taskRelations = sqliteTable('task_relations', {
  taskId1: text('task_id_1').notNull().references(() => tasks.id, { onDelete: 'cascade' }),
  taskId2: text('task_id_2').notNull().references(() => tasks.id, { onDelete: 'cascade' }),
}, (table) => [
  primaryKey({ columns: [table.taskId1, table.taskId2] }),
  check('ordered_ids', sql`${table.taskId1} < ${table.taskId2}`),
  check('no_self_relate', sql`${table.taskId1} != ${table.taskId2}`),
]);
