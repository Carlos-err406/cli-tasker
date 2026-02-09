import { Command } from 'commander';
import chalk from 'chalk';
import type { TaskerDb } from '@tasker/core';
import type { Task } from '@tasker/core';
import {
  getAllListNames, getTrash, restoreFromTrash, clearTrash,
} from '@tasker/core';
import * as out from '../output.js';
import { resolveListFilter, $try } from '../helpers.js';

export function createTrashCommand(db: TaskerDb): Command {
  const trashCommand = new Command('trash')
    .description('Manage deleted tasks');

  trashCommand.addCommand(
    new Command('list')
      .description('List deleted tasks in trash')
      .action((_opts: unknown, cmd: Command) => $try(() => {
        const g = cmd.optsWithGlobals() as { list?: string; all?: boolean };
        const listName = resolveListFilter(db, g.list, g.all ?? false);

        if (listName == null) {
          const listNames = getAllListNames(db);
          for (const name of listNames) {
            console.log(chalk.bold.underline(name));
            displayTrash(getTrash(db, name));
            console.log();
          }
        } else {
          displayTrash(getTrash(db, listName));
        }
      })),
  );

  trashCommand.addCommand(
    new Command('restore')
      .description('Restore a task from trash')
      .argument('<taskId>', 'The id of the task to restore')
      .action((taskId: string) => $try(() => {
        out.printResult(restoreFromTrash(db, taskId));
      })),
  );

  trashCommand.addCommand(
    new Command('clear')
      .description('Permanently delete all tasks in trash')
      .action((_opts: unknown, cmd: Command) => $try(() => {
        const g = cmd.optsWithGlobals() as { list?: string; all?: boolean };
        const listName = resolveListFilter(db, g.list, g.all ?? false);
        const count = clearTrash(db, listName ?? undefined);
        out.success(`Permanently deleted ${count} task(s) from trash`);
      })),
  );

  return trashCommand;
}

function displayTrash(trash: Task[]): void {
  if (trash.length === 0) {
    out.info('Trash is empty');
    return;
  }

  for (const td of trash) {
    const lines = td.description.split('\n');
    const firstLine = chalk.bold(lines[0]);
    const restLines = lines.length > 1
      ? lines.slice(1).map(l => `\n          ${chalk.dim(l)}`).join('')
      : '';

    const checkbox = out.formatCheckbox(td.status);
    const taskId = chalk.dim(`(${td.id})`);
    console.log(`${taskId} ${checkbox} - ${firstLine}${restLines}`);
  }
}
