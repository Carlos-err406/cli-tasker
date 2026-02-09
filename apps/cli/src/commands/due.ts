import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { setTaskDueDate, parseDate, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createDueCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('due')
    .description("Set or clear a task's due date")
    .argument('<taskId>', 'The task ID')
    .argument('<date>', "Due date (today, tomorrow, friday, jan15, +3d, or 'clear')")
    .action((taskId: string, dateStr: string) => $try(() => {
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

      const old = getTaskById(db, taskId);
      const result = setTaskDueDate(db, taskId, dueDate);
      out.printResult(result);

      if (old && result.type === 'success') {
        undo.recordCommand({
          $type: 'metadata',
          taskId,
          oldDueDate: old.dueDate,
          newDueDate: dueDate,
          oldPriority: old.priority,
          newPriority: old.priority,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
    }));
}
