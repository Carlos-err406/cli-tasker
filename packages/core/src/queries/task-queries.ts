/**
 * Core task CRUD operations using Drizzle ORM.
 * Port of TodoTaskList from C# — the largest single file in the codebase.
 */

import { eq, and, desc, max, count, sql } from 'drizzle-orm';
import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';
import type { Task, TaskId, ListName } from '../types/task.js';
import type { TaskResult, BatchResult } from '../types/results.js';
import type { TaskStatus } from '../types/task-status.js';
import { TaskStatus as TS } from '../types/task-status.js';
import type { Priority } from '../types/priority.js';
import { tasks } from '../schema/tasks.js';
import { taskDependencies } from '../schema/task-dependencies.js';
import { taskRelations } from '../schema/task-relations.js';
import { lists } from '../schema/lists.js';
import {
  createTask, withStatus, statusLabel,
  sortTasksForDisplay, serializeTags, deserializeTags,
} from './task-helpers.js';
import {
  parse as parseDescription,
  getDisplayDescription,
  syncMetadataToDescription,
} from '../parsers/task-description-parser.js';
import { parseSearchFilters } from '../parsers/search-filter-parser.js';
import { formatDate, addDays } from '../parsers/date-parser.js';
import { getAllListNames } from './list-queries.js';

// ---------------------------------------------------------------------------
// Row mapper
// ---------------------------------------------------------------------------

/** Map a Drizzle row to a Task object (only tags needs deserialization) */
function toTask(row: typeof tasks.$inferSelect): Task {
  return {
    ...row,
    tags: deserializeTags(row.tags),
    status: row.status as TaskStatus,
    priority: row.priority as Priority | null,
    isTrashed: row.isTrashed as number,
    sortOrder: row.sortOrder as number,
  };
}

// ---------------------------------------------------------------------------
// Read queries
// ---------------------------------------------------------------------------

/** Get a single task by ID (non-trashed only) */
export function getTaskById(db: TaskerDb, taskId: TaskId): Task | null {
  const row = db.select().from(tasks).where(and(eq(tasks.id, taskId), eq(tasks.isTrashed, 0))).get();
  return row ? toTask(row) : null;
}

/** Get a single task by ID, including trashed tasks */
export function getTaskByIdIncludingTrashed(db: TaskerDb, taskId: TaskId): Task | null {
  const row = db.select().from(tasks).where(eq(tasks.id, taskId)).get();
  return row ? toTask(row) : null;
}

/** Get all non-trashed tasks, optionally filtered by list */
export function getAllTasks(db: TaskerDb, listName?: ListName): Task[] {
  const conditions = listName
    ? and(eq(tasks.isTrashed, 0), eq(tasks.listName, listName))
    : eq(tasks.isTrashed, 0);
  const rows = db.select().from(tasks).where(conditions).orderBy(desc(tasks.sortOrder)).all();
  return rows.map(toTask);
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
    const today = formatDate(new Date());
    taskList = taskList.filter(t => t.dueDate != null && t.dueDate < today);
  }

  return sortTasksForDisplay(taskList);
}

/** Get trashed tasks, optionally filtered by list */
export function getTrash(db: TaskerDb, listName?: ListName): Task[] {
  const conditions = listName
    ? and(eq(tasks.isTrashed, 1), eq(tasks.listName, listName))
    : eq(tasks.isTrashed, 1);
  const rows = db.select().from(tasks).where(conditions).orderBy(desc(tasks.sortOrder)).all();
  return rows.map(toTask);
}

