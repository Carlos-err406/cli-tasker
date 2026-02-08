import { describe, it, expect, beforeEach } from 'vitest';
import { createTestDb, type TaskerDb } from '../../src/db.js';
import {
  addTask,
  getTaskById,
  getAllTasks,
  getSortedTasks,
  setStatus,
  deleteTask,
  renameTask,
  moveTask,
  setTaskDueDate,
  setTaskPriority,
  getTrash,
  restoreFromTrash,
  clearTrash,
  clearTasks,
  searchTasks,
  getStats,
  reorderTask,
  getSubtasks,
  setParent,
  unsetParent,
  addBlocker,
  removeBlocker,
  addRelated,
  removeRelated,
  getBlocksIds,
  getBlockedByIds,
  getRelatedIds,
  getAllDescendantIds,
} from '../../src/queries/task-queries.js';
import {
  getAllListNames,
  createList,
  listExists,
} from '../../src/queries/list-queries.js';
import { TaskStatus } from '../../src/types/task-status.js';
import { Priority } from '../../src/types/priority.js';

let db: TaskerDb;

beforeEach(() => {
  db = createTestDb();
});

describe('addTask', () => {
  it('creates a task with a 3-char ID', () => {
    const { task } = addTask(db, 'My first task', 'tasks');
    expect(task.id).toHaveLength(3);
    expect(task.description).toBe('My first task');
    expect(task.status).toBe(TaskStatus.Pending);
    expect(task.listName).toBe('tasks');
  });

  it('parses inline metadata', () => {
    const { task } = addTask(db, 'Important task\np1 @2026-03-01 #urgent', 'tasks');
    expect(task.priority).toBe(Priority.High);
    expect(task.dueDate).toBe('2026-03-01');
    expect(task.tags).toContain('urgent');
  });

  it('auto-creates list if needed', () => {
    addTask(db, 'task in new list', 'work');
    expect(listExists(db, 'work')).toBe(true);
  });

  it('validates parent reference', () => {
    const { warnings } = addTask(db, 'child\n^zzz', 'tasks');
    expect(warnings.some(w => w.includes('not found'))).toBe(true);
  });
});

describe('getTaskById', () => {
  it('returns task if found', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const found = getTaskById(db, task.id);
    expect(found).not.toBeNull();
    expect(found!.id).toBe(task.id);
  });

  it('returns null for nonexistent', () => {
    expect(getTaskById(db, 'zzz')).toBeNull();
  });

  it('excludes trashed tasks', () => {
    const { task } = addTask(db, 'test', 'tasks');
    deleteTask(db, task.id);
    expect(getTaskById(db, task.id)).toBeNull();
  });
});

describe('setStatus', () => {
  it('changes status', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const result = setStatus(db, task.id, TaskStatus.Done);
    expect(result.type).toBe('success');
    const updated = getTaskById(db, task.id)!;
    expect(updated.status).toBe(TaskStatus.Done);
    expect(updated.completedAt).not.toBeNull();
  });

  it('returns no-change when already at target status', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const result = setStatus(db, task.id, TaskStatus.Pending);
    expect(result.type).toBe('no-change');
  });

  it('returns not-found for missing task', () => {
    const result = setStatus(db, 'zzz', TaskStatus.Done);
    expect(result.type).toBe('not-found');
  });

  it('cascades to descendants when marking Done', () => {
    const { task: parent } = addTask(db, 'parent', 'tasks');
    const { task: child } = addTask(db, `child\n^${parent.id}`, 'tasks');

    setStatus(db, parent.id, TaskStatus.Done);

    const updatedChild = getTaskById(db, child.id)!;
    expect(updatedChild.status).toBe(TaskStatus.Done);
  });
});

describe('deleteTask', () => {
  it('moves task to trash', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const result = deleteTask(db, task.id);
    expect(result.type).toBe('success');
    expect(getTaskById(db, task.id)).toBeNull();
    expect(getTrash(db)).toHaveLength(1);
  });

  it('cascades to descendants', () => {
    const { task: parent } = addTask(db, 'parent', 'tasks');
    addTask(db, `child\n^${parent.id}`, 'tasks');

    deleteTask(db, parent.id);

    expect(getTrash(db)).toHaveLength(2);
  });
});

