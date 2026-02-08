import { useEffect } from 'react';
import { hideWindow } from '@/lib/services/window.js';

interface ShortcutHandlers {
  onUndo: () => void;
  onRedo: () => void;
  onRefresh: () => void;
  onFocusSearch: () => void;
  onToggleHelp: () => void;
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

      // Cmd+? or Cmd+/ - toggle help
      if (meta && (e.key === '/' || e.key === '?')) {
        e.preventDefault();
        handlers.onToggleHelp();
        return;
      }

      // Escape - close
      if (e.key === 'Escape') {
        hideWindow();
        return;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handlers]);
}