/** Search tasks by description and/or smart filters (tag:x status:done due:today etc.) */
export function searchTasks(db: TaskerDb, query: string): Task[] {
  const filters = parseSearchFilters(query);
  const conditions: ReturnType<typeof eq>[] = [eq(tasks.isTrashed, 0)];

  // Description free-text search
  if (filters.descriptionQuery) {
    const escaped = filters.descriptionQuery.replace(/\\/g, '\\\\').replace(/%/g, '\\%').replace(/_/g, '\\_');
    conditions.push(sql`${tasks.description} LIKE ${'%' + escaped + '%'} ESCAPE '\\' COLLATE NOCASE` as any);
  }

  // Status filter
  if (filters.status != null) {
    conditions.push(eq(tasks.status, filters.status));
  }

  // Priority filter
  if (filters.priority != null) {
    conditions.push(eq(tasks.priority, filters.priority));
  }

  // List filter
  if (filters.listName != null) {
    conditions.push(eq(tasks.listName, filters.listName));
  }

  // Due date filters
  if (filters.dueFilter) {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const todayStr = formatDate(today);

    switch (filters.dueFilter) {
      case 'today':
        conditions.push(eq(tasks.dueDate, todayStr));
        break;
      case 'overdue':
        conditions.push(sql`${tasks.dueDate} IS NOT NULL AND ${tasks.dueDate} < ${todayStr}` as any);
        break;
      case 'week': {
        const weekEnd = formatDate(addDays(today, 7));
        conditions.push(sql`${tasks.dueDate} IS NOT NULL AND ${tasks.dueDate} >= ${todayStr} AND ${tasks.dueDate} <= ${weekEnd}` as any);
        break;
      }
      case 'month': {
        const monthEnd = formatDate(addDays(today, 30));
        conditions.push(sql`${tasks.dueDate} IS NOT NULL AND ${tasks.dueDate} >= ${todayStr} AND ${tasks.dueDate} <= ${monthEnd}` as any);
        break;
      }
    }
  }

  // has:due — tasks with a due date set
  if (filters.has.due) {
    conditions.push(sql`${tasks.dueDate} IS NOT NULL` as any);
  }

  // has:tags — tasks with non-null tags
  if (filters.has.tags) {
    conditions.push(sql`${tasks.tags} IS NOT NULL` as any);
  }

  // has:parent — tasks that are subtasks
  if (filters.has.parent) {
    conditions.push(sql`${tasks.parentId} IS NOT NULL` as any);
  }

  const rows = db.select().from(tasks).where(and(...conditions)).orderBy(desc(tasks.sortOrder)).all();
  let results = rows.map(toTask);

  // Post-filter: tags (stored as JSON array)
  if (filters.tags.length > 0) {
    results = results.filter(t => {
      if (!t.tags) return false;
      const lowerTags = t.tags.map(tag => tag.toLowerCase());
      return filters.tags.every(ft => lowerTags.includes(ft));
    });
  }

  // Post-filter: has:subtasks (needs child lookup)
  if (filters.has.subtasks) {
    const raw = getRawDb(db);
    const parentIds = new Set(
      (raw.prepare(`SELECT DISTINCT parent_id FROM tasks WHERE parent_id IS NOT NULL AND is_trashed = 0`).all() as any[])
        .map(r => r.parent_id as string),
    );
    results = results.filter(t => parentIds.has(t.id));
  }

  return sortTasksForDisplay(results);
}

// ---------------------------------------------------------------------------
// Write operations
// ---------------------------------------------------------------------------

/** Ensure a list exists, creating it if necessary */
export function ensureListExists(db: TaskerDb, listName: ListName): void {
  db.insert(lists).values({
    name: listName,
    sortOrder: sql`(SELECT COALESCE(MAX(${lists.sortOrder}), -1) + 1 FROM ${lists})`,
  }).onConflictDoNothing().run();
}

/** Get the next sort_order for inserting at the top of a list */
function nextSortOrder(db: TaskerDb, listName: ListName, trashed: boolean): number {
  const row = db.select({ maxOrder: max(tasks.sortOrder) }).from(tasks).where(
    and(eq(tasks.listName, listName), eq(tasks.isTrashed, trashed ? 1 : 0)),
  ).get();
  return ((row?.maxOrder ?? -1) as number) + 1;
}

/** Insert a task into the database */
export function insertTask(db: TaskerDb, task: Task, isTrashed = false): void {
  const order = nextSortOrder(db, task.listName, isTrashed);
  db.insert(tasks).values({
    id: task.id,
    description: task.description,
    status: task.status,
    createdAt: task.createdAt,
    listName: task.listName,
    dueDate: task.dueDate,
    priority: task.priority,
    tags: serializeTags(task.tags),
    isTrashed: isTrashed ? 1 : 0,
    sortOrder: order,
    completedAt: task.completedAt,
    parentId: task.parentId,
  }).run();
}

/** Update an existing task's core fields */
export function updateTask(db: TaskerDb, task: Task): void {
  db.update(tasks).set({
    description: task.description,
    status: task.status,
    listName: task.listName,
    dueDate: task.dueDate,
    priority: task.priority,
    tags: serializeTags(task.tags),
    completedAt: task.completedAt,
    parentId: task.parentId,
  }).where(eq(tasks.id, task.id)).run();
}

/** Delete a task permanently */
export function deleteTaskPermanently(db: TaskerDb, taskId: TaskId): void {
  db.delete(tasks).where(eq(tasks.id, taskId)).run();
}