describe('renameTask', () => {
  it('updates description', () => {
    const { task } = addTask(db, 'old name', 'tasks');
    const result = renameTask(db, task.id, 'new name');
    expect(result.type).toBe('success');
    const updated = getTaskById(db, task.id)!;
    expect(updated.description).toBe('new name');
  });

  it('parses new metadata on rename', () => {
    const { task } = addTask(db, 'task', 'tasks');
    renameTask(db, task.id, 'task\np1 #urgent');
    const updated = getTaskById(db, task.id)!;
    expect(updated.priority).toBe(Priority.High);
    expect(updated.tags).toContain('urgent');
  });
});

describe('moveTask', () => {
  it('moves task to a different list', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const result = moveTask(db, task.id, 'work');
    expect(result.type).toBe('success');
    const updated = getTaskById(db, task.id)!;
    expect(updated.listName).toBe('work');
  });

  it('returns error when moving a subtask', () => {
    const { task: parent } = addTask(db, 'parent', 'tasks');
    const { task: child } = addTask(db, `child\n^${parent.id}`, 'tasks');
    const result = moveTask(db, child.id, 'work');
    expect(result.type).toBe('error');
  });
});

describe('setTaskDueDate / setTaskPriority', () => {
  it('sets and clears due date', () => {
    const { task } = addTask(db, 'test', 'tasks');
    setTaskDueDate(db, task.id, '2026-03-01');
    expect(getTaskById(db, task.id)!.dueDate).toBe('2026-03-01');

    setTaskDueDate(db, task.id, null);
    expect(getTaskById(db, task.id)!.dueDate).toBeNull();
  });

  it('sets and clears priority', () => {
    const { task } = addTask(db, 'test', 'tasks');
    setTaskPriority(db, task.id, Priority.High);
    expect(getTaskById(db, task.id)!.priority).toBe(Priority.High);

    setTaskPriority(db, task.id, null);
    expect(getTaskById(db, task.id)!.priority).toBeNull();
  });
});

describe('trash operations', () => {
  it('restores from trash', () => {
    const { task } = addTask(db, 'test', 'tasks');
    deleteTask(db, task.id);
    restoreFromTrash(db, task.id);
    expect(getTaskById(db, task.id)).not.toBeNull();
    expect(getTrash(db)).toHaveLength(0);
  });

  it('clears trash permanently', () => {
    const { task } = addTask(db, 'test', 'tasks');
    deleteTask(db, task.id);
    const count = clearTrash(db);
    expect(count).toBe(1);
    expect(getTrash(db)).toHaveLength(0);
  });
});

describe('clearTasks', () => {
  it('moves all tasks to trash', () => {
    addTask(db, 'task1', 'tasks');
    addTask(db, 'task2', 'tasks');
    const count = clearTasks(db, 'tasks');
    expect(count).toBe(2);
    expect(getAllTasks(db, 'tasks')).toHaveLength(0);
    expect(getTrash(db)).toHaveLength(2);
  });
});

describe('searchTasks', () => {
  it('finds tasks by description', () => {
    addTask(db, 'build the API', 'tasks');
    addTask(db, 'write tests', 'tasks');
    const results = searchTasks(db, 'API');
    expect(results).toHaveLength(1);
    expect(results[0]!.description).toBe('build the API');
  });
});

describe('getStats', () => {
  it('counts tasks by status', () => {
    const { task: t1 } = addTask(db, 'pending', 'tasks');
    const { task: t2 } = addTask(db, 'done', 'tasks');
    setStatus(db, t2.id, TaskStatus.Done);
    const { task: t3 } = addTask(db, 'trashed', 'tasks');
    deleteTask(db, t3.id);

    const stats = getStats(db, 'tasks');
    expect(stats.total).toBe(2);
    expect(stats.pending).toBe(1);
    expect(stats.done).toBe(1);
    expect(stats.trash).toBe(1);
  });
});

