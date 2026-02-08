/**
 * Core task CRUD operations using Drizzle ORM.
 * Port of TodoTaskList from C# — the largest single file in the codebase.
 */

import { eq, and, sql, like } from 'drizzle-orm';
import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';
import { tasks } from '../schema/tasks.js';
import { lists } from '../schema/lists.js';
import { taskDependencies } from '../schema/task-dependencies.js';
import { taskRelations } from '../schema/task-relations.js';
import type { Task, TaskId, ListName } from '../types/task.js';
import type { TaskResult, BatchResult } from '../types/results.js';
import type { TaskStatus } from '../types/task-status.js';
import { TaskStatus as TS } from '../types/task-status.js';
import type { Priority } from '../types/priority.js';
import {
  generateId, createTask, withStatus, statusLabel,
  sortTasksForDisplay, serializeTags, deserializeTags,
} from './task-helpers.js';
import {
  parse as parseDescription,
  syncMetadataToDescription,
} from '../parsers/task-description-parser.js';

// ---------------------------------------------------------------------------
// Row mapper
// ---------------------------------------------------------------------------

/** Map a raw DB row to a Task object */
function rowToTask(row: any): Task {
  return {
    id: row.id,
    description: row.description,
    status: row.status,
    createdAt: row.created_at ?? row.createdAt,
    listName: row.list_name ?? row.listName,
    dueDate: row.due_date ?? row.dueDate ?? null,
    priority: row.priority ?? null,
    tags: deserializeTags(row.tags),
    isTrashed: row.is_trashed ?? row.isTrashed ?? 0,
    sortOrder: row.sort_order ?? row.sortOrder ?? 0,
    completedAt: row.completed_at ?? row.completedAt ?? null,
    parentId: row.parent_id ?? row.parentId ?? null,
  };
}

// ---------------------------------------------------------------------------
// Read queries
// ---------------------------------------------------------------------------

/** Get a single task by ID (non-trashed only) */
export function getTaskById(db: TaskerDb, taskId: TaskId): Task | null {
  const raw = getRawDb(db);
  const row = raw.prepare(
    'SELECT * FROM tasks WHERE id = ? AND is_trashed = 0',
  ).get(taskId) as any;
  return row ? rowToTask(row) : null;
}

/** Get a single task by ID, including trashed tasks */
export function getTaskByIdIncludingTrashed(db: TaskerDb, taskId: TaskId): Task | null {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT * FROM tasks WHERE id = ?').get(taskId) as any;
  return row ? rowToTask(row) : null;
}

/** Get all non-trashed tasks, optionally filtered by list */
export function getAllTasks(db: TaskerDb, listName?: ListName): Task[] {
  const raw = getRawDb(db);
  const stmt = listName
    ? raw.prepare('SELECT * FROM tasks WHERE is_trashed = 0 AND list_name = ? ORDER BY sort_order DESC')
    : raw.prepare('SELECT * FROM tasks WHERE is_trashed = 0 ORDER BY sort_order DESC');
  const rows = listName ? stmt.all(listName) : stmt.all();
  return (rows as any[]).map(rowToTask);
}

/** Get tasks sorted for display with optional filters */
export function getSortedTasks(
  db: TaskerDb,
  opts?: {
    listName?: ListName;
    status?: TaskStatus;
    priority?: Priority;
    overdue?: boolean;
  },
): Task[] {
  let taskList = getAllTasks(db, opts?.listName);

  if (opts?.status != null) {
    taskList = taskList.filter(t => t.status === opts.status);
  }
  if (opts?.priority != null) {
    taskList = taskList.filter(t => t.priority === opts.priority);
  }
  if (opts?.overdue) {
    const today = formatDateNow();
    taskList = taskList.filter(t => t.dueDate != null && t.dueDate < today);
  }

  return sortTasksForDisplay(taskList);
}

/** Get trashed tasks, optionally filtered by list */
export function getTrash(db: TaskerDb, listName?: ListName): Task[] {
  const raw = getRawDb(db);
  const stmt = listName
    ? raw.prepare('SELECT * FROM tasks WHERE is_trashed = 1 AND list_name = ? ORDER BY sort_order DESC')
    : raw.prepare('SELECT * FROM tasks WHERE is_trashed = 1 ORDER BY sort_order DESC');
  const rows = listName ? stmt.all(listName) : stmt.all();
  return (rows as any[]).map(rowToTask);
}

/** Search tasks by description (case-insensitive LIKE) */
export function searchTasks(db: TaskerDb, query: string): Task[] {
  const raw = getRawDb(db);
  const escaped = query.replace(/\\/g, '\\\\').replace(/%/g, '\\%').replace(/_/g, '\\_');
  const rows = raw.prepare(
    "SELECT * FROM tasks WHERE is_trashed = 0 AND description LIKE ? ESCAPE '\\' COLLATE NOCASE ORDER BY sort_order DESC",
  ).all(`%${escaped}%`) as any[];
  return sortTasksForDisplay(rows.map(rowToTask));
}

