#!/usr/bin/env node

import { join } from 'node:path';
import { homedir } from 'node:os';
import { Command } from 'commander';
import {
  createDb, getDefaultDbPath,
  UndoManager, BackupManager,
} from '@tasker/core';

import { createAddCommand } from './commands/add.js';
import { createListCommand } from './commands/list.js';
import { createGetCommand } from './commands/get.js';
import { createCheckCommand, createUncheckCommand } from './commands/check.js';
import { createDeleteCommand, createClearCommand } from './commands/delete.js';
import { createStatusCommand, createWipCommand } from './commands/status.js';
import { createRenameCommand } from './commands/rename.js';
import { createMoveCommand } from './commands/move.js';
import { createDueCommand } from './commands/due.js';
import { createPriorityCommand } from './commands/priority.js';
import { createUndoCommand, createRedoCommand, createHistoryCommand } from './commands/undo.js';
import { createListsCommand } from './commands/lists.js';
import { createTrashCommand } from './commands/trash.js';
import { createSystemCommand } from './commands/system.js';
import { createInitCommand } from './commands/init.js';
import { createBackupCommand } from './commands/backup.js';
import { createDepsCommand } from './commands/deps.js';

// Initialize database
const dbPath = getDefaultDbPath();
const db = createDb(dbPath);

// Initialize services
const backupDir = join(homedir(), '.tasker', 'backups');
const undo = new UndoManager(db);
const backup = new BackupManager(backupDir, db);

// Build the CLI program
const program = new Command()
  .name('tasker')
  .description('Lightweight task manager')
  .version('3.0.0')
  .option('-l, --list <name>', 'Filter to a specific list')
  .option('-a, --all', 'Show all lists (disable auto-detection)');

// Register commands
program.addCommand(createAddCommand(db, undo));
program.addCommand(createListCommand(db));
program.addCommand(createGetCommand(db));
program.addCommand(createCheckCommand(db, undo));
program.addCommand(createUncheckCommand(db, undo));
program.addCommand(createDeleteCommand(db, undo));
program.addCommand(createClearCommand(db, undo));
program.addCommand(createStatusCommand(db, undo));
program.addCommand(createWipCommand(db, undo));
program.addCommand(createRenameCommand(db, undo));
program.addCommand(createMoveCommand(db, undo));
program.addCommand(createDueCommand(db, undo));
program.addCommand(createPriorityCommand(db, undo));
program.addCommand(createUndoCommand(undo));
program.addCommand(createRedoCommand(undo));
program.addCommand(createHistoryCommand(undo));
program.addCommand(createListsCommand(db, undo));
program.addCommand(createTrashCommand(db));
program.addCommand(createSystemCommand(db));
program.addCommand(createInitCommand(db));
program.addCommand(createBackupCommand(backup, undo));
program.addCommand(createDepsCommand(db, undo));

// Default action (no command): show task list
program.action((_opts: unknown, cmd: Command) => {
  cmd.commands.find(c => c.name() === 'list')?.parse(process.argv.slice(0, 2));
});

program.parse();