describe('parent/child relationships', () => {
  it('sets and unsets parent', () => {
    const { task: parent } = addTask(db, 'parent', 'tasks');
    const { task: child } = addTask(db, 'child', 'tasks');

    const setResult = setParent(db, child.id, parent.id);
    expect(setResult.type).toBe('success');
    expect(getTaskById(db, child.id)!.parentId).toBe(parent.id);
    expect(getSubtasks(db, parent.id)).toHaveLength(1);

    const unsetResult = unsetParent(db, child.id);
    expect(unsetResult.type).toBe('success');
    expect(getTaskById(db, child.id)!.parentId).toBeNull();
  });

  it('prevents circular parent reference', () => {
    const { task: a } = addTask(db, 'a', 'tasks');
    const { task: b } = addTask(db, 'b', 'tasks');
    setParent(db, b.id, a.id);

    const result = setParent(db, a.id, b.id);
    expect(result.type).toBe('error');
  });

  it('prevents self-parent', () => {
    const { task } = addTask(db, 'task', 'tasks');
    const result = setParent(db, task.id, task.id);
    expect(result.type).toBe('error');
  });
});

describe('blocker relationships', () => {
  it('adds and removes blockers', () => {
    const { task: a } = addTask(db, 'a', 'tasks');
    const { task: b } = addTask(db, 'b', 'tasks');

    const addResult = addBlocker(db, a.id, b.id);
    expect(addResult.type).toBe('success');
    expect(getBlocksIds(db, a.id)).toContain(b.id);
    expect(getBlockedByIds(db, b.id)).toContain(a.id);

    const removeResult = removeBlocker(db, a.id, b.id);
    expect(removeResult.type).toBe('success');
    expect(getBlocksIds(db, a.id)).toHaveLength(0);
  });

  it('prevents circular blocking', () => {
    const { task: a } = addTask(db, 'a', 'tasks');
    const { task: b } = addTask(db, 'b', 'tasks');
    addBlocker(db, a.id, b.id);

    const result = addBlocker(db, b.id, a.id);
    expect(result.type).toBe('error');
  });
});

describe('related relationships', () => {
  it('adds and removes related tasks', () => {
    const { task: a } = addTask(db, 'a', 'tasks');
    const { task: b } = addTask(db, 'b', 'tasks');

    const addResult = addRelated(db, a.id, b.id);
    expect(addResult.type).toBe('success');
    expect(getRelatedIds(db, a.id)).toContain(b.id);
    expect(getRelatedIds(db, b.id)).toContain(a.id);

    const removeResult = removeRelated(db, a.id, b.id);
    expect(removeResult.type).toBe('success');
    expect(getRelatedIds(db, a.id)).toHaveLength(0);
  });
});

describe('reorderTask', () => {
  it('moves task to new position', () => {
    const { task: t1 } = addTask(db, 'first', 'tasks');
    const { task: t2 } = addTask(db, 'second', 'tasks');
    const { task: t3 } = addTask(db, 'third', 'tasks');

    // Display order is sort_order DESC, so t3 is at index 0
    reorderTask(db, t3.id, 2); // Move t3 to last

    const tasks = getAllTasks(db, 'tasks');
    // t2 should now be first (highest sort_order)
    expect(tasks[0]!.id).toBe(t2.id);
  });
});

describe('getSortedTasks', () => {
  it('sorts in-progress before pending before done', () => {
    const { task: t1 } = addTask(db, 'pending', 'tasks');
    const { task: t2 } = addTask(db, 'done', 'tasks');
    setStatus(db, t2.id, TaskStatus.Done);
    const { task: t3 } = addTask(db, 'in-progress', 'tasks');
    setStatus(db, t3.id, TaskStatus.InProgress);

    const sorted = getSortedTasks(db, { listName: 'tasks' });
    expect(sorted[0]!.status).toBe(TaskStatus.InProgress);
    expect(sorted[1]!.status).toBe(TaskStatus.Pending);
    expect(sorted[2]!.status).toBe(TaskStatus.Done);
  });
});
