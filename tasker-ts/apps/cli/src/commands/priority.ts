import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { setTaskPriority } from '@tasker/core';
import * as out from '../output.js';
import { parsePriorityArg } from '../helpers.js';

export function createPriorityCommand(db: TaskerDb): Command {
  return new Command('priority')
    .description("Set or clear a task's priority")
    .argument('<taskId>', 'The task ID')
    .argument('<level>', "Priority level (high, medium, low, 1, 2, 3, or 'clear')")
    .action((taskId: string, level: string) => {
      try {
        const priority = parsePriorityArg(level);
        out.printResult(setTaskPriority(db, taskId, priority));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
