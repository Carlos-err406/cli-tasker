import { useEffect } from 'react';
import { hideWindow } from '@/lib/services/window.js';

interface ShortcutHandlers {
  onUndo: () => void;
  onRedo: () => void;
  onRefresh: () => void;
  onFocusSearch: () => void;
  onToggleHelp: () => void;
  onToggleLogs: () => void;
  onApplySort: () => void;
  onToggleCollapseAll: () => void;
  onEscape: () => void;
}

export function useKeyboardShortcuts(handlers: ShortcutHandlers) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const meta = e.metaKey || e.ctrlKey;

      // Cmd+K - focus search
      if (meta && e.key === 'k') {
        e.preventDefault();
        handlers.onFocusSearch();
        return;
      }

      // Cmd+R - refresh
      if (meta && e.key === 'r') {
        e.preventDefault();
        handlers.onRefresh();
        return;
      }

      // Cmd+Z - undo, Cmd+Shift+Z - redo
      if (meta && e.key === 'z') {
        e.preventDefault();
        if (e.shiftKey) handlers.onRedo();
        else handlers.onUndo();
        return;
      }

      // Cmd+W - close popup
      if (meta && e.key === 'w') {
        e.preventDefault();
        hideWindow();
        return;
      }

      // Cmd+J - apply system sort
      if (meta && e.key === 'j') {
        e.preventDefault();
        handlers.onApplySort();
        return;
      }

      // Cmd+L - toggle logs
      if (meta && e.key === 'l') {
        e.preventDefault();
        handlers.onToggleLogs();
        return;
      }

      // Cmd+E - collapse/expand all lists
      if (meta && e.key === 'e') {
        e.preventDefault();
        handlers.onToggleCollapseAll();
        return;
      }

      // Cmd+? or Cmd+/ - toggle help
      if (meta && (e.key === '/' || e.key === '?')) {
        e.preventDefault();
        handlers.onToggleHelp();
        return;
      }

      // Escape - context-dependent (close help, cancel edit, or close window)
      // Skip if the event originated from an input/textarea — let the component handle it
      if (e.key === 'Escape') {
        const target = e.target as HTMLElement;
        if (target?.tagName === 'INPUT' || target?.tagName === 'TEXTAREA') {
          return;
        }
        handlers.onEscape();
        return;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handlers]);
}