/** Bump a task to the top of its list's sort order */
export function bumpSortOrder(db: TaskerDb, taskId: TaskId, listName: ListName): void {
  const row = db.select({ maxOrder: max(tasks.sortOrder) }).from(tasks).where(
    and(eq(tasks.listName, listName), eq(tasks.isTrashed, 0)),
  ).get();
  const maxOrder = (row?.maxOrder ?? 0) as number;
  db.update(tasks).set({ sortOrder: maxOrder + 1 }).where(eq(tasks.id, taskId)).run();
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

      db.insert(taskDependencies).values({ taskId: task.id, blocksTaskId: blockedId }).run();
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

      db.update(tasks).set({ parentId: task.id }).where(eq(tasks.id, subtaskId)).run();
      // Sync ^thisTask on subtask
      const subParsed = parseDescription(subtask.description);
      const subSynced = syncMetadataToDescription(
        subtask.description, subtask.priority, subtask.dueDate, subtask.tags,
        task.id, subParsed.blocksIds, subParsed.hasSubtaskIds, subParsed.blockedByIds, subParsed.relatedIds,
      );
      if (subSynced !== subtask.description) {
        db.update(tasks).set({ description: subSynced }).where(eq(tasks.id, subtaskId)).run();
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

      db.insert(taskDependencies).values({ taskId: blockerId, blocksTaskId: task.id }).onConflictDoNothing().run();
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
          db.update(tasks).set({ description: blockerSynced }).where(eq(tasks.id, blockerId)).run();
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
      db.insert(taskRelations).values({ taskId1: id1, taskId2: id2 }).onConflictDoNothing().run();
      syncRelatedMetadata(db, relatedId);
    }
  }

  return { task, warnings };
}

/** Set a task's status, with cascade to descendants when marking Done */
export function setStatus(db: TaskerDb, taskId: TaskId, status: TaskStatus): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };
  if (task.status === status) return { type: 'no-change', message: `Task ${taskId} is already ${statusLabel(status)}` };

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
  return { type: 'success', message: msg };
}

/** Move task to trash (soft delete), cascading to descendants */
export function deleteTask(db: TaskerDb, taskId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };

  const descendantIds = getAllDescendantIds(db, taskId);
  db.update(tasks).set({ isTrashed: 1 }).where(eq(tasks.id, taskId)).run();
  for (const descId of descendantIds) {
    db.update(tasks).set({ isTrashed: 1 }).where(eq(tasks.id, descId)).run();
  }

  const msg = descendantIds.length > 0
    ? `Deleted task (${taskId}) and ${descendantIds.length} subtask(s)`
    : `Deleted task: ${taskId}`;
  return { type: 'success', message: msg };
}

/** Batch delete (trash) multiple tasks */
export function deleteTasks(db: TaskerDb, taskIds: TaskId[]): BatchResult {
  const raw = getRawDb(db);
  const results: TaskResult[] = [];

  const run = raw.transaction(() => {
    for (const taskId of taskIds) {
      const task = getTaskById(db, taskId);
      if (!task) { results.push({ type: 'not-found', taskId }); continue; }
      db.update(tasks).set({ isTrashed: 1 }).where(eq(tasks.id, taskId)).run();
      results.push({ type: 'success', message: `Deleted task: ${taskId}` });
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
      if (!task) { results.push({ type: 'not-found', taskId }); continue; }
      if (task.status === status) { results.push({ type: 'no-change', message: `Task ${taskId} is already ${statusLabel(status)}` }); continue; }

      const updated = withStatus(task, status);
      updateTask(db, updated);
      results.push({ type: 'success', message: `Set ${taskId} to ${statusLabel(status)}` });
    }
  });
  run();

  return { results };
}

/** Rename a task, processing metadata changes */
export function renameTask(db: TaskerDb, taskId: TaskId, newDescription: string): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };

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

  // Sync inverse parent markers (-^childId removed = unset parent on child)
  const oldSubtasks = new Set(oldParsed.hasSubtaskIds ?? []);
  const newSubtasks = new Set(newParsed.hasSubtaskIds ?? []);
  for (const removed of oldSubtasks) {
    if (!newSubtasks.has(removed)) {
      const child = getTaskById(db, removed);
      if (child && child.parentId === taskId) {
        unsetParent(db, removed);
      }
    }
  }

  // Sync inverse blocker markers (-!blockerId removed = remove blocking relationship + forward marker)
  const oldBlockedBy = new Set(oldParsed.blockedByIds ?? []);
  const newBlockedBy = new Set(newParsed.blockedByIds ?? []);
  for (const removed of oldBlockedBy) {
    if (!newBlockedBy.has(removed)) {
      // Remove the DB relationship
      db.delete(taskDependencies).where(
        and(eq(taskDependencies.taskId, removed), eq(taskDependencies.blocksTaskId, taskId)),
      ).run();
      // Remove the forward !taskId marker from the blocker's description
      const blocker = getTaskById(db, removed);
      if (blocker) {
        const blockerParsed = parseDescription(blocker.description);
        const updatedBlocksIds = (blockerParsed.blocksIds ?? []).filter(id => id !== taskId);
        const synced = syncMetadataToDescription(
          blocker.description, blocker.priority, blocker.dueDate, blocker.tags,
          blockerParsed.parentId, updatedBlocksIds.length > 0 ? updatedBlocksIds : null,
          blockerParsed.hasSubtaskIds, blockerParsed.blockedByIds, blockerParsed.relatedIds,
        );
        if (synced !== blocker.description) {
          db.update(tasks).set({ description: synced }).where(eq(tasks.id, removed)).run();
        }
      }
    }
  }

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

  return { type: 'success', message: `Renamed task: ${taskId}` };
}

