import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { setTaskPriority, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { parsePriorityArg, $try } from '../helpers.js';

export function createPriorityCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('priority')
    .description("Set or clear a task's priority")
    .argument('<taskId>', 'The task ID')
    .argument('<level>', "Priority level (high, medium, low, 1, 2, 3, or 'clear')")
    .action((taskId: string, level: string) => $try(() => {
      const priority = parsePriorityArg(level);

      const old = getTaskById(db, taskId);
      const result = setTaskPriority(db, taskId, priority);
      out.printResult(result);

      if (old && result.type === 'success') {
        undo.recordCommand({
          $type: 'metadata',
          taskId,
          oldDueDate: old.dueDate,
          newDueDate: old.dueDate,
          oldPriority: old.priority,
          newPriority: priority,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
    }));
}
