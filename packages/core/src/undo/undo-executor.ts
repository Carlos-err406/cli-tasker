/**
 * Executes undo/redo operations by dispatching command types to the query layer.
 */

import type { TaskerDb } from '../db.js';
import type { UndoCommand } from './undo-commands.js';
import { lists } from '../schema/lists.js';
import {
  addTask, deleteTask, setStatus, renameTask, moveTask, clearTasks,
  setTaskDueDate, setTaskPriority, restoreFromTrash, reorderTask,
  insertTask, deleteTaskPermanently,
  setParent, unsetParent, addBlocker, removeBlocker, addRelated, removeRelated,
} from '../queries/task-queries.js';
import { renameList, reorderList, deleteList } from '../queries/list-queries.js';

/** Execute a command (for redo) */
export function executeCommand(db: TaskerDb, cmd: UndoCommand): void {
  switch (cmd.$type) {
    case 'add':
      addTask(db, cmd.task.description, cmd.task.listName);
      break;
    case 'delete':
      deleteTask(db, cmd.deletedTask.id);
      break;
    case 'set-status':
      setStatus(db, cmd.taskId, cmd.newStatus);
      break;
    case 'rename':
      renameTask(db, cmd.taskId, cmd.newDescription);
      break;
    case 'move':
      moveTask(db, cmd.taskId, cmd.targetList);
      break;
    case 'clear':
      clearTasks(db, cmd.listName ?? undefined);
      break;
    case 'batch':
      for (const sub of cmd.commands) {
        executeCommand(db, sub);
      }
      break;
    case 'metadata':
      if (cmd.oldDueDate !== cmd.newDueDate) setTaskDueDate(db, cmd.taskId, cmd.newDueDate);
      if (cmd.oldPriority !== cmd.newPriority) setTaskPriority(db, cmd.taskId, cmd.newPriority);
      break;
    case 'renameList':
      renameList(db, cmd.oldName, cmd.newName);
      break;
    case 'reorderTask':
      reorderTask(db, cmd.taskId, cmd.newIndex);
      break;
    case 'reorderList':
      reorderList(db, cmd.listName, cmd.newIndex);
      break;
    case 'deleteList':
      deleteList(db, cmd.listName);
      break;
    case 'set-parent':
      if (cmd.newParentId) setParent(db, cmd.taskId, cmd.newParentId);
      else unsetParent(db, cmd.taskId);
      break;
    case 'add-blocker':
      addBlocker(db, cmd.blockerId, cmd.blockedId);
      break;
    case 'remove-blocker':
      removeBlocker(db, cmd.blockerId, cmd.blockedId);
      break;
    case 'add-related':
      addRelated(db, cmd.taskId1, cmd.taskId2);
      break;
    case 'remove-related':
      removeRelated(db, cmd.taskId1, cmd.taskId2);
      break;
  }
}

/** Undo a command (reverse the operation) */
export function undoCommand(db: TaskerDb, cmd: UndoCommand): void {
  switch (cmd.$type) {
    case 'add':
      deleteTaskPermanently(db, cmd.task.id);
      break;
    case 'delete': {
      // Try to restore from trash first; if cleared, re-insert
      const result = restoreFromTrash(db, cmd.deletedTask.id);
      if (result.type === 'not-found') {
        insertTask(db, cmd.deletedTask);
      }
      break;
    }
    case 'set-status':
      setStatus(db, cmd.taskId, cmd.oldStatus);
      break;
    case 'rename':
      renameTask(db, cmd.taskId, cmd.oldDescription);
      break;
    case 'move':
      moveTask(db, cmd.taskId, cmd.sourceList);
      break;
    case 'clear':
      // Re-insert all cleared tasks
      for (const task of cmd.clearedTasks) {
        insertTask(db, task);
      }
      break;
    case 'batch':
      // Undo in REVERSE order
      for (let i = cmd.commands.length - 1; i >= 0; i--) {
        undoCommand(db, cmd.commands[i]!);
      }
      break;
    case 'metadata':
      if (cmd.oldDueDate !== cmd.newDueDate) setTaskDueDate(db, cmd.taskId, cmd.oldDueDate);
      if (cmd.oldPriority !== cmd.newPriority) setTaskPriority(db, cmd.taskId, cmd.oldPriority);
      break;
    case 'renameList':
      renameList(db, cmd.newName, cmd.oldName);
      break;
    case 'reorderTask':
      reorderTask(db, cmd.taskId, cmd.oldIndex);
      break;
    case 'reorderList':
      reorderList(db, cmd.listName, cmd.oldIndex);
      break;
    case 'deleteList': {
      // Re-create the list and restore tasks
      db.insert(lists).values({ name: cmd.listName, sortOrder: cmd.originalIndex }).onConflictDoNothing().run();
      for (const task of cmd.deletedTasks) insertTask(db, task);
      for (const task of cmd.trashedTasks) insertTask(db, task, true);
      break;
    }
    case 'set-parent':
      if (cmd.oldParentId) setParent(db, cmd.taskId, cmd.oldParentId);
      else unsetParent(db, cmd.taskId);
      break;
    case 'add-blocker':
      removeBlocker(db, cmd.blockerId, cmd.blockedId);
      break;
    case 'remove-blocker':
      addBlocker(db, cmd.blockerId, cmd.blockedId);
      break;
    case 'add-related':
      removeRelated(db, cmd.taskId1, cmd.taskId2);
      break;
    case 'remove-related':
      addRelated(db, cmd.taskId1, cmd.taskId2);
      break;
  }
}
