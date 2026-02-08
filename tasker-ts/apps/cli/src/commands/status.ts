import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { TaskStatus, setStatuses } from '@tasker/core';
import * as out from '../output.js';
import { parseStatus } from '../helpers.js';

export function createStatusCommand(db: TaskerDb): Command {
  return new Command('status')
    .description('Set the status of one or more tasks')
    .argument('<status>', 'The status to set: pending, in-progress, done')
    .argument('<taskIds...>', 'The id(s) of the task(s)')
    .action((statusStr: string, taskIds: string[]) => {
      try {
        const status = parseStatus(statusStr);
        if (status == null) {
          out.error(`Unknown status: '${statusStr}'. Use: pending, in-progress, done`);
          return;
        }
        out.printBatchResults(setStatuses(db, taskIds, status));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}

export function createWipCommand(db: TaskerDb): Command {
  return new Command('wip')
    .description('Mark tasks as in-progress')
    .argument('<taskIds...>', 'The id(s) of the task(s)')
    .action((taskIds: string[]) => {
      try {
        out.printBatchResults(setStatuses(db, taskIds, TaskStatus.InProgress));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
