/**
 * Undo command types as a discriminated union.
 * Each command stores the data needed to execute and undo the operation.
 * The actual execution is done by the undo executor using the query layer.
 */

import type { Task } from '../types/task.js';
import type { TaskStatus } from '../types/task-status.js';
import type { Priority } from '../types/priority.js';

interface BaseCommand {
  executedAt: string; // ISO string
}

export interface AddTaskCmd extends BaseCommand {
  $type: 'add';
  task: Task;
}

export interface DeleteTaskCmd extends BaseCommand {
  $type: 'delete';
  deletedTask: Task;
}

export interface SetStatusCmd extends BaseCommand {
  $type: 'set-status';
  taskId: string;
  oldStatus: TaskStatus;
  newStatus: TaskStatus;
}

export interface RenameTaskCmd extends BaseCommand {
  $type: 'rename';
  taskId: string;
  oldDescription: string;
  newDescription: string;
}

export interface MoveTaskCmd extends BaseCommand {
  $type: 'move';
  taskId: string;
  sourceList: string;
  targetList: string;
}

export interface ClearTasksCmd extends BaseCommand {
  $type: 'clear';
  listName: string | null;
  clearedTasks: Task[];
}

export interface CompositeCmd extends BaseCommand {
  $type: 'batch';
  batchDescription: string;
  commands: UndoCommand[];
}

export interface MetadataChangedCmd extends BaseCommand {
  $type: 'metadata';
  taskId: string;
  oldDueDate: string | null;
  newDueDate: string | null;
  oldPriority: Priority | null;
  newPriority: Priority | null;
}

export interface RenameListCmd extends BaseCommand {
  $type: 'renameList';
  oldName: string;
  newName: string;
  wasDefaultList: boolean;
}

export interface ReorderTaskCmd extends BaseCommand {
  $type: 'reorderTask';
  taskId: string;
  listName: string;
  oldIndex: number;
  newIndex: number;
}

export interface ReorderListCmd extends BaseCommand {
  $type: 'reorderList';
  listName: string;
  oldIndex: number;
  newIndex: number;
}

export interface DeleteListCmd extends BaseCommand {
  $type: 'deleteList';
  listName: string;
  deletedTasks: Task[];
  trashedTasks: Task[];
  wasDefaultList: boolean;
  originalIndex: number;
}

export interface SetParentCmd extends BaseCommand {
  $type: 'set-parent';
  taskId: string;
  oldParentId: string | null;
  newParentId: string | null;
}

export interface AddBlockerCmd extends BaseCommand {
  $type: 'add-blocker';
  blockerId: string;
  blockedId: string;
}

export interface RemoveBlockerCmd extends BaseCommand {
  $type: 'remove-blocker';
  blockerId: string;
  blockedId: string;
}

export interface AddRelatedCmd extends BaseCommand {
  $type: 'add-related';
  taskId1: string;
  taskId2: string;
}

export interface RemoveRelatedCmd extends BaseCommand {
  $type: 'remove-related';
  taskId1: string;
  taskId2: string;
}

export type UndoCommand =
  | AddTaskCmd
  | DeleteTaskCmd
  | SetStatusCmd
  | RenameTaskCmd
  | MoveTaskCmd
  | ClearTasksCmd
  | CompositeCmd
  | MetadataChangedCmd
  | RenameListCmd
  | ReorderTaskCmd
  | ReorderListCmd
  | DeleteListCmd
  | SetParentCmd
  | AddBlockerCmd
  | RemoveBlockerCmd
  | AddRelatedCmd
  | RemoveRelatedCmd;

/** Get a human-readable description of an undo command */
export function getCommandDescription(cmd: UndoCommand): string {
  switch (cmd.$type) {
    case 'add': return `Add: ${cmd.task.description.slice(0, 30)}`;
    case 'delete': return `Delete: ${cmd.deletedTask.description.slice(0, 30)}`;
    case 'set-status': return `Status: ${cmd.taskId} → ${cmd.newStatus}`;
    case 'rename': return `Rename: ${cmd.taskId}`;
    case 'move': return `Move: ${cmd.taskId} to ${cmd.targetList}`;
    case 'clear': return `Clear: ${cmd.clearedTasks.length} tasks from ${cmd.listName ?? 'all lists'}`;
    case 'batch': return cmd.batchDescription;
    case 'metadata': return `Changed ${cmd.taskId}`;
    case 'renameList': return `Rename list: ${cmd.oldName} to ${cmd.newName}`;
    case 'reorderTask': return `Reorder task in ${cmd.listName}`;
    case 'reorderList': return `Reorder ${cmd.listName} list`;
    case 'deleteList': return `Delete list: ${cmd.listName}`;
    case 'set-parent': return cmd.newParentId ? `Set parent: ${cmd.taskId} → ${cmd.newParentId}` : `Remove parent: ${cmd.taskId}`;
    case 'add-blocker': return `Add blocker: ${cmd.blockerId} blocks ${cmd.blockedId}`;
    case 'remove-blocker': return `Remove blocker: ${cmd.blockerId} no longer blocks ${cmd.blockedId}`;
    case 'add-related': return `Add related: ${cmd.taskId1} ↔ ${cmd.taskId2}`;
    case 'remove-related': return `Remove related: ${cmd.taskId1} ↔ ${cmd.taskId2}`;
  }
}
