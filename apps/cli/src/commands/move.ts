import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { moveTask, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createMoveCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('move')
    .description('Move a task to a different list')
    .argument('<taskId>', 'The task ID to move')
    .argument('<targetList>', 'The list to move the task to')
    .action((taskId: string, targetList: string) => $try(() => {
      const old = getTaskById(db, taskId);
      const result = moveTask(db, taskId, targetList);
      out.printResult(result);

      if (old && result.type === 'success') {
        undo.recordCommand({
          $type: 'move',
          taskId,
          sourceList: old.listName,
          targetList,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
    }));
}
