import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { TaskStatus, setStatuses, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createCheckCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('check')
    .description('Check one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to check')
    .action((taskIds: string[]) => $try(() => {
      const oldTasks = taskIds.map(id => getTaskById(db, id)).filter((t): t is NonNullable<typeof t> => t !== null);
      const result = setStatuses(db, taskIds, TaskStatus.Done);
      out.printBatchResults(result);

      if (oldTasks.length > 0) {
        undo.beginBatch(`Check ${taskIds.length} task(s)`);
        for (const old of oldTasks) {
          if (old.status !== TaskStatus.Done) {
            undo.recordCommand({
              $type: 'set-status',
              taskId: old.id,
              oldStatus: old.status,
              newStatus: TaskStatus.Done,
              executedAt: new Date().toISOString(),
            });
          }
        }
        undo.endBatch();
        undo.saveHistory();
      }
    }));
}

export function createUncheckCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('uncheck')
    .description('Uncheck one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to uncheck')
    .action((taskIds: string[]) => $try(() => {
      const oldTasks = taskIds.map(id => getTaskById(db, id)).filter((t): t is NonNullable<typeof t> => t !== null);
      const result = setStatuses(db, taskIds, TaskStatus.Pending);
      out.printBatchResults(result);

      if (oldTasks.length > 0) {
        undo.beginBatch(`Uncheck ${taskIds.length} task(s)`);
        for (const old of oldTasks) {
          if (old.status !== TaskStatus.Pending) {
            undo.recordCommand({
              $type: 'set-status',
              taskId: old.id,
              oldStatus: old.status,
              newStatus: TaskStatus.Pending,
              executedAt: new Date().toISOString(),
            });
          }
        }
        undo.endBatch();
        undo.saveHistory();
      }
    }));
}