// ---------------------------------------------------------------------------
// Write operations
// ---------------------------------------------------------------------------

/** Ensure a list exists, creating it if necessary */
export function ensureListExists(db: TaskerDb, listName: ListName): void {
  const raw = getRawDb(db);
  raw.prepare(
    "INSERT OR IGNORE INTO lists (name, sort_order) VALUES (?, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM lists))",
  ).run(listName);
}

/** Get the next sort_order for inserting at the top of a list */
function nextSortOrder(db: TaskerDb, listName: ListName, trashed: boolean): number {
  const raw = getRawDb(db);
  const row = raw.prepare(
    'SELECT MAX(sort_order) as max_order FROM tasks WHERE list_name = ? AND is_trashed = ?',
  ).get(listName, trashed ? 1 : 0) as any;
  return ((row?.max_order as number | null) ?? -1) + 1;
}

/** Insert a task into the database */
export function insertTask(db: TaskerDb, task: Task, isTrashed = false): void {
  const raw = getRawDb(db);
  const order = nextSortOrder(db, task.listName, isTrashed);
  raw.prepare(`
    INSERT INTO tasks (id, description, status, created_at, list_name, due_date, priority, tags, is_trashed, sort_order, completed_at, parent_id)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).run(
    task.id, task.description, task.status, task.createdAt, task.listName,
    task.dueDate, task.priority, serializeTags(task.tags),
    isTrashed ? 1 : 0, order, task.completedAt, task.parentId,
  );
}

/** Update an existing task's core fields */
export function updateTask(db: TaskerDb, task: Task): void {
  const raw = getRawDb(db);
  raw.prepare(`
    UPDATE tasks SET description = ?, status = ?, list_name = ?,
      due_date = ?, priority = ?, tags = ?, completed_at = ?, parent_id = ?
    WHERE id = ?
  `).run(
    task.description, task.status, task.listName,
    task.dueDate, task.priority, serializeTags(task.tags),
    task.completedAt, task.parentId, task.id,
  );
}

/** Delete a task permanently */
export function deleteTaskPermanently(db: TaskerDb, taskId: TaskId): void {
  const raw = getRawDb(db);
  raw.prepare('DELETE FROM tasks WHERE id = ?').run(taskId);
}

/** Bump a task to the top of its list's sort order */
export function bumpSortOrder(db: TaskerDb, taskId: TaskId, listName: ListName): void {
  const raw = getRawDb(db);
  const row = raw.prepare(
    'SELECT MAX(sort_order) as max_order FROM tasks WHERE list_name = ? AND is_trashed = 0',
  ).get(listName) as any;
  const maxOrder = (row?.max_order as number | null) ?? 0;
  raw.prepare('UPDATE tasks SET sort_order = ? WHERE id = ?').run(maxOrder + 1, taskId);
}

// ---------------------------------------------------------------------------
// High-level operations
// ---------------------------------------------------------------------------

export interface AddResult {
  task: Task;
  warnings: string[];
}

/** Add a new task from a description, processing all metadata markers */
export function addTask(db: TaskerDb, description: string, listName: ListName): AddResult {
  const warnings: string[] = [];
  let task = createTask(description, listName);
  const parsed = parseDescription(task.description);

  // Validate parent reference
  if (task.parentId) {
    const parent = getTaskById(db, task.parentId);
    if (!parent) {
      warnings.push(`Parent task (${task.parentId}) not found, created as top-level task`);
      task = { ...task, parentId: null };
    } else if (task.listName !== parent.listName) {
      warnings.push(`Subtask moved to list '${parent.listName}' to match parent (${task.parentId})`);
      task = { ...task, listName: parent.listName };
    }
  }

  ensureListExists(db, task.listName);
  insertTask(db, task);

  // Process blocking references (!abc)
  if (parsed.blocksIds?.length) {
    for (const blockedId of parsed.blocksIds) {
      const blocked = getTaskById(db, blockedId);
      if (!blocked) { warnings.push(`Blocked task (${blockedId}) not found, skipping blocker relationship`); continue; }
      if (blockedId === task.id) { warnings.push('A task cannot block itself, skipping'); continue; }
      if (hasCircularBlocking(db, task.id, blockedId)) { warnings.push(`Circular dependency with (${blockedId}), skipping blocker relationship`); continue; }

      const raw = getRawDb(db);
      raw.prepare('INSERT INTO task_dependencies (task_id, blocks_task_id) VALUES (?, ?)').run(task.id, blockedId);
      addInverseMarker(db, blockedId, task.id, false);
    }
  }

  // Sync inverse marker on parent
  if (task.parentId) {
    addInverseMarker(db, task.parentId, task.id, true);
  }

  // Process inverse parent markers (-^abc)
  if (parsed.hasSubtaskIds?.length) {
    for (const subtaskId of parsed.hasSubtaskIds) {
      const subtask = getTaskById(db, subtaskId);
      if (!subtask) { warnings.push(`Subtask (${subtaskId}) not found, skipping inverse parent relationship`); continue; }
      if (subtaskId === task.id) { warnings.push('A task cannot be its own subtask, skipping'); continue; }
      if (subtask.listName !== task.listName) { warnings.push(`Subtask (${subtaskId}) is in a different list, skipping inverse parent relationship`); continue; }
      const descendants = getAllDescendantIds(db, subtaskId);
      if (descendants.includes(task.id)) { warnings.push(`Circular reference with (${subtaskId}), skipping inverse parent relationship`); continue; }

      const raw = getRawDb(db);
      raw.prepare('UPDATE tasks SET parent_id = ? WHERE id = ?').run(task.id, subtaskId);
      // Sync ^thisTask on subtask
      const subParsed = parseDescription(subtask.description);
      const subSynced = syncMetadataToDescription(
        subtask.description, subtask.priority, subtask.dueDate, subtask.tags,
        task.id, subParsed.blocksIds, subParsed.hasSubtaskIds, subParsed.blockedByIds, subParsed.relatedIds,
      );
      if (subSynced !== subtask.description) {
        raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(subSynced, subtaskId);
      }
    }
  }

  // Process inverse blocker markers (-!abc)
  if (parsed.blockedByIds?.length) {
    for (const blockerId of parsed.blockedByIds) {
      const blocker = getTaskById(db, blockerId);
      if (!blocker) { warnings.push(`Blocker task (${blockerId}) not found, skipping inverse blocker relationship`); continue; }
      if (blockerId === task.id) { warnings.push('A task cannot block itself, skipping'); continue; }
      if (hasCircularBlocking(db, blockerId, task.id)) { warnings.push(`Circular dependency with (${blockerId}), skipping inverse blocker relationship`); continue; }

      const raw = getRawDb(db);
      raw.prepare('INSERT OR IGNORE INTO task_dependencies (task_id, blocks_task_id) VALUES (?, ?)').run(blockerId, task.id);
      // Sync !thisTask on blocker
      const blockerParsed = parseDescription(blocker.description);
      const blockerBlocksIds = [...(blockerParsed.blocksIds ?? [])];
      if (!blockerBlocksIds.includes(task.id)) {
        blockerBlocksIds.push(task.id);
        const blockerSynced = syncMetadataToDescription(
          blocker.description, blocker.priority, blocker.dueDate, blocker.tags,
          blockerParsed.parentId, blockerBlocksIds, blockerParsed.hasSubtaskIds, blockerParsed.blockedByIds, blockerParsed.relatedIds,
        );
        if (blockerSynced !== blocker.description) {
          raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(blockerSynced, blockerId);
        }
      }
    }
  }

  // Process related references (~abc)
  if (parsed.relatedIds?.length) {
    for (const relatedId of parsed.relatedIds) {
      const related = getTaskById(db, relatedId);
      if (!related) { warnings.push(`Related task (${relatedId}) not found, skipping related relationship`); continue; }
      if (relatedId === task.id) { warnings.push('A task cannot be related to itself, skipping'); continue; }

      const [id1, id2] = task.id < relatedId ? [task.id, relatedId] : [relatedId, task.id];
      const raw = getRawDb(db);
      raw.prepare('INSERT OR IGNORE INTO task_relations (task_id_1, task_id_2) VALUES (?, ?)').run(id1, id2);
      syncRelatedMetadata(db, relatedId);
    }
  }

  return { task, warnings };
}

/** Set a task's status, with cascade to descendants when marking Done */
export function setStatus(db: TaskerDb, taskId: TaskId, status: TaskStatus): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };
  if (task.status === status) return { kind: 'no-change', message: `Task ${taskId} is already ${statusLabel(status)}` };

  // Cascade: when marking Done, also mark all non-Done descendants
  const cascadeIds: string[] = [];
  if (status === TS.Done) {
    for (const descId of getAllDescendantIds(db, taskId)) {
      const desc = getTaskById(db, descId);
      if (desc && desc.status !== TS.Done) cascadeIds.push(descId);
    }
  }

  const updated = withStatus(task, status);
  updateTask(db, updated);

  for (const descId of cascadeIds) {
    const desc = getTaskById(db, descId)!;
    updateTask(db, withStatus(desc, status));
  }

  const msg = cascadeIds.length > 0
    ? `Set ${taskId} and ${cascadeIds.length} subtask(s) to ${statusLabel(status)}`
    : `Set ${taskId} to ${statusLabel(status)}`;
  return { kind: 'success', message: msg };
}

/** Move task to trash (soft delete), cascading to descendants */
export function deleteTask(db: TaskerDb, taskId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };

  const descendantIds = getAllDescendantIds(db, taskId);
  const raw = getRawDb(db);
  raw.prepare('UPDATE tasks SET is_trashed = 1 WHERE id = ?').run(taskId);
  for (const descId of descendantIds) {
    raw.prepare('UPDATE tasks SET is_trashed = 1 WHERE id = ?').run(descId);
  }

  const msg = descendantIds.length > 0
    ? `Deleted task (${taskId}) and ${descendantIds.length} subtask(s)`
    : `Deleted task: ${taskId}`;
  return { kind: 'success', message: msg };
}

/** Batch delete (trash) multiple tasks */
export function deleteTasks(db: TaskerDb, taskIds: TaskId[]): BatchResult {
  const raw = getRawDb(db);
  const results: TaskResult[] = [];

  const run = raw.transaction(() => {
    for (const taskId of taskIds) {
      const task = getTaskById(db, taskId);
      if (!task) { results.push({ kind: 'not-found', taskId }); continue; }
      raw.prepare('UPDATE tasks SET is_trashed = 1 WHERE id = ?').run(taskId);
      results.push({ kind: 'success', message: `Deleted task: ${taskId}` });
    }
  });
  run();

  return { results };
}

/** Batch set status for multiple tasks */
export function setStatuses(db: TaskerDb, taskIds: TaskId[], status: TaskStatus): BatchResult {
  const raw = getRawDb(db);
  const results: TaskResult[] = [];

  const run = raw.transaction(() => {
    for (const taskId of taskIds) {
      const task = getTaskById(db, taskId);
      if (!task) { results.push({ kind: 'not-found', taskId }); continue; }
      if (task.status === status) { results.push({ kind: 'no-change', message: `Task ${taskId} is already ${statusLabel(status)}` }); continue; }

      const updated = withStatus(task, status);
      updateTask(db, updated);
      results.push({ kind: 'success', message: `Set ${taskId} to ${statusLabel(status)}` });
    }
  });
  run();

  return { results };
}

/** Rename a task, processing metadata changes */
export function renameTask(db: TaskerDb, taskId: TaskId, newDescription: string): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };

  const trimmed = newDescription.trim();
  const oldParsed = parseDescription(task.description);
  const newParsed = parseDescription(trimmed);

  // Preserve existing due date if the date marker text hasn't changed
  const newDueDate = (newParsed.dueDateRaw === oldParsed.dueDateRaw) ? task.dueDate : newParsed.dueDate;

  let renamedTask: Task = {
    ...task,
    description: trimmed,
    priority: newParsed.priority,
    dueDate: newDueDate,
    tags: newParsed.tags.length > 0 ? newParsed.tags : null,
    parentId: newParsed.lastLineIsMetadataOnly ? newParsed.parentId : task.parentId,
  };

  // Validate new parent
  if (newParsed.lastLineIsMetadataOnly && newParsed.parentId) {
    const parent = getTaskById(db, newParsed.parentId);
    if (!parent || parent.id === taskId) {
      renamedTask = { ...renamedTask, parentId: null };
    }
  }

  updateTask(db, renamedTask);
  bumpSortOrder(db, taskId, renamedTask.listName);

  if (newParsed.lastLineIsMetadataOnly) {
    // Sync blocking relationships
    const currentBlocksIds = getBlocksIds(db, taskId);
    syncBlockingRelationships(db, taskId, currentBlocksIds, newParsed.blocksIds);

    // Sync inverse markers for forward blocker changes
    const oldForward = new Set(oldParsed.blocksIds ?? []);
    const newForward = new Set(newParsed.blocksIds ?? []);
    for (const added of newForward) {
      if (!oldForward.has(added)) addInverseMarker(db, added, taskId, false);
    }
    for (const removed of oldForward) {
      if (!newForward.has(removed)) removeInverseMarker(db, removed, taskId, false);
    }

    // Sync parent change
    const oldParentId = oldParsed.parentId;
    const newParentId = renamedTask.parentId;
    if (oldParentId !== newParentId) {
      if (oldParentId) removeInverseMarker(db, oldParentId, taskId, true);
      if (newParentId) addInverseMarker(db, newParentId, taskId, true);
    }

    // Sync related relationships
    const currentRelatedIds = getRelatedIds(db, taskId);
    syncRelatedRelationships(db, taskId, currentRelatedIds, newParsed.relatedIds);
  }

  return { kind: 'success', message: `Renamed task: ${taskId}` };
}

/** Move a task to a different list, cascading to descendants */
export function moveTask(db: TaskerDb, taskId: TaskId, targetList: ListName): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };
  if (task.listName === targetList) return { kind: 'no-change', message: `Task is already in '${targetList}'` };
  if (task.parentId) return { kind: 'error', message: `Cannot move subtask (${taskId}) to a different list. Remove parent first, or move its parent.` };

  const descendantIds = getAllDescendantIds(db, taskId);

  ensureListExists(db, targetList);
  updateTask(db, { ...task, listName: targetList });
  bumpSortOrder(db, taskId, targetList);

  const raw = getRawDb(db);
  for (const descId of descendantIds) {
    raw.prepare('UPDATE tasks SET list_name = ? WHERE id = ?').run(targetList, descId);
  }

  const msg = descendantIds.length > 0
    ? `Moved (${taskId}) and ${descendantIds.length} subtask(s) from '${task.listName}' to '${targetList}'`
    : `Moved task ${taskId} from '${task.listName}' to '${targetList}'`;
  return { kind: 'success', message: msg };
}

/** Clear all non-trashed tasks in a list (move to trash) */
export function clearTasks(db: TaskerDb, listName?: ListName): number {
  const tasksToClear = getAllTasks(db, listName);
  if (tasksToClear.length === 0) return 0;

  const raw = getRawDb(db);
  const run = raw.transaction(() => {
    for (const task of tasksToClear) {
      raw.prepare('UPDATE tasks SET is_trashed = 1 WHERE id = ?').run(task.id);
    }
  });
  run();

  return tasksToClear.length;
}

/** Set a task's due date (or clear it) */
export function setTaskDueDate(db: TaskerDb, taskId: TaskId, dueDate: string | null): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };

  const updated: Task = { ...task, dueDate };
  const parsed = parseDescription(updated.description);
  const synced = syncMetadataToDescription(
    updated.description, updated.priority, updated.dueDate, updated.tags,
    parsed.parentId, parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  updateTask(db, { ...updated, description: synced });
  bumpSortOrder(db, taskId, updated.listName);

  const msg = dueDate ? `Set due date for ${taskId}: ${dueDate}` : `Cleared due date for ${taskId}`;
  return { kind: 'success', message: msg };
}

/** Set a task's priority (or clear it) */
export function setTaskPriority(db: TaskerDb, taskId: TaskId, priority: Priority | null): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };

  const updated: Task = { ...task, priority };
  const parsed = parseDescription(updated.description);
  const synced = syncMetadataToDescription(
    updated.description, updated.priority, updated.dueDate, updated.tags,
    parsed.parentId, parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  updateTask(db, { ...updated, description: synced });
  bumpSortOrder(db, taskId, updated.listName);

  const msg = priority != null ? `Set priority for ${taskId}: ${priority}` : `Cleared priority for ${taskId}`;
  return { kind: 'success', message: msg };
}

/** Restore a trashed task and its descendants */
export function restoreFromTrash(db: TaskerDb, taskId: TaskId): TaskResult {
  const raw = getRawDb(db);
  const row = raw.prepare('SELECT * FROM tasks WHERE id = ? AND is_trashed = 1').get(taskId) as any;
  if (!row) return { kind: 'not-found', taskId };

  const descendantIds: string[] = (raw.prepare(`
    WITH RECURSIVE desc AS (
      SELECT id FROM tasks WHERE parent_id = ? AND is_trashed = 1
      UNION ALL
      SELECT t.id FROM tasks t JOIN desc d ON t.parent_id = d.id WHERE t.is_trashed = 1
    )
    SELECT id FROM desc
  `).all(taskId) as any[]).map(r => r.id);

  raw.prepare('UPDATE tasks SET is_trashed = 0 WHERE id = ?').run(taskId);
  for (const descId of descendantIds) {
    raw.prepare('UPDATE tasks SET is_trashed = 0 WHERE id = ?').run(descId);
  }

  const msg = descendantIds.length > 0
    ? `Restored (${taskId}) and ${descendantIds.length} subtask(s)`
    : `Restored task: ${taskId}`;
  return { kind: 'success', message: msg };
}

/** Permanently delete all trashed tasks, optionally in a specific list */
export function clearTrash(db: TaskerDb, listName?: ListName): number {
  const trashItems = getTrash(db, listName);
  if (trashItems.length === 0) return 0;

  const raw = getRawDb(db);
  if (listName) {
    raw.prepare('DELETE FROM tasks WHERE is_trashed = 1 AND list_name = ?').run(listName);
  } else {
    raw.prepare('DELETE FROM tasks WHERE is_trashed = 1').run();
  }

  return trashItems.length;
}

/** Get stats for tasks in a list (or all lists) */
export function getStats(db: TaskerDb, listName?: ListName) {
  const taskList = getAllTasks(db, listName);
  const trash = getTrash(db, listName);
  return {
    total: taskList.length,
    pending: taskList.filter(t => t.status === TS.Pending).length,
    inProgress: taskList.filter(t => t.status === TS.InProgress).length,
    done: taskList.filter(t => t.status === TS.Done).length,
    trash: trash.length,
  };
}

/** Reorder a task within its list */
export function reorderTask(db: TaskerDb, taskId: TaskId, newIndex: number): void {
  const raw = getRawDb(db);
  const taskRow = raw.prepare('SELECT id, list_name FROM tasks WHERE id = ? AND is_trashed = 0').get(taskId) as any;
  if (!taskRow) return;

  const listRows = raw.prepare(
    'SELECT id FROM tasks WHERE list_name = ? AND is_trashed = 0 ORDER BY sort_order DESC',
  ).all(taskRow.list_name) as any[];
  const ids = listRows.map((r: any) => r.id as string);

  const currentIndex = ids.indexOf(taskId);
  if (currentIndex < 0) return;

  const clamped = Math.max(0, Math.min(newIndex, ids.length - 1));
  if (currentIndex === clamped) return;

  ids.splice(currentIndex, 1);
  ids.splice(clamped, 0, taskId);

  const run = raw.transaction(() => {
    for (let i = 0; i < ids.length; i++) {
      raw.prepare('UPDATE tasks SET sort_order = ? WHERE id = ?').run(ids.length - 1 - i, ids[i]);
    }
  });
  run();
}

// ---------------------------------------------------------------------------
// Dependency / relationship helpers
// ---------------------------------------------------------------------------

/** Get all descendant IDs of a task (recursive) */
export function getAllDescendantIds(db: TaskerDb, parentId: TaskId): string[] {
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    WITH RECURSIVE desc AS (
      SELECT id FROM tasks WHERE parent_id = ? AND is_trashed = 0
      UNION ALL
      SELECT t.id FROM tasks t JOIN desc d ON t.parent_id = d.id WHERE t.is_trashed = 0
    )
    SELECT id FROM desc
  `).all(parentId) as any[];
  return rows.map(r => r.id);
}

