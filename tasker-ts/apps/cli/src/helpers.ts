/**
 * CLI helpers: list resolution, error handling, status parsing.
 */

import { basename } from 'node:path';
import type { TaskerDb } from '@tasker/core';
import type { TaskStatus as TaskStatusType, Priority as PriorityType } from '@tasker/core';
import { listExists, getDefaultList, TaskStatus, Priority } from '@tasker/core';
import * as out from './output.js';

/**
 * Resolve effective list filter.
 * Priority: explicit > auto-detect from cwd > null (all lists).
 */
export function resolveListFilter(
  db: TaskerDb,
  explicitList: string | undefined,
  showAll: boolean,
  cwd?: string,
): string | null {
  if (explicitList) return explicitList;
  if (showAll) return null;

  const workingDir = cwd ?? process.cwd();
  const dirName = basename(workingDir);
  if (dirName && listExists(db, dirName)) return dirName;
  return null;
}

/**
 * Resolve list for adding tasks.
 * Uses explicit list > auto-detect > default list.
 */
export function resolveListForAdd(
  db: TaskerDb,
  explicitList: string | undefined,
  showAll: boolean,
): string {
  const resolved = resolveListFilter(db, explicitList, showAll);
  return resolved ?? getDefaultList(db);
}

/**
 * Parse a status string into a TaskStatus value.
 */
export function parseStatus(status: string): TaskStatusType | null {
  switch (status.toLowerCase()) {
    case 'pending': return TaskStatus.Pending;
    case 'in-progress': case 'inprogress': case 'wip': return TaskStatus.InProgress;
    case 'done': case 'complete': case 'completed': return TaskStatus.Done;
    default: return null;
  }
}

/**
 * Parse a priority string into a Priority value or null for "clear".
 */
export function parsePriorityArg(level: string): PriorityType | null {
  switch (level.toLowerCase()) {
    case 'high': case '1': case 'p1': return Priority.High;
    case 'medium': case '2': case 'p2': return Priority.Medium;
    case 'low': case '3': case 'p3': return Priority.Low;
    case 'clear': return null;
    default: return null;
  }
}

/**
 * Wrap a command action with error handling.
 */
export function withErrorHandling(fn: () => void): () => void {
  return () => {
    try {
      fn();
    } catch (err: unknown) {
      if (err instanceof Error) {
        out.error(err.message);
      } else {
        out.error(String(err));
      }
    }
  };
}
