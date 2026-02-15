import { useCallback, useEffect, useReducer, useRef } from 'react';
import type { Task, TaskStatus } from '@tasker/core/types';
import { TaskStatus as TS } from '@tasker/core/types';
import { parseTaskDescription } from '@tasker/core/parsers';
import { arrayMove } from '@dnd-kit/sortable';
import * as taskService from '@/lib/services/tasks.js';
import * as listService from '@/lib/services/lists.js';
import * as undoService from '@/lib/services/undo.js';

/** A single relationship entry for display: "(id) title" + status badge. */
export interface RelEntry {
  id: string;
  title: string;
  status: number;
}

/** All relationship details for a single task */
export interface TaskRelDetails {
  parent: RelEntry | null;
  subtasks: RelEntry[];
  blocks: RelEntry[];
  blockedBy: RelEntry[];
  related: RelEntry[];
}

interface TaskerState {
  tasks: Task[];
  lists: string[];
  defaultList: string;
  collapsedLists: Set<string>;
  hideCompletedLists: Set<string>;
  relDetails: Record<string, TaskRelDetails>;
  searchQuery: string;
  statusMessage: string;
  filterList: string | null; // null = all lists
  loading: boolean;
}

type Action =
  | { type: 'SET_TASKS'; tasks: Task[] }
  | { type: 'SET_LISTS'; lists: string[] }
  | { type: 'SET_DEFAULT_LIST'; name: string }
  | { type: 'SET_COLLAPSED'; name: string; collapsed: boolean }
  | { type: 'SET_COLLAPSED_MAP'; map: Map<string, boolean> }
  | { type: 'SET_HIDE_COMPLETED'; name: string; hide: boolean }
  | { type: 'SET_HIDE_COMPLETED_MAP'; map: Map<string, boolean> }
  | { type: 'SET_REL_DETAILS'; details: Record<string, TaskRelDetails> }
  | { type: 'SET_SEARCH'; query: string }
  | { type: 'SET_STATUS_MESSAGE'; message: string }
  | { type: 'SET_FILTER_LIST'; list: string | null }
  | { type: 'SET_LOADING'; loading: boolean }
  | { type: 'REORDER_TASKS'; listName: string; oldIndex: number; newIndex: number }
  | { type: 'REORDER_LISTS'; oldIndex: number; newIndex: number }
  | { type: 'UPDATE_TASK_STATUS'; taskId: string; status: TaskStatus };

function reducer(state: TaskerState, action: Action): TaskerState {
  switch (action.type) {
    case 'SET_TASKS':
      return { ...state, tasks: action.tasks };
    case 'SET_LISTS':
      return { ...state, lists: action.lists };
    case 'SET_DEFAULT_LIST':
      return { ...state, defaultList: action.name };
    case 'SET_COLLAPSED': {
      const next = new Set(state.collapsedLists);
      if (action.collapsed) next.add(action.name);
      else next.delete(action.name);
      return { ...state, collapsedLists: next };
    }
    case 'SET_COLLAPSED_MAP': {
      const set = new Set<string>();
      for (const [name, collapsed] of action.map) {
        if (collapsed) set.add(name);
      }
      return { ...state, collapsedLists: set };
    }
    case 'SET_HIDE_COMPLETED': {
      const next = new Set(state.hideCompletedLists);
      if (action.hide) next.add(action.name);
      else next.delete(action.name);
      return { ...state, hideCompletedLists: next };
    }
    case 'SET_HIDE_COMPLETED_MAP': {
      const set = new Set<string>();
      for (const [name, hide] of action.map) {
        if (hide) set.add(name);
      }
      return { ...state, hideCompletedLists: set };
    }
    case 'SET_REL_DETAILS':
      return { ...state, relDetails: action.details };
    case 'SET_SEARCH':
      return { ...state, searchQuery: action.query };
    case 'SET_STATUS_MESSAGE':
      return { ...state, statusMessage: action.message };
    case 'SET_FILTER_LIST':
      return { ...state, filterList: action.list };
    case 'SET_LOADING':
      return { ...state, loading: action.loading };
    case 'REORDER_TASKS': {
      const { listName, oldIndex, newIndex } = action;
      const listTasks = state.tasks.filter((t) => t.listName === listName);
      const otherTasks = state.tasks.filter((t) => t.listName !== listName);
      const reordered = arrayMove(listTasks, oldIndex, newIndex);
      return { ...state, tasks: [...otherTasks, ...reordered] };
    }
    case 'REORDER_LISTS':
      return { ...state, lists: arrayMove(state.lists, action.oldIndex, action.newIndex) };
    case 'UPDATE_TASK_STATUS': {
      const updateEntry = (e: RelEntry): RelEntry =>
        e.id === action.taskId ? { ...e, status: action.status } : e;
      const updatedRel: Record<string, TaskRelDetails> = {};
      for (const [tid, det] of Object.entries(state.relDetails)) {
        updatedRel[tid] = {
          parent: det.parent && det.parent.id === action.taskId
            ? { ...det.parent, status: action.status }
            : det.parent,
          subtasks: det.subtasks.map(updateEntry),
          blocks: det.blocks.map(updateEntry),
          blockedBy: det.blockedBy.map(updateEntry),
          related: det.related.map(updateEntry),
        };
      }
      return {
        ...state,
        tasks: state.tasks.map((t) =>
          t.id === action.taskId ? { ...t, status: action.status } : t,
        ),
        relDetails: updatedRel,
      };
    }
  }
}