/** Get subtasks of a task */
export function getSubtasks(db: TaskerDb, parentId: TaskId): Task[] {
  const raw = getRawDb(db);
  const rows = raw.prepare('SELECT * FROM tasks WHERE parent_id = ? AND is_trashed = 0').all(parentId) as any[];
  return rows.map(rowToTask);
}

/** Check for circular blocking */
export function hasCircularBlocking(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): boolean {
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    WITH RECURSIVE chain AS (
      SELECT blocks_task_id AS target FROM task_dependencies WHERE task_id = ?
      UNION ALL
      SELECT td.blocks_task_id FROM task_dependencies td JOIN chain c ON td.task_id = c.target
    )
    SELECT target FROM chain
  `).all(blockedId) as any[];
  return rows.some(r => r.target === blockerId);
}

/** Get IDs of tasks that this task blocks */
export function getBlocksIds(db: TaskerDb, taskId: TaskId): string[] {
  const raw = getRawDb(db);
  return (raw.prepare('SELECT blocks_task_id FROM task_dependencies WHERE task_id = ?').all(taskId) as any[])
    .map(r => r.blocks_task_id);
}

/** Get IDs of tasks that block this task */
export function getBlockedByIds(db: TaskerDb, taskId: TaskId): string[] {
  const raw = getRawDb(db);
  return (raw.prepare('SELECT task_id FROM task_dependencies WHERE blocks_task_id = ?').all(taskId) as any[])
    .map(r => r.task_id);
}

/** Get tasks that block this task */
export function getBlockedBy(db: TaskerDb, taskId: TaskId): Task[] {
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    SELECT t.* FROM tasks t
    JOIN task_dependencies td ON td.task_id = t.id
    WHERE td.blocks_task_id = ? AND t.is_trashed = 0
  `).all(taskId) as any[];
  return rows.map(rowToTask);
}