/** Move a task to a different list, cascading to descendants */
export function moveTask(db: TaskerDb, taskId: TaskId, targetList: ListName): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };
  if (task.listName === targetList) return { type: 'no-change', message: `Task is already in '${targetList}'` };
  if (task.parentId) return { type: 'error', message: `Cannot move subtask (${taskId}) to a different list. Remove parent first, or move its parent.` };

  const descendantIds = getAllDescendantIds(db, taskId);

  ensureListExists(db, targetList);
  updateTask(db, { ...task, listName: targetList });
  bumpSortOrder(db, taskId, targetList);

  for (const descId of descendantIds) {
    db.update(tasks).set({ listName: targetList }).where(eq(tasks.id, descId)).run();
  }

  const msg = descendantIds.length > 0
    ? `Moved (${taskId}) and ${descendantIds.length} subtask(s) from '${task.listName}' to '${targetList}'`
    : `Moved task ${taskId} from '${task.listName}' to '${targetList}'`;
  return { type: 'success', message: msg };
}

/** Clear all non-trashed tasks in a list (move to trash) */
export function clearTasks(db: TaskerDb, listName?: ListName): number {
  const tasksToClear = getAllTasks(db, listName);
  if (tasksToClear.length === 0) return 0;

  const raw = getRawDb(db);
  const run = raw.transaction(() => {
    for (const task of tasksToClear) {
      db.update(tasks).set({ isTrashed: 1 }).where(eq(tasks.id, task.id)).run();
    }
  });
  run();

  return tasksToClear.length;
}

/** Set a task's due date (or clear it) */
export function setTaskDueDate(db: TaskerDb, taskId: TaskId, dueDate: string | null): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };

  const updated: Task = { ...task, dueDate };
  const parsed = parseDescription(updated.description);
  const synced = syncMetadataToDescription(
    updated.description, updated.priority, updated.dueDate, updated.tags,
    parsed.parentId, parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  updateTask(db, { ...updated, description: synced });
  bumpSortOrder(db, taskId, updated.listName);

  const msg = dueDate ? `Set due date for ${taskId}: ${dueDate}` : `Cleared due date for ${taskId}`;
  return { type: 'success', message: msg };
}

/** Set a task's priority (or clear it) */
export function setTaskPriority(db: TaskerDb, taskId: TaskId, priority: Priority | null): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };

  const updated: Task = { ...task, priority };
  const parsed = parseDescription(updated.description);
  const synced = syncMetadataToDescription(
    updated.description, updated.priority, updated.dueDate, updated.tags,
    parsed.parentId, parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  updateTask(db, { ...updated, description: synced });
  bumpSortOrder(db, taskId, updated.listName);

  const msg = priority != null ? `Set priority for ${taskId}: ${priority}` : `Cleared priority for ${taskId}`;
  return { type: 'success', message: msg };
}

/** Restore a trashed task and its descendants */
export function restoreFromTrash(db: TaskerDb, taskId: TaskId): TaskResult {
  const row = db.select().from(tasks).where(and(eq(tasks.id, taskId), eq(tasks.isTrashed, 1))).get();
  if (!row) return { type: 'not-found', taskId };

  // Recursive CTE for trashed descendants — Drizzle doesn't support WITH RECURSIVE
  const raw = getRawDb(db);
  const descendantIds: string[] = (raw.prepare(`
    WITH RECURSIVE desc AS (
      SELECT id FROM tasks WHERE parent_id = ? AND is_trashed = 1
      UNION ALL
      SELECT t.id FROM tasks t JOIN desc d ON t.parent_id = d.id WHERE t.is_trashed = 1
    )
    SELECT id FROM desc
  `).all(taskId) as any[]).map(r => r.id);

  db.update(tasks).set({ isTrashed: 0 }).where(eq(tasks.id, taskId)).run();
  for (const descId of descendantIds) {
    db.update(tasks).set({ isTrashed: 0 }).where(eq(tasks.id, descId)).run();
  }

  const msg = descendantIds.length > 0
    ? `Restored (${taskId}) and ${descendantIds.length} subtask(s)`
    : `Restored task: ${taskId}`;
  return { type: 'success', message: msg };
}

