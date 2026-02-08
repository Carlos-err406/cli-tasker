import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import {
  setParent, unsetParent, addBlocker, removeBlocker,
  addRelated, removeRelated,
} from '@tasker/core';
import * as out from '../output.js';

export function createDepsCommand(db: TaskerDb): Command {
  const depsCommand = new Command('deps')
    .description('Manage task dependencies (subtasks, blocking, and related)');

  depsCommand.addCommand(
    new Command('set-parent')
      .description('Make a task a subtask of another task')
      .argument('<taskId>', 'The task to make a subtask')
      .argument('<parentId>', 'The parent task ID')
      .action((taskId: string, parentId: string) => {
        try {
          out.printResult(setParent(db, taskId, parentId));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  depsCommand.addCommand(
    new Command('unset-parent')
      .description('Remove a task\'s parent (make it top-level)')
      .argument('<taskId>', 'The subtask to detach')
      .action((taskId: string) => {
        try {
          out.printResult(unsetParent(db, taskId));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  depsCommand.addCommand(
    new Command('add-blocker')
      .description('Mark a task as blocking another task')
      .argument('<blockerId>', 'The blocking task ID')
      .argument('<blockedId>', 'The blocked task ID')
      .action((blockerId: string, blockedId: string) => {
        try {
          out.printResult(addBlocker(db, blockerId, blockedId));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  depsCommand.addCommand(
    new Command('remove-blocker')
      .description('Remove a blocking relationship between tasks')
      .argument('<blockerId>', 'The blocking task ID')
      .argument('<blockedId>', 'The blocked task ID')
      .action((blockerId: string, blockedId: string) => {
        try {
          out.printResult(removeBlocker(db, blockerId, blockedId));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  depsCommand.addCommand(
    new Command('add-related')
      .description('Mark two tasks as related to each other')
      .argument('<taskId1>', 'First task ID')
      .argument('<taskId2>', 'Second task ID')
      .action((taskId1: string, taskId2: string) => {
        try {
          out.printResult(addRelated(db, taskId1, taskId2));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  depsCommand.addCommand(
    new Command('remove-related')
      .description('Remove a related relationship between two tasks')
      .argument('<taskId1>', 'First task ID')
      .argument('<taskId2>', 'Second task ID')
      .action((taskId1: string, taskId2: string) => {
        try {
          out.printResult(removeRelated(db, taskId1, taskId2));
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  return depsCommand;
}