/** Get tasks that this task blocks */
export function getBlocks(db: TaskerDb, taskId: TaskId): Task[] {
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    SELECT t.* FROM tasks t
    JOIN task_dependencies td ON td.blocks_task_id = t.id
    WHERE td.task_id = ? AND t.is_trashed = 0
  `).all(taskId) as any[];
  return rows.map(rowToTask);
}

/** Get related task IDs */
export function getRelatedIds(db: TaskerDb, taskId: TaskId): string[] {
  const raw = getRawDb(db);
  return (raw.prepare(`
    SELECT task_id_2 AS id FROM task_relations WHERE task_id_1 = ?
    UNION
    SELECT task_id_1 AS id FROM task_relations WHERE task_id_2 = ?
  `).all(taskId, taskId) as any[]).map(r => r.id);
}

/** Get related tasks */
export function getRelated(db: TaskerDb, taskId: TaskId): Task[] {
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    SELECT t.* FROM tasks t
    WHERE t.id IN (
      SELECT task_id_2 FROM task_relations WHERE task_id_1 = ?
      UNION
      SELECT task_id_1 FROM task_relations WHERE task_id_2 = ?
    ) AND t.is_trashed = 0
  `).all(taskId, taskId) as any[];
  return rows.map(rowToTask);
}

/** Set parent on a task */
export function setParent(db: TaskerDb, taskId: TaskId, parentId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };

  const parent = getTaskById(db, parentId);
  if (!parent) return { kind: 'error', message: `Parent task not found: ${parentId}` };
  if (task.id === parentId) return { kind: 'error', message: 'A task cannot be its own parent' };
  if (task.listName !== parent.listName) return { kind: 'error', message: `Cannot set parent: task (${taskId}) and parent (${parentId}) are in different lists.` };

  const descendants = getAllDescendantIds(db, taskId);
  if (descendants.includes(parentId)) return { kind: 'error', message: `Circular reference: (${parentId}) is already a descendant of (${taskId})` };

  const oldParentId = task.parentId;
  const raw = getRawDb(db);
  raw.prepare('UPDATE tasks SET parent_id = ? WHERE id = ?').run(parentId, taskId);

  // Sync metadata on child
  const parsed = parseDescription(task.description);
  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags, parentId,
    parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  if (synced !== task.description) {
    raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(synced, taskId);
  }

  // Sync inverse markers
  if (oldParentId && oldParentId !== parentId) removeInverseMarker(db, oldParentId, taskId, true);
  addInverseMarker(db, parentId, taskId, true);

  return { kind: 'success', message: `Set (${taskId}) as subtask of (${parentId})` };
}