const initialState: TaskerState = {
  tasks: [],
  lists: [],
  defaultList: 'tasks',
  collapsedLists: new Set(),
  hideCompletedLists: new Set(),
  relDetails: {},
  searchQuery: '',
  statusMessage: '',
  filterList: null,
  loading: true,
};

export function useTaskerStore() {
  const [state, dispatch] = useReducer(reducer, initialState);
  const statusTimeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const showStatus = useCallback((message: string) => {
    dispatch({ type: 'SET_STATUS_MESSAGE', message });
    if (statusTimeoutRef.current) clearTimeout(statusTimeoutRef.current);
    statusTimeoutRef.current = setTimeout(() => {
      dispatch({ type: 'SET_STATUS_MESSAGE', message: '' });
    }, 3000);
  }, []);

  const refresh = useCallback(async () => {
    try {
      const [lists, defaultList] = await Promise.all([
        listService.getAllLists(),
        listService.getDefaultList(),
      ]);
      dispatch({ type: 'SET_LISTS', lists });
      dispatch({ type: 'SET_DEFAULT_LIST', name: defaultList });

      // Load collapsed states
      const collapsedMap = new Map<string, boolean>();
      await Promise.all(
        lists.map(async (name) => {
          const collapsed = await listService.isListCollapsed(name);
          collapsedMap.set(name, collapsed);
        }),
      );
      dispatch({ type: 'SET_COLLAPSED_MAP', map: collapsedMap });

      // Load hide-completed states
      const hideCompletedMap = new Map<string, boolean>();
      await Promise.all(
        lists.map(async (name) => {
          const hide = await listService.isListHideCompleted(name);
          hideCompletedMap.set(name, hide);
        }),
      );
      dispatch({ type: 'SET_HIDE_COMPLETED_MAP', map: hideCompletedMap });

      // Load tasks
      const tasks = state.searchQuery
        ? await taskService.searchTasks(state.searchQuery)
        : await taskService.getAllTasks(state.filterList ?? undefined);
      dispatch({ type: 'SET_TASKS', tasks });

      // Build relationship details by parsing descriptions + batch-fetching titles
      if (tasks.length > 0) {
        // Parse each task's description to extract relationship IDs
        const referencedIds = new Set<string>();
        const parsedMap = new Map<string, ReturnType<typeof parseTaskDescription>>();
        for (const t of tasks) {
          const parsed = parseTaskDescription(t.description);
          parsedMap.set(t.id, parsed);
          if (parsed.parentId) referencedIds.add(parsed.parentId);
          for (const id of parsed.hasSubtaskIds ?? []) referencedIds.add(id);
          for (const id of parsed.blocksIds ?? []) referencedIds.add(id);
          for (const id of parsed.blockedByIds ?? []) referencedIds.add(id);
          for (const id of parsed.relatedIds ?? []) referencedIds.add(id);
        }

        // Batch-fetch titles + statuses for all referenced task IDs
        const uniqueIds = [...referencedIds];
        const summaries = uniqueIds.length > 0
          ? await taskService.getTaskTitles(uniqueIds)
          : {};

        // Build details map
        const details: Record<string, TaskRelDetails> = {};
        const toEntry = (id: string): RelEntry => ({
          id,
          title: summaries[id]?.title ?? '?',
          status: summaries[id]?.status ?? 0,
        });
        for (const t of tasks) {
          const parsed = parsedMap.get(t.id)!;
          details[t.id] = {
            parent: parsed.parentId ? toEntry(parsed.parentId) : null,
            subtasks: (parsed.hasSubtaskIds ?? []).map(toEntry),
            blocks: (parsed.blocksIds ?? []).map(toEntry),
            blockedBy: (parsed.blockedByIds ?? []).map(toEntry),
            related: (parsed.relatedIds ?? []).map(toEntry),
          };
        }
        dispatch({ type: 'SET_REL_DETAILS', details });
      } else {
        dispatch({ type: 'SET_REL_DETAILS', details: {} });
      }

      // Reload undo history
      await undoService.reloadUndoHistory();
    } catch (err) {
      showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      dispatch({ type: 'SET_LOADING', loading: false });
    }
  }, [state.searchQuery, state.filterList, showStatus]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  // Listen for external database changes (file watcher)
  useEffect(() => {
    const unsubscribe = window.ipc.onDbChanged(() => {
      refresh();
    });
    return unsubscribe;
  }, [refresh]);

  // Refresh when popup is shown (pick up external changes) or hidden (re-sort ready for next open)
  useEffect(() => {
    const unsubShown = window.ipc.onPopupShown(() => {
      refresh();
    });
    const unsubHidden = window.ipc.onPopupHidden(() => {
      refresh();
    });
    return () => {
      unsubShown();
      unsubHidden();
    };
  }, [refresh]);

  // Task operations
  const addTask = useCallback(
    async (description: string, listName: string) => {
      try {
        const result = await taskService.addTask(description, listName);
        showStatus(`Added: ${result.task.id.slice(0, 3)}`);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const toggleStatus = useCallback(
    async (taskId: string, currentStatus: TaskStatus) => {
      const newStatus = currentStatus === TS.Done ? TS.Pending : TS.Done;
      dispatch({ type: 'UPDATE_TASK_STATUS', taskId, status: newStatus });
      try {
        await taskService.setTaskStatus(taskId, newStatus);
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
        await refresh();
      }
    },
    [refresh, showStatus],
  );

  const setStatusTo = useCallback(
    async (taskId: string, status: TaskStatus) => {
      dispatch({ type: 'UPDATE_TASK_STATUS', taskId, status });
      try {
        await taskService.setTaskStatus(taskId, status);
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
        await refresh();
      }
    },
    [refresh, showStatus],
  );

  const rename = useCallback(
    async (taskId: string, newDescription: string) => {
      try {
        await taskService.renameTask(taskId, newDescription);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const deleteTaskAction = useCallback(
    async (taskId: string) => {
      try {
        await taskService.deleteTask(taskId);
        showStatus('Deleted');
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const moveTaskAction = useCallback(
    async (taskId: string, targetList: string) => {
      try {
        await taskService.moveTask(taskId, targetList);
        showStatus(`Moved to ${targetList}`);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const reorderTaskAction = useCallback(
    async (taskId: string, newIndex: number, listName: string, oldIndex: number) => {
      dispatch({ type: 'REORDER_TASKS', listName, oldIndex, newIndex });
      try {
        await taskService.reorderTask(taskId, newIndex);
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
        await refresh();
      }
    },
    [refresh, showStatus],
  );

  // List operations
  const createListAction = useCallback(
    async (name: string) => {
      try {
        await listService.createList(name);
        showStatus(`Created list "${name}"`);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const deleteListAction = useCallback(
    async (name: string) => {
      try {
        await listService.deleteList(name);
        showStatus(`Deleted list "${name}"`);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const renameListAction = useCallback(
    async (oldName: string, newName: string) => {
      try {
        await listService.renameList(oldName, newName);
        showStatus(`Renamed "${oldName}" to "${newName}"`);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const reorderListAction = useCallback(
    async (name: string, newIndex: number, oldIndex: number) => {
      dispatch({ type: 'REORDER_LISTS', oldIndex, newIndex });
      try {
        await listService.reorderList(name, newIndex);
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
        await refresh();
      }
    },
    [refresh, showStatus],
  );

  const toggleCollapsed = useCallback(
    async (name: string) => {
      const collapsed = !state.collapsedLists.has(name);
      dispatch({ type: 'SET_COLLAPSED', name, collapsed });
      await listService.setListCollapsed(name, collapsed);
    },
    [state.collapsedLists],
  );

  const toggleHideCompleted = useCallback(
    async (name: string) => {
      const hide = !state.hideCompletedLists.has(name);
      dispatch({ type: 'SET_HIDE_COMPLETED', name, hide });
      await listService.setListHideCompleted(name, hide);
    },
    [state.hideCompletedLists],
  );

  const toggleCollapseAll = useCallback(async () => {
    const allCollapsed = state.lists.every((name) => state.collapsedLists.has(name));
    const target = !allCollapsed;
    const map = new Map<string, boolean>();
    for (const name of state.lists) {
      map.set(name, target);
      await listService.setListCollapsed(name, target);
    }
    dispatch({ type: 'SET_COLLAPSED_MAP', map });
  }, [state.lists, state.collapsedLists]);

  // Undo/redo
  const undoAction = useCallback(async () => {
    try {
      const desc = await undoService.undo();
      if (desc) showStatus(`Undone: ${desc}`);
      else showStatus('Nothing to undo');
      await refresh();
    } catch (err) {
      showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [refresh, showStatus]);

  const redoAction = useCallback(async () => {
    try {
      const desc = await undoService.redo();
      if (desc) showStatus(`Redone: ${desc}`);
      else showStatus('Nothing to redo');
      await refresh();
    } catch (err) {
      showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [refresh, showStatus]);

  // Search
  const setSearch = useCallback((query: string) => {
    dispatch({ type: 'SET_SEARCH', query });
  }, []);

  const setFilterList = useCallback((list: string | null) => {
    dispatch({ type: 'SET_FILTER_LIST', list });
  }, []);

  // Apply system sort (one-shot)
  const applySystemSortAction = useCallback(async () => {
    try {
      const count = await taskService.applySystemSort(state.filterList ?? undefined);
      showStatus(`Sorted ${count} list${count !== 1 ? 's' : ''}`);
      await refresh();
    } catch (err) {
      showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [state.filterList, refresh, showStatus]);

  // Navigate to a linked task: expand its list if collapsed, scroll into view, highlight
  const navigateToTask = useCallback(
    async (taskId: string) => {
      const task = state.tasks.find((t) => t.id === taskId);
      if (!task) {
        showStatus(`Task ${taskId} not found`);
        return;
      }

      // Expand the list if collapsed
      if (state.collapsedLists.has(task.listName)) {
        dispatch({ type: 'SET_COLLAPSED', name: task.listName, collapsed: false });
        await listService.setListCollapsed(task.listName, false);
      }

      // Un-hide completed tasks if the target task is done and hidden
      if (task.status === TS.Done && state.hideCompletedLists.has(task.listName)) {
        dispatch({ type: 'SET_HIDE_COMPLETED', name: task.listName, hide: false });
        await listService.setListHideCompleted(task.listName, false);
      }

      // Wait a frame for DOM to update after expanding
      requestAnimationFrame(() => {
        const el = document.querySelector(`[data-task-id="${taskId}"]`);
        if (el) {
          el.scrollIntoView({ behavior: 'smooth', block: 'center' });
          el.classList.remove('task-highlight');
          // Force reflow to restart animation
          void (el as HTMLElement).offsetWidth;
          el.classList.add('task-highlight');
        }
      });
    },
    [state.tasks, state.collapsedLists, state.hideCompletedLists, showStatus],
  );

  // Group tasks by list
  const tasksByList = state.lists.reduce<Record<string, Task[]>>((acc, listName) => {
    acc[listName] = state.tasks.filter((t) => t.listName === listName);
    return acc;
  }, {});

  // Stats
  const pendingCount = state.tasks.filter((t) => t.status === 0).length;
  const inProgressCount = state.tasks.filter((t) => t.status === 1).length;
  const totalCount = state.tasks.length;

  return {
    ...state,
    tasksByList,
    relDetails: state.relDetails,
    pendingCount,
    inProgressCount,
    totalCount,
    refresh,
    addTask,
    toggleStatus,
    setStatusTo,
    rename,
    deleteTask: deleteTaskAction,
    moveTask: moveTaskAction,
    reorderTask: reorderTaskAction,
    createList: createListAction,
    deleteList: deleteListAction,
    renameList: renameListAction,
    reorderList: reorderListAction,
    toggleCollapsed,
    toggleHideCompleted,
    toggleCollapseAll,
    undo: undoAction,
    redo: redoAction,
    setSearch,
    setFilterList,
    applySystemSort: applySystemSortAction,
    navigateToTask,
    showStatus,
  };
}