/** Permanently delete all trashed tasks, optionally in a specific list */
export function clearTrash(db: TaskerDb, listName?: ListName): number {
  const trashItems = getTrash(db, listName);
  if (trashItems.length === 0) return 0;

  if (listName) {
    db.delete(tasks).where(and(eq(tasks.isTrashed, 1), eq(tasks.listName, listName))).run();
  } else {
    db.delete(tasks).where(eq(tasks.isTrashed, 1)).run();
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
  const taskRow = db.select({ id: tasks.id, listName: tasks.listName }).from(tasks).where(
    and(eq(tasks.id, taskId), eq(tasks.isTrashed, 0)),
  ).get();
  if (!taskRow) return;

  const listRows = db.select({ id: tasks.id }).from(tasks).where(
    and(eq(tasks.listName, taskRow.listName), eq(tasks.isTrashed, 0)),
  ).orderBy(desc(tasks.sortOrder)).all();
  const ids = listRows.map(r => r.id);

  const currentIndex = ids.indexOf(taskId);
  if (currentIndex < 0) return;

  const clamped = Math.max(0, Math.min(newIndex, ids.length - 1));
  if (currentIndex === clamped) return;

  ids.splice(currentIndex, 1);
  ids.splice(clamped, 0, taskId);

  const raw = getRawDb(db);
  const run = raw.transaction(() => {
    for (let i = 0; i < ids.length; i++) {
      db.update(tasks).set({ sortOrder: ids.length - 1 - i }).where(eq(tasks.id, ids[i]!)).run();
    }
  });
  run();
}

// ---------------------------------------------------------------------------
// Dependency / relationship helpers
// ---------------------------------------------------------------------------

/** Get all descendant IDs of a task (recursive) */
export function getAllDescendantIds(db: TaskerDb, parentId: TaskId): string[] {
  // Recursive CTE — Drizzle doesn't support WITH RECURSIVE
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
  const rows = db.select().from(tasks).where(and(eq(tasks.parentId, parentId), eq(tasks.isTrashed, 0))).all();
  return rows.map(toTask);
}

/** Check for circular blocking */
export function hasCircularBlocking(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): boolean {
  // Recursive CTE — Drizzle doesn't support WITH RECURSIVE
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
  return db.select({ id: taskDependencies.blocksTaskId }).from(taskDependencies)
    .where(eq(taskDependencies.taskId, taskId)).all()
    .map(r => r.id);
}

/** Get IDs of tasks that block this task */
export function getBlockedByIds(db: TaskerDb, taskId: TaskId): string[] {
  return db.select({ id: taskDependencies.taskId }).from(taskDependencies)
    .where(eq(taskDependencies.blocksTaskId, taskId)).all()
    .map(r => r.id);
}

/** Get tasks that block this task */
export function getBlockedBy(db: TaskerDb, taskId: TaskId): Task[] {
  const rows = db.select({
    id: tasks.id, description: tasks.description, status: tasks.status, createdAt: tasks.createdAt,
    listName: tasks.listName, dueDate: tasks.dueDate, priority: tasks.priority, tags: tasks.tags,
    isTrashed: tasks.isTrashed, sortOrder: tasks.sortOrder, completedAt: tasks.completedAt, parentId: tasks.parentId,
  }).from(tasks)
    .innerJoin(taskDependencies, eq(taskDependencies.taskId, tasks.id))
    .where(and(eq(taskDependencies.blocksTaskId, taskId), eq(tasks.isTrashed, 0)))
    .all();
  return rows.map(toTask);
}

/** Get tasks that this task blocks */
export function getBlocks(db: TaskerDb, taskId: TaskId): Task[] {
  const rows = db.select({
    id: tasks.id, description: tasks.description, status: tasks.status, createdAt: tasks.createdAt,
    listName: tasks.listName, dueDate: tasks.dueDate, priority: tasks.priority, tags: tasks.tags,
    isTrashed: tasks.isTrashed, sortOrder: tasks.sortOrder, completedAt: tasks.completedAt, parentId: tasks.parentId,
  }).from(tasks)
    .innerJoin(taskDependencies, eq(taskDependencies.blocksTaskId, tasks.id))
    .where(and(eq(taskDependencies.taskId, taskId), eq(tasks.isTrashed, 0)))
    .all();
  return rows.map(toTask);
}

/** Get related task IDs */
export function getRelatedIds(db: TaskerDb, taskId: TaskId): string[] {
  // Union of both directions
  const raw = getRawDb(db);
  const rows = raw.prepare(`
    SELECT task_id_2 AS id FROM task_relations WHERE task_id_1 = ?
    UNION
    SELECT task_id_1 AS id FROM task_relations WHERE task_id_2 = ?
  `).all(taskId, taskId) as any[];
  return rows.map(r => r.id);
}

/** Get related tasks */
export function getRelated(db: TaskerDb, taskId: TaskId): Task[] {
  const relatedIds = getRelatedIds(db, taskId);
  if (relatedIds.length === 0) return [];
  // Fetch each related task
  return relatedIds.map(id => getTaskById(db, id)).filter((t): t is Task => t !== null);
}

/** Set parent on a task */
export function setParent(db: TaskerDb, taskId: TaskId, parentId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };

  const parent = getTaskById(db, parentId);
  if (!parent) return { type: 'error', message: `Parent task not found: ${parentId}` };
  if (task.id === parentId) return { type: 'error', message: 'A task cannot be its own parent' };
  if (task.listName !== parent.listName) return { type: 'error', message: `Cannot set parent: task (${taskId}) and parent (${parentId}) are in different lists.` };

  const descendants = getAllDescendantIds(db, taskId);
  if (descendants.includes(parentId)) return { type: 'error', message: `Circular reference: (${parentId}) is already a descendant of (${taskId})` };

  const oldParentId = task.parentId;
  db.update(tasks).set({ parentId }).where(eq(tasks.id, taskId)).run();

  // Sync metadata on child
  const parsed = parseDescription(task.description);
  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags, parentId,
    parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  if (synced !== task.description) {
    db.update(tasks).set({ description: synced }).where(eq(tasks.id, taskId)).run();
  }

  // Sync inverse markers
  if (oldParentId && oldParentId !== parentId) removeInverseMarker(db, oldParentId, taskId, true);
  addInverseMarker(db, parentId, taskId, true);

  return { type: 'success', message: `Set (${taskId}) as subtask of (${parentId})` };
}

