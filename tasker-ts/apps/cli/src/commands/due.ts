import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { setTaskDueDate, parseDate } from '@tasker/core';
import * as out from '../output.js';

export function createDueCommand(db: TaskerDb): Command {
  return new Command('due')
    .description("Set or clear a task's due date")
    .argument('<taskId>', 'The task ID')
    .argument('<date>', "Due date (today, tomorrow, friday, jan15, +3d, or 'clear')")
    .action((taskId: string, dateStr: string) => {
      try {
        let dueDate: string | null;
        if (dateStr.toLowerCase() === 'clear') {
          dueDate = null;
        } else {
          const parsed = parseDate(dateStr);
          if (!parsed) {
            out.error(`Could not parse date: ${dateStr}`);
            return;
          }
          dueDate = parsed;
        }
        out.printResult(setTaskDueDate(db, taskId, dueDate));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
