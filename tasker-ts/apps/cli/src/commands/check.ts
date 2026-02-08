import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { TaskStatus, setStatuses } from '@tasker/core';
import * as out from '../output.js';

export function createCheckCommand(db: TaskerDb): Command {
  return new Command('check')
    .description('Check one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to check')
    .action((taskIds: string[]) => {
      try {
        out.printBatchResults(setStatuses(db, taskIds, TaskStatus.Done));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}

export function createUncheckCommand(db: TaskerDb): Command {
  return new Command('uncheck')
    .description('Uncheck one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to uncheck')
    .action((taskIds: string[]) => {
      try {
        out.printBatchResults(setStatuses(db, taskIds, TaskStatus.Pending));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
