import { sqliteTable, text, integer, index } from 'drizzle-orm/sqlite-core';

export const undoHistory = sqliteTable('undo_history', {
  id: integer('id').primaryKey({ autoIncrement: true }),
  stackType: text('stack_type', { enum: ['undo', 'redo'] }).notNull(),
  commandJson: text('command_json').notNull(),
  createdAt: text('created_at').notNull(),
}, (table) => [
  index('idx_undo_stack_type').on(table.stackType),
]);
