/**
 * Parses GitHub-style search filter strings into structured filters.
 * Tokens like `tag:ui status:done due:today` are extracted; remaining
 * text becomes the description query for LIKE matching.
 *
 * Negation: prefix the value with `!` (e.g. `status:!done`, `tag:!ui`).
 * ID filter: `id:abc` matches task IDs by prefix.
 */

import { TaskStatus } from '../types/task-status.js';
import type { Priority } from '../types/priority.js';
import { Priority as P } from '../types/priority.js';

export interface SearchFilters {
  tags: string[];
  status: (typeof TaskStatus)[keyof typeof TaskStatus] | null;
  priority: Priority | null;
  dueFilter: 'today' | 'overdue' | 'week' | 'month' | null;
  listName: string | null;
  has: { subtasks?: boolean; parent?: boolean; due?: boolean; tags?: boolean };
  descriptionQuery: string;

  // Negation filters
  notStatus: (typeof TaskStatus)[keyof typeof TaskStatus] | null;
  notPriority: Priority | null;
  notTags: string[];
  notDueFilter: 'today' | 'overdue' | 'week' | 'month' | null;
  notListName: string | null;
  notHas: { subtasks?: boolean; parent?: boolean; due?: boolean; tags?: boolean };

  // ID prefix filter
  idPrefix: string | null;
}

const STATUS_MAP: Record<string, (typeof TaskStatus)[keyof typeof TaskStatus]> = {
  pending: TaskStatus.Pending,
  wip: TaskStatus.InProgress,
  'in-progress': TaskStatus.InProgress,
  inprogress: TaskStatus.InProgress,
  done: TaskStatus.Done,
};

const PRIORITY_MAP: Record<string, Priority> = {
  high: P.High,
  p1: P.High,
  medium: P.Medium,
  p2: P.Medium,
  low: P.Low,
  p3: P.Low,
};

const DUE_VALUES = new Set(['today', 'overdue', 'week', 'month']);
const HAS_VALUES = new Set(['subtasks', 'parent', 'due', 'tags']);

// Matches prefix:value tokens — value can be quoted or unquoted
const TOKEN_RE = /\b(tag|status|priority|due|list|has|id):("[^"]*"|[^\s]+)/gi;

export function parseSearchFilters(query: string): SearchFilters {
  const filters: SearchFilters = {
    tags: [],
    status: null,
    priority: null,
    dueFilter: null,
    listName: null,
    has: {},
    descriptionQuery: '',
    notStatus: null,
    notPriority: null,
    notTags: [],
    notDueFilter: null,
    notListName: null,
    notHas: {},
    idPrefix: null,
  };

  // Extract all filter tokens and track their positions
  const remaining = query.replace(TOKEN_RE, (_, prefix: string, rawValue: string) => {
    // Strip quotes if present
    const unquoted = rawValue.replace(/^"|"$/g, '');
    const key = prefix.toLowerCase();

    // ID filter — no negation, preserve original case
    if (key === 'id') {
      filters.idPrefix = unquoted;
      return '';
    }

    // Detect negation
    const negated = unquoted.startsWith('!');
    const cleanValue = negated ? unquoted.slice(1) : unquoted;
    const value = cleanValue.toLowerCase();

    switch (key) {
      case 'tag':
        if (negated) {
          filters.notTags.push(value);
        } else {
          filters.tags.push(value);
        }
        break;
      case 'status':
        if (STATUS_MAP[value] !== undefined) {
          if (negated) {
            filters.notStatus = STATUS_MAP[value]!;
          } else {
            filters.status = STATUS_MAP[value]!;
          }
        } else {
          return `${prefix}:${rawValue}`; // Unknown status — keep as text
        }
        break;
      case 'priority':
        if (PRIORITY_MAP[value] !== undefined) {
          if (negated) {
            filters.notPriority = PRIORITY_MAP[value]!;
          } else {
            filters.priority = PRIORITY_MAP[value]!;
          }
        } else {
          return `${prefix}:${rawValue}`;
        }
        break;
      case 'due':
        if (DUE_VALUES.has(value)) {
          if (negated) {
            filters.notDueFilter = value as SearchFilters['dueFilter'];
          } else {
            filters.dueFilter = value as SearchFilters['dueFilter'];
          }
        } else {
          return `${prefix}:${rawValue}`;
        }
        break;
      case 'list':
        if (negated) {
          filters.notListName = cleanValue; // Preserve original case
        } else {
          filters.listName = rawValue.replace(/^"|"$/g, ''); // Preserve original case for list names
        }
        break;
      case 'has':
        if (HAS_VALUES.has(value)) {
          if (negated) {
            filters.notHas[value as keyof SearchFilters['notHas']] = true;
          } else {
            filters.has[value as keyof SearchFilters['has']] = true;
          }
        } else {
          return `${prefix}:${rawValue}`;
        }
        break;
    }
    return ''; // Remove matched token from remaining text
  });

  filters.descriptionQuery = remaining.replace(/\s+/g, ' ').trim();
  return filters;
}
