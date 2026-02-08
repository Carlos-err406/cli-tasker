import { useCallback, useEffect, useReducer, useRef } from 'react';
import type { Task, TaskStatus } from '@tasker/core';
import * as taskService from '@/lib/services/tasks.js';
import * as listService from '@/lib/services/lists.js';
import * as undoService from '@/lib/services/undo.js';

interface TaskerState {
  tasks: Task[];
  lists: string[];
  defaultList: string;
  collapsedLists: Set<string>;
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
  | { type: 'SET_SEARCH'; query: string }
  | { type: 'SET_STATUS_MESSAGE'; message: string }
  | { type: 'SET_FILTER_LIST'; list: string | null }
  | { type: 'SET_LOADING'; loading: boolean };

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
    case 'SET_SEARCH':
      return { ...state, searchQuery: action.query };
    case 'SET_STATUS_MESSAGE':
      return { ...state, statusMessage: action.message };
    case 'SET_FILTER_LIST':
      return { ...state, filterList: action.list };
    case 'SET_LOADING':
      return { ...state, loading: action.loading };
  }
}

const initialState: TaskerState = {
  tasks: [],
  lists: [],
  defaultList: 'tasks',
  collapsedLists: new Set(),
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

      // Load tasks
      const tasks = state.searchQuery
        ? await taskService.searchTasks(state.searchQuery)
        : await taskService.getAllTasks(state.filterList ?? undefined);
      dispatch({ type: 'SET_TASKS', tasks });

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
      const { TaskStatus: TS } = await import('@tasker/core');
      const newStatus = currentStatus === TS.Done ? TS.Pending : TS.Done;
      try {
        await taskService.setTaskStatus(taskId, newStatus);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
    },
    [refresh, showStatus],
  );

  const setStatusTo = useCallback(
    async (taskId: string, status: TaskStatus) => {
      try {
        await taskService.setTaskStatus(taskId, status);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
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
    async (taskId: string, newIndex: number) => {
      try {
        await taskService.reorderTask(taskId, newIndex);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
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
    async (name: string, newIndex: number) => {
      try {
        await listService.reorderList(name, newIndex);
        await refresh();
      } catch (err) {
        showStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
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
    undo: undoAction,
    redo: redoAction,
    setSearch,
    setFilterList,
    showStatus,
  };
}