/** Remove parent from a task */
export function unsetParent(db: TaskerDb, taskId: TaskId): TaskResult {
  const task = getTaskById(db, taskId);
  if (!task) return { type: 'not-found', taskId };
  if (!task.parentId) return { type: 'no-change', message: `Task (${taskId}) has no parent` };

  const oldParentId = task.parentId;
  db.update(tasks).set({ parentId: null }).where(eq(tasks.id, taskId)).run();

  const parsed = parseDescription(task.description);
  const synced = syncMetadataToDescription(
    task.description, task.priority, task.dueDate, task.tags, null,
    parsed.blocksIds, parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
  );
  if (synced !== task.description) {
    db.update(tasks).set({ description: synced }).where(eq(tasks.id, taskId)).run();
  }

  removeInverseMarker(db, oldParentId, taskId, true);
  return { type: 'success', message: `Removed parent from (${taskId})` };
}

/** Add a blocker relationship */
export function addBlocker(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): TaskResult {
  if (blockerId === blockedId) return { type: 'error', message: 'A task cannot block itself' };

  const blocker = getTaskById(db, blockerId);
  if (!blocker) return { type: 'not-found', taskId: blockerId };

  const blocked = getTaskById(db, blockedId);
  if (!blocked) return { type: 'error', message: `Blocked task not found: ${blockedId}` };

  if (hasCircularBlocking(db, blockerId, blockedId)) return { type: 'error', message: `Circular dependency: (${blockedId}) already blocks (${blockerId})` };

  const existing = db.select({ cnt: count() }).from(taskDependencies).where(
    and(eq(taskDependencies.taskId, blockerId), eq(taskDependencies.blocksTaskId, blockedId)),
  ).get();
  if ((existing?.cnt ?? 0) > 0) return { type: 'no-change', message: `(${blockerId}) already blocks (${blockedId})` };

  db.insert(taskDependencies).values({ taskId: blockerId, blocksTaskId: blockedId }).run();
  addInverseMarker(db, blockedId, blockerId, false);

  return { type: 'success', message: `(${blockerId}) now blocks (${blockedId})` };
}

