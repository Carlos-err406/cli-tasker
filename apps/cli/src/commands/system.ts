import { Command } from 'commander';
import chalk from 'chalk';
import type { TaskerDb } from '@tasker/core';
import { getAllListNames, getStats } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createSystemCommand(db: TaskerDb): Command {
  const systemCommand = new Command('system')
    .description('System information and diagnostics');

  systemCommand.addCommand(
    new Command('status')
      .description('Show status of all tasks and trash across all lists')
      .action(() => $try(() => {
        const listNames = getAllListNames(db);
        if (listNames.length === 0) {
          out.info('No lists found');
          return;
        }

        let totalTasks = 0;
        let totalPending = 0;
        let totalInProgress = 0;
        let totalDone = 0;
        let totalTrash = 0;

        console.log(chalk.bold.underline('Tasks'));
        console.log();

        for (const name of listNames) {
          const stats = getStats(db, name);
          totalTasks += stats.total;
          totalPending += stats.pending;
          totalInProgress += stats.inProgress;
          totalDone += stats.done;
          totalTrash += stats.trash;

          const doneLabel = stats.done > 0 ? chalk.green(`${stats.done} done`) : chalk.dim('0 done');
          const wipLabel = stats.inProgress > 0 ? chalk.yellow(`${stats.inProgress} in-progress`) : chalk.dim('0 in-progress');
          const pendingLabel = stats.pending > 0 ? chalk.gray(`${stats.pending} pending`) : chalk.dim('0 pending');
          const trashLabel = stats.trash > 0 ? chalk.red(`${stats.trash} in trash`) : chalk.dim('0 in trash');

          console.log(`  ${chalk.bold(name)}: ${wipLabel}, ${pendingLabel}, ${doneLabel}, ${trashLabel}`);
        }

        console.log();
        console.log(chalk.bold.underline('Summary'));
        console.log();
        console.log(`  Lists: ${chalk.bold(String(listNames.length))}`);
        console.log(`  Total tasks: ${chalk.bold(String(totalTasks))} (${chalk.yellow(`${totalInProgress} in-progress`)}, ${chalk.gray(`${totalPending} pending`)}, ${chalk.green(`${totalDone} done`)})`);
        console.log(`  Total in trash: ${chalk.red(String(totalTrash))}`);
      })),
  );

  return systemCommand;
}
