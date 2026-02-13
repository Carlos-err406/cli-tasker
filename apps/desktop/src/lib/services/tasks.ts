import type { Task, TaskStatus, Priority, TaskResult } from '@tasker/core/types';
import type { AddResult, TaskRelCounts, TaskSummary } from '@tasker/core/queries';
import { IPC } from './ipc.js';

async function unwrap<T>(promise: Promise<[{ message: string } | null, T | null]>): Promise<T> {
  const [err, data] = await promise;
  if (err) throw new Error(err.message);
  return data as T;
}

export async function getAllTasks(listName?: string): Promise<Task[]> {
  return unwrap(IPC['tasks:getAll'](listName));
}

export async function getTaskById(taskId: string): Promise<Task | null> {
  return unwrap(IPC['tasks:getById'](taskId));
}

export async function searchTasks(query: string): Promise<Task[]> {
  return unwrap(IPC['tasks:search'](query));
}

export async function addTask(description: string, listName: string): Promise<AddResult> {
  return unwrap(IPC['tasks:add'](description, listName));
}

export async function setTaskStatus(taskId: string, status: TaskStatus): Promise<TaskResult> {
  return unwrap(IPC['tasks:setStatus'](taskId, status));
}

export async function renameTask(taskId: string, newDescription: string): Promise<TaskResult> {
  return unwrap(IPC['tasks:rename'](taskId, newDescription));
}

export async function deleteTask(taskId: string): Promise<TaskResult> {
  return unwrap(IPC['tasks:delete'](taskId));
}

export async function moveTask(taskId: string, targetList: string): Promise<TaskResult> {
  return unwrap(IPC['tasks:move'](taskId, targetList));
}

export async function reorderTask(taskId: string, newIndex: number): Promise<void> {
  return unwrap(IPC['tasks:reorder'](taskId, newIndex));
}

export async function setDueDate(taskId: string, dueDate: string | null): Promise<TaskResult> {
  return unwrap(IPC['tasks:setDueDate'](taskId, dueDate));
}

export async function setPriority(taskId: string, priority: Priority | null): Promise<TaskResult> {
  return unwrap(IPC['tasks:setPriority'](taskId, priority));
}

export async function getStats(listName?: string) {
  return unwrap(IPC['tasks:getStats'](listName));
}

export async function restoreTask(taskId: string): Promise<TaskResult> {
  return unwrap(IPC['tasks:restore'](taskId));
}

export async function getRelationshipCounts(taskIds: string[]): Promise<Record<string, TaskRelCounts>> {
  return unwrap(IPC['tasks:getRelCounts'](taskIds));
}

export async function getTaskTitles(taskIds: string[]): Promise<Record<string, TaskSummary>> {
  return unwrap(IPC['tasks:getTitles'](taskIds));
}

export async function applySystemSort(listName?: string): Promise<number> {
  return unwrap(IPC['tasks:applySystemSort'](listName));
}