/** Remove a blocker relationship */
export function removeBlocker(db: TaskerDb, blockerId: TaskId, blockedId: TaskId): TaskResult {
  const existing = db.select({ cnt: count() }).from(taskDependencies).where(
    and(eq(taskDependencies.taskId, blockerId), eq(taskDependencies.blocksTaskId, blockedId)),
  ).get();
  if ((existing?.cnt ?? 0) === 0) return { type: 'no-change', message: `(${blockerId}) does not block (${blockedId})` };

  db.delete(taskDependencies).where(
    and(eq(taskDependencies.taskId, blockerId), eq(taskDependencies.blocksTaskId, blockedId)),
  ).run();
  removeInverseMarker(db, blockedId, blockerId, false);

  return { type: 'success', message: `(${blockerId}) no longer blocks (${blockedId})` };
}

/** Add a related relationship */
export function addRelated(db: TaskerDb, taskId1: TaskId, taskId2: TaskId): TaskResult {
  if (taskId1 === taskId2) return { type: 'error', message: 'A task cannot be related to itself' };

  const task1 = getTaskById(db, taskId1);
  if (!task1) return { type: 'not-found', taskId: taskId1 };

  const task2 = getTaskById(db, taskId2);
  if (!task2) return { type: 'error', message: `Related task not found: ${taskId2}` };

  const [id1, id2] = taskId1 < taskId2 ? [taskId1, taskId2] : [taskId2, taskId1];
  const existing = db.select({ cnt: count() }).from(taskRelations).where(
    and(eq(taskRelations.taskId1, id1), eq(taskRelations.taskId2, id2)),
  ).get();
  if ((existing?.cnt ?? 0) > 0) return { type: 'no-change', message: `(${taskId1}) is already related to (${taskId2})` };

  db.insert(taskRelations).values({ taskId1: id1, taskId2: id2 }).run();
  syncRelatedMetadata(db, taskId1);
  syncRelatedMetadata(db, taskId2);

  return { type: 'success', message: `(${taskId1}) is now related to (${taskId2})` };
}

/** Remove a related relationship */
export function removeRelated(db: TaskerDb, taskId1: TaskId, taskId2: TaskId): TaskResult {
  const [id1, id2] = taskId1 < taskId2 ? [taskId1, taskId2] : [taskId2, taskId1];
  const existing = db.select({ cnt: count() }).from(taskRelations).where(
    and(eq(taskRelations.taskId1, id1), eq(taskRelations.taskId2, id2)),
  ).get();
  if ((existing?.cnt ?? 0) === 0) return { type: 'no-change', message: `(${taskId1}) is not related to (${taskId2})` };

  db.delete(taskRelations).where(
    and(eq(taskRelations.taskId1, id1), eq(taskRelations.taskId2, id2)),
  ).run();
  syncRelatedMetadata(db, taskId1);
  syncRelatedMetadata(db, taskId2);

  return { type: 'success', message: `(${taskId1}) is no longer related to (${taskId2})` };
}

/** Batch-fetch relationship counts for display (subtasks, blocks, blockedBy, related). */
export interface TaskRelCounts {
  subtaskCount: number;
  blocksCount: number;
  blockedByCount: number;
  relatedCount: number;
}

export function getRelationshipCounts(db: TaskerDb, taskIds: TaskId[]): Record<string, TaskRelCounts> {
  if (taskIds.length === 0) return {};

  const raw = getRawDb(db);
  const result: Record<string, TaskRelCounts> = {};
  for (const id of taskIds) {
    result[id] = { subtaskCount: 0, blocksCount: 0, blockedByCount: 0, relatedCount: 0 };
  }

  // Subtask counts: tasks where parent_id = taskId
  const subtaskRows = raw.prepare(
    `SELECT parent_id AS id, COUNT(*) AS cnt FROM tasks WHERE parent_id IS NOT NULL AND is_trashed = 0 GROUP BY parent_id`,
  ).all() as { id: string; cnt: number }[];
  for (const r of subtaskRows) {
    if (result[r.id]) result[r.id]!.subtaskCount = r.cnt;
  }

  // Blocks counts: tasks this task blocks (task_id = taskId in task_dependencies)
  const blocksRows = raw.prepare(
    `SELECT task_id AS id, COUNT(*) AS cnt FROM task_dependencies GROUP BY task_id`,
  ).all() as { id: string; cnt: number }[];
  for (const r of blocksRows) {
    if (result[r.id]) result[r.id]!.blocksCount = r.cnt;
  }

  // BlockedBy counts: tasks blocking this task (blocks_task_id = taskId)
  const blockedByRows = raw.prepare(
    `SELECT blocks_task_id AS id, COUNT(*) AS cnt FROM task_dependencies GROUP BY blocks_task_id`,
  ).all() as { id: string; cnt: number }[];
  for (const r of blockedByRows) {
    if (result[r.id]) result[r.id]!.blockedByCount = r.cnt;
  }

  // Related counts: both directions
  const relRows = raw.prepare(
    `SELECT task_id_1 AS id, COUNT(*) AS cnt FROM task_relations GROUP BY task_id_1
     UNION ALL
     SELECT task_id_2 AS id, COUNT(*) AS cnt FROM task_relations GROUP BY task_id_2`,
  ).all() as { id: string; cnt: number }[];
  for (const r of relRows) {
    if (result[r.id]) result[r.id]!.relatedCount += r.cnt;
  }

  return result;
}

