import {
  getAllTasks,
  getTaskById,
  searchTasks,
  addTask,
  setStatus,
  renameTask,
  deleteTask,
  moveTask,
  reorderTask,
  setTaskDueDate,
  setTaskPriority,
  restoreFromTrash,
  getStats,
  getRelationshipCounts,
  getTaskTitles,
  applySystemSort,
} from '@tasker/core';
import type { TaskStatus, Priority } from '@tasker/core';
import $try from '@utils/try.js';
import type { IPCRegisterFunction } from '../types.js';
import {
  TASKS_GET_ALL,
  TASKS_GET_BY_ID,
  TASKS_SEARCH,
  TASKS_ADD,
  TASKS_SET_STATUS,
  TASKS_RENAME,
  TASKS_DELETE,
  TASKS_MOVE,
  TASKS_REORDER,
  TASKS_SET_DUE_DATE,
  TASKS_SET_PRIORITY,
  TASKS_GET_STATS,
  TASKS_RESTORE,
  TASKS_GET_REL_COUNTS,
  TASKS_GET_TITLES,
  TASKS_APPLY_SYSTEM_SORT,
} from './channels.js';
import { log } from './utils.js';

export const tasksRegister: IPCRegisterFunction = (ipcMain, _widget, { db, undo }) => {
  ipcMain.handle(TASKS_GET_ALL, (_, listName?: string) => {
    log('getAll', listName ?? 'all');
    return $try(() => getAllTasks(db, listName));
  });

  ipcMain.handle(TASKS_GET_BY_ID, (_, taskId: string) => {
    log('getById', taskId);
    return $try(() => getTaskById(db, taskId));
  });

  ipcMain.handle(TASKS_SEARCH, (_, query: string) => {
    log('search', query);
    return $try(() => searchTasks(db, query));
  });

  ipcMain.handle(TASKS_ADD, (_, description: string, listName: string) => {
    log('add', description, listName);
    return $try(() => {
      const result = addTask(db, description, listName);
      undo.recordCommand({
        $type: 'add',
        task: result.task,
        executedAt: new Date().toISOString(),
      });
      undo.saveHistory();
      return result;
    });
  });

  ipcMain.handle(TASKS_SET_STATUS, (_, taskId: string, status: TaskStatus) => {
    log('setStatus', taskId, status);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const oldStatus = task.status;
      const result = setStatus(db, taskId, status);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'set-status',
          taskId,
          oldStatus,
          newStatus: status,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_RENAME, (_, taskId: string, newDescription: string) => {
    log('rename', taskId);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const oldDescription = task.description;
      const result = renameTask(db, taskId, newDescription);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'rename',
          taskId,
          oldDescription,
          newDescription,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_DELETE, (_, taskId: string) => {
    log('delete', taskId);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const result = deleteTask(db, taskId);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'delete',
          deletedTask: task,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_MOVE, (_, taskId: string, targetList: string) => {
    log('move', taskId, targetList);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const sourceList = task.listName;
      const result = moveTask(db, taskId, targetList);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'move',
          taskId,
          sourceList,
          targetList,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_REORDER, (_, taskId: string, newIndex: number) => {
    log('reorder', taskId, newIndex);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return;
      // Compute display index (position in sort_order DESC list), not raw sortOrder
      const listTasks = getAllTasks(db, task.listName);
      const oldIndex = listTasks.findIndex(t => t.id === taskId);
      reorderTask(db, taskId, newIndex);
      undo.recordCommand({
        $type: 'reorderTask',
        taskId,
        listName: task.listName,
        oldIndex,
        newIndex,
        executedAt: new Date().toISOString(),
      });
      undo.saveHistory();
    });
  });

  ipcMain.handle(TASKS_SET_DUE_DATE, (_, taskId: string, dueDate: string | null) => {
    log('setDueDate', taskId, dueDate);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const oldDueDate = task.dueDate;
      const result = setTaskDueDate(db, taskId, dueDate);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'metadata',
          taskId,
          oldDueDate,
          newDueDate: dueDate,
          oldPriority: task.priority,
          newPriority: task.priority,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_SET_PRIORITY, (_, taskId: string, priority: Priority | null) => {
    log('setPriority', taskId, priority);
    return $try(() => {
      const task = getTaskById(db, taskId);
      if (!task) return { type: 'not-found' as const, taskId };
      const oldPriority = task.priority;
      const result = setTaskPriority(db, taskId, priority);
      if (result.type === 'success') {
        undo.recordCommand({
          $type: 'metadata',
          taskId,
          oldDueDate: task.dueDate,
          newDueDate: task.dueDate,
          oldPriority,
          newPriority: priority,
          executedAt: new Date().toISOString(),
        });
        undo.saveHistory();
      }
      return result;
    });
  });

  ipcMain.handle(TASKS_GET_STATS, (_, listName?: string) => {
    log('getStats', listName ?? 'all');
    return $try(() => getStats(db, listName));
  });

  ipcMain.handle(TASKS_RESTORE, (_, taskId: string) => {
    log('restore', taskId);
    return $try(() => restoreFromTrash(db, taskId));
  });

  ipcMain.handle(TASKS_GET_REL_COUNTS, (_, taskIds: string[]) => {
    log('getRelCounts', `${taskIds.length} tasks`);
    return $try(() => getRelationshipCounts(db, taskIds));
  });

  ipcMain.handle(TASKS_GET_TITLES, (_, taskIds: string[]) => {
    log('getTitles', `${taskIds.length} tasks`);
    return $try(() => getTaskTitles(db, taskIds));
  });

  ipcMain.handle(TASKS_APPLY_SYSTEM_SORT, (_, listName?: string) => {
    log('applySystemSort', listName ?? 'all');
    return $try(() => applySystemSort(db, listName));
  });
};
