import { basename } from 'node:path';
import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { listExists, createList, isValidListName } from '@tasker/core';
import * as out from '../output.js';

export function createInitCommand(db: TaskerDb): Command {
  return new Command('init')
    .description('Initialize tasker for the current directory (creates a list named after the directory)')
    .action(() => {
      try {
        const dirName = basename(process.cwd());
        if (!dirName) {
          out.error('Cannot determine directory name');
          return;
        }

        if (!isValidListName(dirName)) {
          out.error(`Directory name '${dirName}' is not a valid list name (only letters, numbers, hyphens, underscores)`);
          return;
        }

        if (listExists(db, dirName)) {
          out.info(`List '${dirName}' already exists. Auto-detection is active in this directory.`);
          return;
        }

        createList(db, dirName);
        out.success(`Created list '${dirName}'. Commands in this directory will auto-filter to it.`);
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