/** Remove parent from a task */
export function unsetParent(db: TaskerDb, taskId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { kind: 'not-found', taskId };
  if (!task.parentId) return { kind: 'no-change', message: `Task (${taskId}) has no parent` };

  const oldParentId = task.parentId;
  const raw = getRawDb(db);
  raw.prepare('UPDATE tasks SET parent_id = NULL WHERE id = ?').run(taskId);

  const parsed = parseDescription(task.description);
  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags, null,
    parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  if (synced !== task.description) {
    raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(synced, taskId);
  }

  removeInverseMarker(db, oldParentId, taskId, true);
  return { kind: 'success', message: `Removed parent from (${taskId})` };
}

/** Add a blocker relationship */
export function addBlocker(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): TaskResult {
  if (blockerId === blockedId) return { kind: 'error', message: 'A task cannot block itself' };

  const blocker = getTaskById(db, blockerId);
  if (!blocker) return { kind: 'not-found', taskId: blockerId };

  const blocked = getTaskById(db, blockedId);
  if (!blocked) return { kind: 'error', message: `Blocked task not found: ${blockedId}` };

  if (hasCircularBlocking(db, blockerId, blockedId)) return { kind: 'error', message: `Circular dependency: (${blockedId}) already blocks (${blockerId})` };

  const raw = getRawDb(db);
  const exists = (raw.prepare('SELECT COUNT(*) as cnt FROM task_dependencies WHERE task_id = ? AND blocks_task_id = ?').get(blockerId, blockedId) as any).cnt;
  if (exists > 0) return { kind: 'no-change', message: `(${blockerId}) already blocks (${blockedId})` };

  raw.prepare('INSERT INTO task_dependencies (task_id, blocks_task_id) VALUES (?, ?)').run(blockerId, blockedId);
  addInverseMarker(db, blockedId, blockerId, false);

  return { kind: 'success', message: `(${blockerId}) now blocks (${blockedId})` };
}

