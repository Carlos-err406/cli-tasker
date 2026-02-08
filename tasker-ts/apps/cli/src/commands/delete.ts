import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { deleteTasks, clearTasks } from '@tasker/core';
import * as out from '../output.js';
import { resolveListFilter } from '../helpers.js';

export function createDeleteCommand(db: TaskerDb): Command {
  return new Command('delete')
    .description('Delete one or more tasks')
    .argument('<taskIds...>', 'The id(s) of the task(s) to delete')
    .action((taskIds: string[]) => {
      try {
        out.printBatchResults(deleteTasks(db, taskIds));
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}

export function createClearCommand(db: TaskerDb): Command {
  return new Command('clear')
    .description('Delete all tasks from a list')
    .action((_opts: unknown, cmd: Command) => {
      try {
        const g = cmd.optsWithGlobals() as { list?: string; all?: boolean };
        const listName = resolveListFilter(db, g.list, g.all ?? false);
        if (listName == null) {
          out.error('Please specify a list with -l <list-name>');
          return;
        }
        const count = clearTasks(db, listName);
        out.success(`Cleared ${count} task(s) from '${listName}'`);
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
