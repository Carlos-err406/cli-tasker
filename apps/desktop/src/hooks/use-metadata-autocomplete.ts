import { useState, useCallback, useRef, useEffect } from 'react';
import type { Task } from '@tasker/core/types';
import * as taskService from '@/lib/services/tasks.js';
import { getDisplayTitle, getShortId } from '@/lib/task-display.js';

export interface Suggestion {
  task: Task;
  shortId: string;
  title: string;
}

interface AutocompleteState {
  isOpen: boolean;
  suggestions: Suggestion[];
  selectedIndex: number;
  prefix: string;
  partial: string;
  /** Character index where the prefix starts in the textarea value */
  matchStart: number;
}

const CLOSED: AutocompleteState = {
  isOpen: false,
  suggestions: [],
  selectedIndex: 0,
  prefix: '',
  partial: '',
  matchStart: 0,
};

/** Regex to detect a metadata relationship prefix at cursor position.
 *  Matches: ^, !, ~, -^, -!  followed by optional partial ID/query chars */
const PREFIX_RE = /(?:^|\s)(-[!^]|[!^~])(\w*)$/;

export function useMetadataAutocomplete(
  value: string,
  textareaRef: React.RefObject<HTMLTextAreaElement | null>,
  excludeTaskId?: string,
) {
  const [state, setState] = useState<AutocompleteState>(CLOSED);
  const allTasksRef = useRef<Task[] | null>(null);
  const fetchingRef = useRef(false);
  /** Suppresses the next detect() call after a selection (the inserted ID still matches the prefix regex) */
  const justSelectedRef = useRef(false);

  const fetchTasks = useCallback(async () => {
    if (allTasksRef.current || fetchingRef.current) return allTasksRef.current;
    fetchingRef.current = true;
    try {
      const tasks = await taskService.getAllTasks();
      allTasksRef.current = tasks;
      return tasks;
    } finally {
      fetchingRef.current = false;
    }
  }, []);

  /** Call this on every value/cursor change to detect prefix */
  const detect = useCallback(
    async (cursorPos?: number) => {
      if (justSelectedRef.current) {
        justSelectedRef.current = false;
        return;
      }
      const el = textareaRef.current;
      if (!el) return;
      const pos = cursorPos ?? el.selectionStart;
      const textBeforeCursor = value.slice(0, pos);

      // Check the current line only (from last newline to cursor)
      const lineStart = textBeforeCursor.lastIndexOf('\n') + 1;
      const lineText = textBeforeCursor.slice(lineStart);
      const match = PREFIX_RE.exec(lineText);

      if (!match) {
        if (state.isOpen) setState(CLOSED);
        return;
      }

      const prefix = match[1]!;
      const partial = match[2]!;
      // matchStart is the absolute index in value where the prefix begins
      const matchStart = lineStart + match.index + (match[0].startsWith(' ') ? 1 : 0);

      // Fetch tasks if needed
      let tasks = allTasksRef.current;
      if (!tasks) {
        tasks = await fetchTasks();
        if (!tasks) return;
      }

      // Filter
      const lowerPartial = partial.toLowerCase();
      const filtered: Suggestion[] = [];
      for (const t of tasks) {
        if (excludeTaskId && t.id === excludeTaskId) continue;
        const sid = getShortId(t);
        const title = getDisplayTitle(t);
        if (!partial || sid.toLowerCase().startsWith(lowerPartial) || title.toLowerCase().includes(lowerPartial)) {
          filtered.push({ task: t, shortId: sid, title });
        }
        if (filtered.length >= 50) break;
      }

      setState({
        isOpen: filtered.length > 0,
        suggestions: filtered,
        selectedIndex: 0,
        prefix,
        partial,
        matchStart,
      });
    },
    [value, textareaRef, excludeTaskId, state.isOpen, fetchTasks],
  );

  // Reset task cache when autocomplete closes so next open gets fresh data
  useEffect(() => {
    if (!state.isOpen) {
      allTasksRef.current = null;
    }
  }, [state.isOpen]);

  /** Insert the selected task ID into the value. Returns the new value string. */
  const select = useCallback(
    (index: number): string | null => {
      if (!state.isOpen || index < 0 || index >= state.suggestions.length) return null;
      const suggestion = state.suggestions[index]!;
      const replaceEnd = state.matchStart + state.prefix.length + state.partial.length;
      const insertion = state.prefix + suggestion.shortId;
      const newValue = value.slice(0, state.matchStart) + insertion + value.slice(replaceEnd);
      justSelectedRef.current = true;
      setState(CLOSED);
      // Set cursor position after insertion
      const cursorPos = state.matchStart + insertion.length;
      setTimeout(() => {
        const el = textareaRef.current;
        if (el) {
          el.selectionStart = el.selectionEnd = cursorPos;
        }
      }, 0);
      return newValue;
    },
    [state, value, textareaRef],
  );

  /** Keyboard handler — returns true if the event was consumed by autocomplete */
  const onKeyDown = useCallback(
    (e: React.KeyboardEvent): boolean => {
      if (!state.isOpen) return false;

      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setState((s) => ({
          ...s,
          selectedIndex: Math.min(s.selectedIndex + 1, s.suggestions.length - 1),
        }));
        return true;
      }

      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setState((s) => ({
          ...s,
          selectedIndex: Math.max(s.selectedIndex - 1, 0),
        }));
        return true;
      }

      if (e.key === 'Enter' && !e.metaKey && !e.ctrlKey) {
        e.preventDefault();
        return true; // caller should call select(state.selectedIndex)
      }

      if (e.key === 'Escape') {
        e.preventDefault();
        setState(CLOSED);
        return true;
      }

      if (e.key === 'Tab') {
        e.preventDefault();
        return true; // caller should call select(state.selectedIndex)
      }

      return false;
    },
    [state.isOpen],
  );

  const dismiss = useCallback(() => setState(CLOSED), []);

  return {
    isOpen: state.isOpen,
    suggestions: state.suggestions,
    selectedIndex: state.selectedIndex,
    detect,
    select,
    onKeyDown,
    dismiss,
  };
}