/** Remove a blocker relationship */
export function removeBlocker(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): TaskResult {
  const raw = getRawDb(db);
  const exists = (raw.prepare('SELECT COUNT(*) as cnt FROM task_dependencies WHERE task_id = ? AND blocks_task_id = ?').get(blockerId, blockedId) as any).cnt;
  if (exists === 0) return { kind: 'no-change', message: `(${blockerId}) does not block (${blockedId})` };

  raw.prepare('DELETE FROM task_dependencies WHERE task_id = ? AND blocks_task_id = ?').run(blockerId, blockedId);
  removeInverseMarker(db, blockedId, blockerId, false);

  return { kind: 'success', message: `(${blockerId}) no longer blocks (${blockedId})` };
}

/** Add a related relationship */
export function addRelated(db: TaskerDb, taskId1: TaskId, taskId2: TaskId): TaskResult {
  if (taskId1 === taskId2) return { kind: 'error', message: 'A task cannot be related to itself' };

  const task1 = getTaskById(db, taskId1);
  if (!task1) return { kind: 'not-found', taskId: taskId1 };

  const task2 = getTaskById(db, taskId2);
  if (!task2) return { kind: 'error', message: `Related task not found: ${taskId2}` };

  const [id1, id2] = taskId1 < taskId2 ? [taskId1, taskId2] : [taskId2, taskId1];
  const raw = getRawDb(db);
  const exists = (raw.prepare('SELECT COUNT(*) as cnt FROM task_relations WHERE task_id_1 = ? AND task_id_2 = ?').get(id1, id2) as any).cnt;
  if (exists > 0) return { kind: 'no-change', message: `(${taskId1}) is already related to (${taskId2})` };

  raw.prepare('INSERT INTO task_relations (task_id_1, task_id_2) VALUES (?, ?)').run(id1, id2);
  syncRelatedMetadata(db, taskId1);
  syncRelatedMetadata(db, taskId2);

  return { kind: 'success', message: `(${taskId1}) is now related to (${taskId2})` };
}

