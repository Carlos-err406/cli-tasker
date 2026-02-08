import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { renameTask } from '@tasker/core';
import * as out from '../output.js';

export function createRenameCommand(db: TaskerDb): Command {
  return new Command('rename')
    .description('Rename a task')
    .argument('<taskId>', 'The task ID to rename')
    .argument('<description>', 'The new task description')
    .action((taskId: string, description: string) => {
      try {
        out.printResult(renameTask(db, taskId, description));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
