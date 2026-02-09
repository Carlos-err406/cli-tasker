import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { renameTask, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createRenameCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('rename')
    .description('Rename a task')
    .argument('<taskId>', 'The task ID to rename')
    .argument('<description>', 'The new task description')
    .action((taskId: string, description: string) => $try(() => {
      const old = getTaskById(db, taskId);
      const result = renameTask(db, taskId, description);
      out.printResult(result);

      if (old && result.type === 'success') {
        undo.recordCommand({
          $type: 'rename',
          taskId,
          oldDescription: old.description,
          newDescription: description,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
    }));
}
