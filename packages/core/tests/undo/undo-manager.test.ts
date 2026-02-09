import { describe, it, expect, beforeEach } from 'vitest';
import { createTestDb, type TaskerDb } from '../../src/db.js';
import { UndoManager } from '../../src/undo/undo-manager.js';
import { addTask, getTaskById, setStatus, deleteTask, moveTask } from '../../src/queries/task-queries.js';
import { TaskStatus } from '../../src/types/task-status.js';
import type { SetStatusCmd, AddTaskCmd, DeleteTaskCmd, MoveTaskCmd } from '../../src/undo/undo-commands.js';

let db: TaskerDb;
let undo: UndoManager;

beforeEach(() => {
  db = createTestDb();
  undo = new UndoManager(db);
});

describe('UndoManager', () => {
  it('starts with empty stacks', () => {
    expect(undo.canUndo).toBe(false);
    expect(undo.canRedo).toBe(false);
    expect(undo.undoCount).toBe(0);
    expect(undo.redoCount).toBe(0);
  });

  it('records a command and enables undo', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const cmd: SetStatusCmd = {
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    };

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand(cmd);

    expect(undo.canUndo).toBe(true);
    expect(undo.undoCount).toBe(1);
  });

  it('undoes a set-status command', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const cmd: SetStatusCmd = {
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    };

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand(cmd);
    undo.saveHistory();

    const description = undo.undo();
    expect(description).not.toBeNull();

    const restored = getTaskById(db, task.id)!;
    expect(restored.status).toBe(TaskStatus.Pending);
    expect(undo.canRedo).toBe(true);
  });

  it('redoes after undo', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const cmd: SetStatusCmd = {
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    };

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand(cmd);
    undo.saveHistory();

    undo.undo();
    expect(getTaskById(db, task.id)!.status).toBe(TaskStatus.Pending);

    undo.redo();
    expect(getTaskById(db, task.id)!.status).toBe(TaskStatus.Done);
  });

  it('clears redo stack when new command is recorded', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const cmd: SetStatusCmd = {
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    };

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand(cmd);
    undo.saveHistory();

    undo.undo();
    expect(undo.canRedo).toBe(true);

    // Record a new command — redo should be cleared
    undo.recordCommand({
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.InProgress,
      executedAt: new Date().toISOString(),
    });

    expect(undo.canRedo).toBe(false);
  });

  it('handles batch (composite) commands', () => {
    const { task: t1 } = addTask(db, 'task 1', 'tasks');
    const { task: t2 } = addTask(db, 'task 2', 'tasks');

    setStatus(db, t1.id, TaskStatus.Done);
    setStatus(db, t2.id, TaskStatus.Done);

    undo.beginBatch('Mark 2 tasks done');
    undo.recordCommand({
      $type: 'set-status',
      taskId: t1.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });
    undo.recordCommand({
      $type: 'set-status',
      taskId: t2.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });
    undo.endBatch();
    undo.saveHistory();

    // Should be a single undo entry
    expect(undo.undoCount).toBe(1);

    undo.undo();

    // Both should be reverted
    expect(getTaskById(db, t1.id)!.status).toBe(TaskStatus.Pending);
    expect(getTaskById(db, t2.id)!.status).toBe(TaskStatus.Pending);
  });

  it('undoes add-task command (permanently deletes)', () => {
    const { task } = addTask(db, 'test task', 'tasks');
    const cmd: AddTaskCmd = {
      $type: 'add',
      task,
      executedAt: new Date().toISOString(),
    };

    undo.recordCommand(cmd);
    undo.saveHistory();

    undo.undo();
    expect(getTaskById(db, task.id)).toBeNull();
  });

  it('undoes delete-task command (restores from trash)', () => {
    const { task } = addTask(db, 'test task', 'tasks');
    deleteTask(db, task.id);

    const cmd: DeleteTaskCmd = {
      $type: 'delete',
      deletedTask: task,
      executedAt: new Date().toISOString(),
    };

    undo.recordCommand(cmd);
    undo.saveHistory();

    undo.undo();
    expect(getTaskById(db, task.id)).not.toBeNull();
  });

  it('undoes move-task command', () => {
    const { task } = addTask(db, 'test', 'tasks');
    moveTask(db, task.id, 'work');

    const cmd: MoveTaskCmd = {
      $type: 'move',
      taskId: task.id,
      sourceList: 'tasks',
      targetList: 'work',
      executedAt: new Date().toISOString(),
    };

    undo.recordCommand(cmd);
    undo.saveHistory();

    undo.undo();
    expect(getTaskById(db, task.id)!.listName).toBe('tasks');
  });

  it('persists and reloads history', () => {
    const { task } = addTask(db, 'test', 'tasks');
    const cmd: SetStatusCmd = {
      $type: 'set-status',
      taskId: task.id,
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    };

    setStatus(db, task.id, TaskStatus.Done);
    undo.recordCommand(cmd);
    undo.saveHistory();

    // Create a new UndoManager — should load from DB
    const undo2 = new UndoManager(db);
    expect(undo2.canUndo).toBe(true);
    expect(undo2.undoCount).toBe(1);
  });

  it('enforces max size limits', () => {
    // Record 55 commands (max is 50)
    for (let i = 0; i < 55; i++) {
      undo.recordCommand({
        $type: 'set-status',
        taskId: 'abc',
        oldStatus: TaskStatus.Pending,
        newStatus: TaskStatus.Done,
        executedAt: new Date().toISOString(),
      });
    }

    expect(undo.undoCount).toBe(50);
  });

  it('clears history', () => {
    undo.recordCommand({
      $type: 'set-status',
      taskId: 'abc',
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });

    undo.clearHistory();
    expect(undo.canUndo).toBe(false);
    expect(undo.canRedo).toBe(false);
  });

  it('cancels batch without recording', () => {
    undo.beginBatch('test batch');
    undo.recordCommand({
      $type: 'set-status',
      taskId: 'abc',
      oldStatus: TaskStatus.Pending,
      newStatus: TaskStatus.Done,
      executedAt: new Date().toISOString(),
    });
    undo.cancelBatch();

    expect(undo.canUndo).toBe(false);
  });

  it('returns null when nothing to undo/redo', () => {
    expect(undo.undo()).toBeNull();
    expect(undo.redo()).toBeNull();
  });
});
