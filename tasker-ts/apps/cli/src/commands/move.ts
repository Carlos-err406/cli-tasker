import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { moveTask } from '@tasker/core';
import * as out from '../output.js';

export function createMoveCommand(db: TaskerDb): Command {
  return new Command('move')
    .description('Move a task to a different list')
    .argument('<taskId>', 'The task ID to move')
    .argument('<targetList>', 'The list to move the task to')
    .action((taskId: string, targetList: string) => {
      try {
        out.printResult(moveTask(db, taskId, targetList));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