/** Remove a related relationship */
export function removeRelated(db: TaskerDb, taskId1: TaskId, taskId2: TaskId): TaskResult {
  const [id1, id2] = taskId1 < taskId2 ? [taskId1, taskId2] : [taskId2, taskId1];
  const raw = getRawDb(db);
  const exists = (raw.prepare('SELECT COUNT(*) as cnt FROM task_relations WHERE task_id_1 = ? AND task_id_2 = ?').get(id1, id2) as any).cnt;
  if (exists === 0) return { kind: 'no-change', message: `(${taskId1}) is not related to (${taskId2})` };

  raw.prepare('DELETE FROM task_relations WHERE task_id_1 = ? AND task_id_2 = ?').run(id1, id2);
  syncRelatedMetadata(db, taskId1);
  syncRelatedMetadata(db, taskId2);

  return { kind: 'success', message: `(${taskId1}) is no longer related to (${taskId2})` };
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function addInverseMarker(db: TaskerDb, taskId: TaskId, refId: string, isSubtask: boolean): void {
  const task = getTaskById(db, taskId);
  if (!task) return;

  const parsed = parseDescription(task.description);
  const currentIds = [...(isSubtask ? parsed.hasSubtaskIds ?? [] : parsed.blockedByIds ?? [])];
  if (currentIds.includes(refId)) return;
  currentIds.push(refId);

  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags,
    parsed.parentId, parsed.blocksIds,
    isSubtask ? currentIds : parsed.hasSubtaskIds,
    isSubtask ? parsed.blockedByIds : currentIds,
    parsed.relatedIds,
  );
  if (synced !== task.description) {
    const raw = getRawDb(db);
    raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(synced, taskId);
  }
}

