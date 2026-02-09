import { Command } from 'commander';
import chalk from 'chalk';
import type { UndoManager } from '@tasker/core';
import { getCommandDescription } from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createUndoCommand(undo: UndoManager): Command {
  return new Command('undo')
    .description('Undo the last action')
    .action(() => $try(() => {
      const desc = undo.undo();
      if (desc != null) {
        out.success(`Undone: ${desc}`);
      } else {
        out.info('Nothing to undo');
      }
    }));
}

export function createRedoCommand(undo: UndoManager): Command {
  return new Command('redo')
    .description('Redo the last undone action')
    .action(() => $try(() => {
      const desc = undo.redo();
      if (desc != null) {
        out.success(`Redone: ${desc}`);
      } else {
        out.info('Nothing to redo');
      }
    }));
}

export function createHistoryCommand(undo: UndoManager): Command {
  return new Command('history')
    .description('Show undo/redo history')
    .option('-c, --clear', 'Clear all undo/redo history')
    .action((opts: { clear?: boolean }) => $try(() => {
      if (opts.clear) {
        undo.clearHistory();
        out.success('Undo/redo history cleared');
        return;
      }

      const undoHistory = undo.undoHistory;
      const redoHistory = undo.redoHistory;

      if (undoHistory.length === 0 && redoHistory.length === 0) {
        out.info('No history');
        return;
      }

      if (undoHistory.length > 0) {
        console.log(`${chalk.bold('Undo stack')} ${chalk.dim(`(${undoHistory.length} actions, 50 max)`)}`);
        const shown = undoHistory.slice(0, 10);
        for (const cmd of shown) {
          const timeAgo = out.getTimeAgo(cmd.executedAt);
          console.log(`  ${chalk.dim(timeAgo)} ${getCommandDescription(cmd)}`);
        }
        if (undoHistory.length > 10) {
          console.log(chalk.dim(`  ... and ${undoHistory.length - 10} more`));
        }
      }

      if (redoHistory.length > 0) {
        console.log(`${chalk.bold('Redo stack')} ${chalk.dim(`(${redoHistory.length} actions)`)}`);
        const shown = redoHistory.slice(0, 10);
        for (const cmd of shown) {
          const timeAgo = out.getTimeAgo(cmd.executedAt);
          console.log(`  ${chalk.dim(timeAgo)} ${getCommandDescription(cmd)}`);
        }
        if (redoHistory.length > 10) {
          console.log(chalk.dim(`  ... and ${redoHistory.length - 10} more`));
        }
      }
    }));
}
