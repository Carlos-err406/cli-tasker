import { describe, it, expect, beforeEach } from 'vitest';
import {
  createTestDb,
  addTask,
  getTaskById,
  setStatus,
  renameTask,
  deleteTask,
  moveTask,
  searchTasks,
  getSortedTasks,
  getStats,
  getAllListNames,
  createList,
  deleteList,
  renameList,
  reorderList,
  getListIndex,
  isListCollapsed,
  setListCollapsed,
  restoreFromTrash,
  UndoManager,
  TaskStatus,
  Priority,
} from '@tasker/core';
import type { TaskerDb } from '@tasker/core';

// These tests exercise the same core operations that the IPC handlers call,
// verifying the data layer works correctly for the desktop app.

let db: TaskerDb;
let undo: InstanceType<typeof UndoManager>;

beforeEach(() => {
  db = createTestDb();
  undo = new UndoManager(db);
});

describe('Task IPC operations', () => {
  it('adds a task and retrieves it', () => {
    const result = addTask(db, 'Buy groceries', 'tasks');
    expect(result.task.id).toBeTruthy();
    expect(result.task.description).toBe('Buy groceries');

    const found = getTaskById(db, result.task.id);
    expect(found).not.toBeNull();
    expect(found!.description).toBe('Buy groceries');
  });

  it('records undo for add task', () => {
    const result = addTask(db, 'Test task', 'tasks');
    undo.recordCommand({
      $type: 'add',
      task: result.task,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();
    expect(undo.canUndo).toBe(true);

    const desc = undo.undo();
    expect(desc).toBeTruthy();
    // Task should be in trash after undo of add
    const found = getTaskById(db, result.task.id);
    expect(found).toBeNull();
  });

  it('sets task status with undo', () => {
    const { task } = addTask(db, 'Test task', 'tasks');
    const oldStatus = task.status;

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand({
      $type: 'set-status',
      taskId: task.id,
      oldStatus,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    const updated = getTaskById(db, task.id);
    expect(updated!.status).toBe(TaskStatus.Done);

    undo.undo();
    const reverted = getTaskById(db, task.id);
    expect(reverted!.status).toBe(TaskStatus.Pending);
  });

  it('renames task with undo', () => {
    const { task } = addTask(db, 'Original name', 'tasks');

    renameTask(db, task.id, 'New name');
    undo.recordCommand({
      $type: 'rename',
      taskId: task.id,
      oldDescription: 'Original name',
      newDescription: 'New name',
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    const renamed = getTaskById(db, task.id);
    expect(renamed!.description).toBe('New name');

    undo.undo();
    const reverted = getTaskById(db, task.id);
    expect(reverted!.description).toBe('Original name');
  });

  it('deletes task with undo', () => {
    const { task } = addTask(db, 'To delete', 'tasks');

    deleteTask(db, task.id);
    undo.recordCommand({
      $type: 'delete',
      deletedTask: task,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    expect(getTaskById(db, task.id)).toBeNull();

    undo.undo();
    const restored = getTaskById(db, task.id);
    expect(restored).not.toBeNull();
    expect(restored!.description).toBe('To delete');
  });

  it('moves task between lists', () => {
    createList(db, 'work');
    const { task } = addTask(db, 'Task to move', 'tasks');

    moveTask(db, task.id, 'work');
    undo.recordCommand({
      $type: 'move',
      taskId: task.id,
      sourceList: 'tasks',
      targetList: 'work',
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    const moved = getTaskById(db, task.id);
    expect(moved!.listName).toBe('work');

    undo.undo();
    const reverted = getTaskById(db, task.id);
    expect(reverted!.listName).toBe('tasks');
  });

  it('searches tasks', () => {
    addTask(db, 'Buy apples', 'tasks');
    addTask(db, 'Buy oranges', 'tasks');
    addTask(db, 'Clean house', 'tasks');

    const results = searchTasks(db, 'buy');
    expect(results).toHaveLength(2);
  });

  it('gets sorted tasks with filters', () => {
    addTask(db, 'Pending task', 'tasks');
    const { task } = addTask(db, 'Done task', 'tasks');
    setStatus(db, task.id, TaskStatus.Done);

    const pending = getSortedTasks(db, { status: TaskStatus.Pending });
    expect(pending.every((t) => t.status === TaskStatus.Pending)).toBe(true);
  });

  it('gets stats', () => {
    addTask(db, 'Task 1', 'tasks');
    addTask(db, 'Task 2', 'tasks');
    const { task } = addTask(db, 'Task 3', 'tasks');
    setStatus(db, task.id, TaskStatus.Done);

    const stats = getStats(db);
    expect(stats.total).toBe(3);
    expect(stats.pending).toBe(2);
    expect(stats.done).toBe(1);
  });

  it('restores from trash', () => {
    const { task } = addTask(db, 'To trash', 'tasks');
    deleteTask(db, task.id);
    expect(getTaskById(db, task.id)).toBeNull();

    restoreFromTrash(db, task.id);
    const restored = getTaskById(db, task.id);
    expect(restored).not.toBeNull();
  });

  it('adds task with metadata markers', () => {
    const result = addTask(db, 'Buy groceries\n@tomorrow #shopping p1', 'tasks');
    expect(result.task.priority).toBe(Priority.High);
    expect(result.task.tags).toContain('shopping');
    expect(result.task.dueDate).toBeTruthy();
  });
});

describe('List IPC operations', () => {
  it('creates and lists', () => {
    const initial = getAllListNames(db);
    expect(initial).toContain('tasks');

    createList(db, 'work');
    const lists = getAllListNames(db);
    expect(lists).toContain('work');
  });

  it('deletes list with undo', () => {
    createList(db, 'temp');
    addTask(db, 'Task in temp', 'temp');

    const tasks = getSortedTasks(db, { listName: 'temp' });
    deleteList(db, 'temp');
    undo.recordCommand({
      $type: 'deleteList',
      listName: 'temp',
      deletedTasks: tasks,
      trashedTasks: [],
      wasDefaultList: false,
      originalIndex: getListIndex(db, 'tasks') + 1,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    expect(getAllListNames(db)).not.toContain('temp');
  });

  it('renames list', () => {
    createList(db, 'old-name');
    renameList(db, 'old-name', 'new-name');
    expect(getAllListNames(db)).toContain('new-name');
    expect(getAllListNames(db)).not.toContain('old-name');
  });

  it('manages list collapsed state', () => {
    expect(isListCollapsed(db, 'tasks')).toBe(false);
    setListCollapsed(db, 'tasks', true);
    expect(isListCollapsed(db, 'tasks')).toBe(true);
    setListCollapsed(db, 'tasks', false);
    expect(isListCollapsed(db, 'tasks')).toBe(false);
  });

  it('reorders lists', () => {
    createList(db, 'alpha');
    createList(db, 'beta');
    const initialIndex = getListIndex(db, 'beta');
    reorderList(db, 'beta', 0);
    const newIndex = getListIndex(db, 'beta');
    expect(newIndex).toBeLessThan(initialIndex);
  });
});

describe('Undo IPC operations', () => {
  it('reports can undo/redo state', () => {
    expect(undo.canUndo).toBe(false);
    expect(undo.canRedo).toBe(false);

    const { task } = addTask(db, 'Test', 'tasks');
    undo.recordCommand({
      $type: 'add',
      task,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    expect(undo.canUndo).toBe(true);
    expect(undo.canRedo).toBe(false);

    undo.undo();
    expect(undo.canRedo).toBe(true);
  });

  it('redo after undo restores status', () => {
    const { task } = addTask(db, 'Test', 'tasks');
    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand({
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    undo.undo();
    expect(getTaskById(db, task.id)!.status).toBe(TaskStatus.Pending);

    undo.redo();
    expect(getTaskById(db, task.id)!.status).toBe(TaskStatus.Done);
  });

  it('reload history preserves state', () => {
    const { task } = addTask(db, 'Test', 'tasks');
    undo.recordCommand({
      $type: 'add',
      task,
      executedAt: new Date().toISOString(),
    });
    undo.saveHistory();

    // Simulate what happens when popup opens (reload from disk)
    undo.reloadHistory();
    expect(undo.canUndo).toBe(true);
  });
});