function removeInverseMarker(db: TaskerDb, taskId: TaskId, refId: string, isSubtask: boolean): void {
  const task = getTaskById(db, taskId);
  if (!task) return;

  const parsed = parseDescription(task.description);
  const currentIds = [...(isSubtask ? parsed.hasSubtaskIds ?? [] : parsed.blockedByIds ?? [])];
  const idx = currentIds.indexOf(refId);
  if (idx < 0) return;
  currentIds.splice(idx, 1);

  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags,
    parsed.parentId, parsed.blocksIds,
    isSubtask ? (currentIds.length > 0 ? currentIds : null) : parsed.hasSubtaskIds,
    isSubtask ? parsed.blockedByIds : (currentIds.length > 0 ? currentIds : null),
    parsed.relatedIds,
  );
  if (synced !== task.description) {
    const raw = getRawDb(db);
    raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(synced, taskId);
  }
}

function syncBlockingRelationships(db: TaskerDb, taskId: TaskId, oldIds: string[], newIds: string[] | null): void {
  const oldSet = new Set(oldIds);
  const newSet = new Set(newIds ?? []);
  const raw = getRawDb(db);

  for (const removed of oldSet) {
    if (!newSet.has(removed)) {
      raw.prepare('DELETE FROM task_dependencies WHERE task_id = ? AND blocks_task_id = ?').run(taskId, removed);
    }
  }

  for (const added of newSet) {
    if (!oldSet.has(added)) {
      const blocked = getTaskById(db, added);
      if (blocked && added !== taskId && !hasCircularBlocking(db, taskId, added)) {
        raw.prepare('INSERT OR IGNORE INTO task_dependencies (task_id, blocks_task_id) VALUES (?, ?)').run(taskId, added);
      }
    }
  }
}

function syncRelatedMetadata(db: TaskerDb, taskId: TaskId): void {
  const task = getTaskById(db, taskId);
  if (!task) return;

  const relIds = getRelatedIds(db, taskId);
  const parsed = parseDescription(task.description);
  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags,
    parsed.parentId, parsed.blocksIds,
    parsed.hasSubtaskIds, parsed.blockedByIds,
    relIds.length > 0 ? relIds : null,
  );
  if (synced !== task.description) {
    const raw = getRawDb(db);
    raw.prepare('UPDATE tasks SET description = ? WHERE id = ?').run(synced, taskId);
  }
}

function syncRelatedRelationships(db: TaskerDb, taskId: TaskId, oldIds: string[], newIds: string[] | null): void {
  const oldSet = new Set(oldIds);
  const newSet = new Set(newIds ?? []);
  const raw = getRawDb(db);

  for (const removed of oldSet) {
    if (!newSet.has(removed)) {
      const [id1, id2] = taskId < removed ? [taskId, removed] : [removed, taskId];
      raw.prepare('DELETE FROM task_relations WHERE task_id_1 = ? AND task_id_2 = ?').run(id1, id2);
      syncRelatedMetadata(db, removed);
    }
  }

  for (const added of newSet) {
    if (!oldSet.has(added)) {
      const related = getTaskById(db, added);
      if (related && added !== taskId) {
        const [id1, id2] = taskId < added ? [taskId, added] : [added, taskId];
        raw.prepare('INSERT OR IGNORE INTO task_relations (task_id_1, task_id_2) VALUES (?, ?)').run(id1, id2);
        syncRelatedMetadata(db, added);
      }
    }
  }
}

function formatDateNow(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}
