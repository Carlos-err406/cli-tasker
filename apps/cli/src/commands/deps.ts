import { Command } from 'commander';
import type { TaskerDb, UndoManager } from '@tasker/core';
import {
  setParent, unsetParent, addBlocker, removeBlocker,
  addRelated, removeRelated, getTaskById,
} from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createDepsCommand(db: TaskerDb, undo: UndoManager): Command {
  const depsCommand = new Command('deps')
    .description('Manage task dependencies (subtasks, blocking, and related)');

  depsCommand.addCommand(
    new Command('set-parent')
      .description('Make a task a subtask of another task')
      .argument('<taskId>', 'The task to make a subtask')
      .argument('<parentId>', 'The parent task ID')
      .action((taskId: string, parentId: string) => $try(() => {
        const old = getTaskById(db, taskId);
        const result = setParent(db, taskId, parentId);
        out.printResult(result);

        if (old && result.type === 'success') {
          undo.recordCommand({
            $type: 'set-parent',
            taskId,
            oldParentId: old.parentId,
            newParentId: parentId,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  depsCommand.addCommand(
    new Command('unset-parent')
      .description('Remove a task\'s parent (make it top-level)')
      .argument('<taskId>', 'The subtask to detach')
      .action((taskId: string) => $try(() => {
        const old = getTaskById(db, taskId);
        const result = unsetParent(db, taskId);
        out.printResult(result);

        if (old && result.type === 'success') {
          undo.recordCommand({
            $type: 'set-parent',
            taskId,
            oldParentId: old.parentId,
            newParentId: null,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  depsCommand.addCommand(
    new Command('add-blocker')
      .description('Mark a task as blocking another task')
      .argument('<blockerId>', 'The blocking task ID')
      .argument('<blockedId>', 'The blocked task ID')
      .action((blockerId: string, blockedId: string) => $try(() => {
        const result = addBlocker(db, blockerId, blockedId);
        out.printResult(result);

        if (result.type === 'success') {
          undo.recordCommand({
            $type: 'add-blocker',
            blockerId,
            blockedId,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  depsCommand.addCommand(
    new Command('remove-blocker')
      .description('Remove a blocking relationship between tasks')
      .argument('<blockerId>', 'The blocking task ID')
      .argument('<blockedId>', 'The blocked task ID')
      .action((blockerId: string, blockedId: string) => $try(() => {
        const result = removeBlocker(db, blockerId, blockedId);
        out.printResult(result);

        if (result.type === 'success') {
          undo.recordCommand({
            $type: 'remove-blocker',
            blockerId,
            blockedId,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  depsCommand.addCommand(
    new Command('add-related')
      .description('Mark two tasks as related to each other')
      .argument('<taskId1>', 'First task ID')
      .argument('<taskId2>', 'Second task ID')
      .action((taskId1: string, taskId2: string) => $try(() => {
        const result = addRelated(db, taskId1, taskId2);
        out.printResult(result);

        if (result.type === 'success') {
          undo.recordCommand({
            $type: 'add-related',
            taskId1,
            taskId2,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  depsCommand.addCommand(
    new Command('remove-related')
      .description('Remove a related relationship between two tasks')
      .argument('<taskId1>', 'First task ID')
      .argument('<taskId2>', 'Second task ID')
      .action((taskId1: string, taskId2: string) => $try(() => {
        const result = removeRelated(db, taskId1, taskId2);
        out.printResult(result);

        if (result.type === 'success') {
          undo.recordCommand({
            $type: 'remove-related',
            taskId1,
            taskId2,
            executedAt: new Date().toISOString(),
          });
          undo.saveHistory();
        }
      })),
  );

  return depsCommand;
}
