import { Command } from 'commander';
import chalk from 'chalk';
import type { TaskerDb, UndoManager } from '@tasker/core';
import {
  getAllListNames, createList, deleteList, renameList,
  isValidListName, getDefaultList, setDefaultList,
  getSortedTasks, getTrash, getListIndex,
} from '@tasker/core';
import * as out from '../output.js';
import { $try } from '../helpers.js';

export function createListsCommand(db: TaskerDb, undo: UndoManager): Command {
  const listsCommand = new Command('lists')
    .description('Manage task lists')
    .action(() => $try(() => {
      const lists = getAllListNames(db);
      const defaultList = getDefaultList(db);

      if (lists.length === 0) {
        out.info('No lists found. Add a task with: tasker add "task" -l <list-name>');
        return;
      }

      out.info('Available lists:');
      for (const list of lists) {
        if (list === defaultList) {
          console.log(`  ${chalk.bold(`${list} (default)`)}`);
        } else {
          console.log(`  ${list}`);
        }
      }
    }));

  listsCommand.addCommand(
    new Command('create')
      .description('Create a new empty list')
      .argument('<name>', 'The name of the list to create')
      .action((name: string) => $try(() => {
        if (!isValidListName(name)) {
          out.error(`Invalid list name '${name}'. Only letters, numbers, hyphens, and underscores are allowed.`);
          return;
        }
        createList(db, name);
        out.success(`Created list '${name}'`);
      })),
  );

  listsCommand.addCommand(
    new Command('delete')
      .description('Delete a list and all its tasks')
      .argument('<name>', 'The name of the list to delete')
      .action((name: string) => $try(() => {
        const tasks = getSortedTasks(db, { listName: name });
        const trashedTasks = getTrash(db, name);
        const wasDefault = getDefaultList(db) === name;
        const originalIndex = getListIndex(db, name);

        deleteList(db, name);

        undo.recordCommand({
          $type: 'deleteList',
          listName: name,
          deletedTasks: tasks,
          trashedTasks,
          wasDefaultList: wasDefault,
          originalIndex,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();

        out.success(`Deleted list '${name}'`);
      })),
  );

  listsCommand.addCommand(
    new Command('rename')
      .description('Rename a list')
      .argument('<oldName>', 'The current name of the list')
      .argument('<newName>', 'The new name for the list')
      .action((oldName: string, newName: string) => $try(() => {
        if (!isValidListName(newName)) {
          out.error(`Invalid list name '${newName}'. Only letters, numbers, hyphens, and underscores are allowed.`);
          return;
        }
        const wasDefault = getDefaultList(db) === oldName;
        renameList(db, oldName, newName);

        undo.recordCommand({
          $type: 'renameList',
          oldName,
          newName,
          wasDefaultList: wasDefault,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();

        out.success(`Renamed list '${oldName}' to '${newName}'`);
      })),
  );

  listsCommand.addCommand(
    new Command('set-default')
      .description('Set the default list for new tasks')
      .argument('<name>', 'The name of the list to set as default')
      .action((name: string) => $try(() => {
        if (!isValidListName(name)) {
          out.error(`Invalid list name '${name}'. Only letters, numbers, hyphens, and underscores are allowed.`);
          return;
        }
        setDefaultList(db, name);
        out.success(`Default list set to '${name}'`);
      })),
  );

  return listsCommand;
}
