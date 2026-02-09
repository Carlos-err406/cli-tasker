import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import { deleteTasks, clearTasks, getAllTasks, getTaskById } from '@tasker/core';
import * as out from '../output.js';
import { resolveListFilter, $try } from '../helpers.js';

export function createDeleteCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('delete')
    .description('Delete one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to delete')
    .action((taskIds: string[]) => $try(() => {
      const oldTasks = taskIds.map(id => getTaskById(db, id)).filter((t): t is NonNullable<typeof t> => t !== null);
      const result = deleteTasks(db, taskIds);
      out.printBatchResults(result);

      if (oldTasks.length > 0) {
        undo.beginBatch(`Delete ${taskIds.length} task(s)`);
        for (const old of oldTasks) {
          undo.recordCommand({
            $type: 'delete',
            deletedTask: old,
            executedAt: new Date().toISOString(),
          });
        }
        undo.endBatch();
        undo.saveHistory();
      }
    }));
}

export function createClearCommand(db: TaskerDb, undo: UndoManager): Command {
  return new Command('clear')
    .description('Delete all tasks from a list')
    .action((_opts: unknown, cmd: Command) => $try(() => {
      const g = cmd.optsWithGlobals() as { list?: string; all?: boolean };
      const listName = resolveListFilter(db, g.list, g.all ?? false);
      if (listName == null) {
        out.error('Please specify a list with -l <list-name>');
        return;
      }

      const tasksToClear = getAllTasks(db, listName);
      const count = clearTasks(db, listName);

      if (tasksToClear.length > 0) {
        undo.recordCommand({
          $type: 'clear',
          listName,
          clearedTasks: tasksToClear,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }

      out.success(`Cleared ${count} task(s) from '${listName}'`);
    }));
}
