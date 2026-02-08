import type { Task, TaskStatus, Priority, TaskResult, AddResult } from '@tasker/core';
import type { TryResult } from '@utils/try.js';
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
} from './channels.js';

export const tasksInvokerFactory = (ipcRenderer: Electron.IpcRenderer) => ({
  [TASKS_GET_ALL]: ((listName?: string) =>
    ipcRenderer.invoke(TASKS_GET_ALL, listName)) as (
    listName?: string,
  ) => TryResult<Task[]>,

  [TASKS_GET_BY_ID]: ((taskId: string) =>
    ipcRenderer.invoke(TASKS_GET_BY_ID, taskId)) as (
    taskId: string,
  ) => TryResult<Task | null>,

  [TASKS_SEARCH]: ((query: string) =>
    ipcRenderer.invoke(TASKS_SEARCH, query)) as (
    query: string,
  ) => TryResult<Task[]>,

  [TASKS_ADD]: ((description: string, listName: string) =>
    ipcRenderer.invoke(TASKS_ADD, description, listName)) as (
    description: string,
    listName: string,
  ) => TryResult<AddResult>,

  [TASKS_SET_STATUS]: ((taskId: string, status: TaskStatus) =>
    ipcRenderer.invoke(TASKS_SET_STATUS, taskId, status)) as (
    taskId: string,
    status: TaskStatus,
  ) => TryResult<TaskResult>,

  [TASKS_RENAME]: ((taskId: string, newDescription: string) =>
    ipcRenderer.invoke(TASKS_RENAME, taskId, newDescription)) as (
    taskId: string,
    newDescription: string,
  ) => TryResult<TaskResult>,

  [TASKS_DELETE]: ((taskId: string) =>
    ipcRenderer.invoke(TASKS_DELETE, taskId)) as (
    taskId: string,
  ) => TryResult<TaskResult>,

  [TASKS_MOVE]: ((taskId: string, targetList: string) =>
    ipcRenderer.invoke(TASKS_MOVE, taskId, targetList)) as (
    taskId: string,
    targetList: string,
  ) => TryResult<TaskResult>,

  [TASKS_REORDER]: ((taskId: string, newIndex: number) =>
    ipcRenderer.invoke(TASKS_REORDER, taskId, newIndex)) as (
    taskId: string,
    newIndex: number,
  ) => TryResult<void>,

  [TASKS_SET_DUE_DATE]: ((taskId: string, dueDate: string | null) =>
    ipcRenderer.invoke(TASKS_SET_DUE_DATE, taskId, dueDate)) as (
    taskId: string,
    dueDate: string | null,
  ) => TryResult<TaskResult>,

  [TASKS_SET_PRIORITY]: ((taskId: string, priority: Priority | null) =>
    ipcRenderer.invoke(TASKS_SET_PRIORITY, taskId, priority)) as (
    taskId: string,
    priority: Priority | null,
  ) => TryResult<TaskResult>,

  [TASKS_GET_STATS]: ((listName?: string) =>
    ipcRenderer.invoke(TASKS_GET_STATS, listName)) as (
    listName?: string,
  ) => TryResult<{ total: number; pending: number; inProgress: number; done: number; trash: number }>,

  [TASKS_RESTORE]: ((taskId: string) =>
    ipcRenderer.invoke(TASKS_RESTORE, taskId)) as (
    taskId: string,
  ) => TryResult<TaskResult>,
});