/** Summary info for a linked task (title + status). */
export interface TaskSummary {
  title: string;
  status: number;
}

/** Batch-fetch display titles and statuses for task IDs. */
export function getTaskTitles(db: TaskerDb, taskIds: TaskId[]): Record<string, TaskSummary> {
  if (taskIds.length === 0) return {};
  const raw = getRawDb(db);
  const result: Record<string, TaskSummary> = {};
  // Fetch all in one query using IN clause
  const placeholders = taskIds.map(() => '?').join(',');
  const rows = raw.prepare(
    `SELECT id, description, status FROM tasks WHERE id IN (${placeholders})`,
  ).all(...taskIds) as { id: string; description: string; status: number }[];
  for (const row of rows) {
    const display = getDisplayDescription(row.description);
    result[row.id] = {
      title: display.split('\n')[0]!,
      status: row.status,
    };
  }
  return result;
}

/** Apply system sort order to the database, persisting it as the user's sort order */
export function applySystemSort(db: TaskerDb, listName?: ListName): number {
  const raw = getRawDb(db);
  const listNames = listName ? [listName] : getAllListNames(db);

  const run = raw.transaction(() => {
    for (const name of listNames) {
      const listTasks = getAllTasks(db, name);
      const sorted = sortTasksForDisplay(listTasks);
      // Highest sort_order = first in display (sorted[0])
      for (let i = 0; i < sorted.length; i++) {
        db.update(tasks).set({ sortOrder: sorted.length - 1 - i }).where(eq(tasks.id, sorted[i]!.id)).run();
      }
    }
  });
  run();

  return listNames.length;
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
    db.update(tasks).set({ description: synced }).where(eq(tasks.id, taskId)).run();
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
    db.update(tasks).set({ description: synced }).where(eq(tasks.id, taskId)).run();
  }
}

function syncBlockingRelationships(db: TaskerDb, taskId: TaskId, oldIds: string[], newIds: string[] | null): void {
  const oldSet = new Set(oldIds);
  const newSet = new Set(newIds ?? []);

  for (const removed of oldSet) {
    if (!newSet.has(removed)) {
      db.delete(taskDependencies).where(
        and(eq(taskDependencies.taskId, taskId), eq(taskDependencies.blocksTaskId, removed)),
      ).run();
    }
  }

  for (const added of newSet) {
    if (!oldSet.has(added)) {
      const blocked = getTaskById(db, added);
      if (blocked && added !== taskId && !hasCircularBlocking(db, taskId, added)) {
        db.insert(taskDependencies).values({ taskId, blocksTaskId: added }).onConflictDoNothing().run();
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
    db.update(tasks).set({ description: synced }).where(eq(tasks.id, taskId)).run();
  }
}

function syncRelatedRelationships(db: TaskerDb, taskId: TaskId, oldIds: string[], newIds: string[] | null): void {
  const oldSet = new Set(oldIds);
  const newSet = new Set(newIds ?? []);

  for (const removed of oldSet) {
    if (!newSet.has(removed)) {
      const [id1, id2] = taskId < removed ? [taskId, removed] : [removed, taskId];
      db.delete(taskRelations).where(
        and(eq(taskRelations.taskId1, id1), eq(taskRelations.taskId2, id2)),
      ).run();
      syncRelatedMetadata(db, removed);
    }
  }

  for (const added of newSet) {
    if (!oldSet.has(added)) {
      const related = getTaskById(db, added);
      if (related && added !== taskId) {
        const [id1, id2] = taskId < added ? [taskId, added] : [added, taskId];
        db.insert(taskRelations).values({ taskId1: id1, taskId2: id2 }).onConflictDoNothing().run();
        syncRelatedMetadata(db, added);
      }
    }
  }
}

