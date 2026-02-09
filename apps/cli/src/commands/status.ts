import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { TaskStatus, setStatuses, getTaskById, statusLabel } from '@tasker/core';
import * as out from '../output.js';
import { parseStatus, $try } from '../helpers.js';

export function createStatusCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('status')
    .description('Set the status of one or more tasks')
    .argument('<status>', 'The status to set: pending, in-progress, done')
    .argument('<taskIds...>', 'The id(s) of the task(s)')
    .action((statusStr: string, taskIds: string[]) => $try(() => {
      const status = parseStatus(statusStr);
      if (status == null) {
        out.error(`Unknown status: '${statusStr}'. Use: pending, in-progress, done`);
        return;
      }

      const oldTasks = taskIds.map(id => getTaskById(db, id)).filter((t): t is NonNullable<typeof t> => t !== null);
      const result = setStatuses(db, taskIds, status);
      out.printBatchResults(result);

      if (oldTasks.length > 0) {
        undo.beginBatch(`Set ${taskIds.length} task(s) to ${statusLabel(status)}`);
        for (const old of oldTasks) {
          if (old.status !== status) {
            undo.recordCommand({
              $type: 'set-status',
              taskId: old.id,
              oldStatus: old.status,
              newStatus: status,
              executedAt: new Date().toISOString(),
            });
          }
        }
        undo.endBatch();
        undo.saveHistory();
      }
    }));
}

export function createWipCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('wip')
    .description('Mark tasks as in-progress')
    .argument('<taskIds...>', 'The id(s) of the task(s)')
    .action((taskIds: string[]) => $try(() => {
      const oldTasks = taskIds.map(id => getTaskById(db, id)).filter((t): t is NonNullable<typeof t> => t !== null);
      const result = setStatuses(db, taskIds, TaskStatus.InProgress);
      out.printBatchResults(result);

      if (oldTasks.length > 0) {
        undo.beginBatch(`Set ${taskIds.length} task(s) to in-progress`);
        for (const old of oldTasks) {
          if (old.status !== TaskStatus.InProgress) {
            undo.recordCommand({
              $type: 'set-status',
              taskId: old.id,
              oldStatus: old.status,
              newStatus: TaskStatus.InProgress,
              executedAt: new Date().toISOString(),
            });
          }
        }
        undo.endBatch();
        undo.saveHistory();
      }
    }));
}
