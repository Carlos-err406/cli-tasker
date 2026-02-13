import { useState, useRef, useCallback, useMemo } from 'react';
import type { Task, TaskStatus } from '@tasker/core/types';
import type { TaskRelDetails } from '@/hooks/use-tasker-store.js';
import { useClickOutside } from '@/hooks/use-click-outside.js';
import {
  SortableContext,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { ChevronDown, Plus, Ellipsis } from 'lucide-react';
import { SortableTaskItem } from './SortableTaskItem.js';

interface ListSectionProps {
  listName: string;
  tasks: Task[];
  lists: string[];
  relDetails: Record<string, TaskRelDetails>;
  isDefault: boolean;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  onAddTask: (description: string, listName: string) => void;
  onToggleStatus: (taskId: string, currentStatus: TaskStatus) => void;
  onSetStatus: (taskId: string, status: TaskStatus) => void;
  onRename: (taskId: string, newDescription: string) => void;
  onDelete: (taskId: string) => void;
  onMove: (taskId: string, targetList: string) => void;
  onRenameList: (oldName: string, newName: string) => void;
  onDeleteList: (name: string) => void;
  onShowStatus: (message: string) => void;
  onNavigateToTask: (taskId: string) => void;
}

export function ListSection({
  listName,
  tasks,
  lists,
  relDetails,
  isDefault,
  collapsed,
  onToggleCollapsed,
  onAddTask,
  onToggleStatus,
  onSetStatus,
  onRename,
  onDelete,
  onMove,
  onRenameList,
  onDeleteList,
  onShowStatus,
  onNavigateToTask,
}: ListSectionProps) {
  const [adding, setAdding] = useState(false);
  const [addValue, setAddValue] = useState('');
  const [editingName, setEditingName] = useState(false);
  const [nameValue, setNameValue] = useState('');
  const [showListMenu, setShowListMenu] = useState(false);
  const addInputRef = useRef<HTMLTextAreaElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const listMenuRef = useRef<HTMLDivElement>(null);

  useClickOutside(listMenuRef, useCallback(() => setShowListMenu(false), []));

  const taskIds = useMemo(() => tasks.map((t) => t.id), [tasks]);

  const pendingCount = tasks.filter((t) => t.status === 0).length;
  const totalCount = tasks.length;
  const summary = pendingCount < totalCount
    ? `${totalCount} task${totalCount !== 1 ? 's' : ''}, ${pendingCount} pending`
    : `${totalCount} task${totalCount !== 1 ? 's' : ''}`;

  const startAdd = (initialValue?: string) => {
    setAdding(true);
    setAddValue(initialValue ?? '');
    // Expand if collapsed
    if (collapsed) onToggleCollapsed();
    setTimeout(() => {
      const el = addInputRef.current;
      if (el) {
        el.focus();
        if (initialValue) {
          el.selectionStart = el.selectionEnd = 0;
        }
      }
    }, 0);
  };

  const submitAdd = () => {
    const trimmed = addValue.trim();
    if (trimmed) {
      onAddTask(trimmed, listName);
    }
    setAdding(false);
    setAddValue('');
  };

  const handleAddKeyDown = (e: React.KeyboardEvent) => {
    // Stop propagation so dnd-kit keyboard listeners don't intercept (e.g. Space)
    e.stopPropagation();
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      submitAdd();
    }
    if (e.key === 'Escape') {
      setAdding(false);
    }
  };

  const startEditName = () => {
    setNameValue(listName);
    setEditingName(true);
    setShowListMenu(false);
    setTimeout(() => nameInputRef.current?.focus(), 0);
  };

  const submitNameEdit = () => {
    const trimmed = nameValue.trim();
    if (trimmed && trimmed !== listName) {
      onRenameList(listName, trimmed);
    }
    setEditingName(false);
  };

  return (
    <div className="border-b border-border/50">
      {/* List header */}
      <div className="group/header sticky top-0 z-10 flex items-center gap-2 px-3 py-2 bg-secondary/80 backdrop-blur-sm hover:bg-secondary/90 transition-colors">
        <button
          onClick={onToggleCollapsed}
          className="text-muted-foreground hover:text-foreground transition-transform flex-shrink-0"
          style={{ transform: collapsed ? 'rotate(-90deg)' : 'rotate(0deg)' }}
        >
          <ChevronDown className="h-3.5 w-3.5" />
        </button>

        <div className="flex-1 min-w-0">
          {editingName ? (
            <input
              ref={nameInputRef}
              value={nameValue}
              onChange={(e) => setNameValue(e.target.value)}
              onKeyDown={(e) => {
                e.stopPropagation();
                if (e.key === 'Enter') submitNameEdit();
                if (e.key === 'Escape') setEditingName(false);
              }}
              onBlur={submitNameEdit}
              className="bg-background border border-border rounded px-1 py-0 text-sm w-full"
            />
          ) : (
            <div className="flex items-baseline gap-2">
              <span className="text-sm font-semibold">{listName}</span>
              <span className="text-[10px] text-muted-foreground">{summary}</span>
            </div>
          )}
        </div>

        <div className="flex items-center gap-1 flex-shrink-0">
          <button
            onClick={() => startAdd()}
            className="text-muted-foreground hover:text-foreground p-0.5"
            title="Add task"
          >
            <Plus className="h-4 w-4" />
          </button>

          {!isDefault && (
            <div ref={listMenuRef} className="relative">
              <button
                onClick={() => setShowListMenu(!showListMenu)}
                className="text-muted-foreground hover:text-foreground p-0.5"
              >
              <Ellipsis className="h-4 w-4" />
            </button>
            {showListMenu && (
              <div className="absolute right-0 top-6 z-50 min-w-[120px] bg-popover border border-border rounded-md shadow-lg py-1 text-sm">
                <button
                  onClick={startEditName}
                  className="w-full text-left px-3 py-1.5 hover:bg-accent"
                >
                  Rename
                </button>
                <button
                  onClick={() => {
                    onDeleteList(listName);
                    setShowListMenu(false);
                  }}
                  className="w-full text-left px-3 py-1.5 hover:bg-accent text-red-400"
                >
                  Delete
                </button>
              </div>
            )}
          </div>
          )}
        </div>
      </div>

      {/* Tasks */}
      {!collapsed && (
        <div>
          {adding && (
            <div className="px-3 py-2 border-b border-border/30">
              <textarea
                ref={addInputRef}
                value={addValue}
                onChange={(e) => setAddValue(e.target.value)}
                onKeyDown={handleAddKeyDown}
                onBlur={() => {
                  if (addValue.trim()) submitAdd();
                  else setAdding(false);
                }}
                placeholder="New task... (Cmd+Enter to submit)"
                className="w-full bg-background border border-border rounded px-2 py-1 text-sm resize-none placeholder:text-muted-foreground/50"
                rows={2}
              />
            </div>
          )}

          {tasks.length === 0 && !adding && (
            <div className="px-3 py-3 text-xs text-muted-foreground/50 text-center">
              No tasks
            </div>
          )}

          <SortableContext items={taskIds} strategy={verticalListSortingStrategy}>
            {tasks.map((task) => (
              <SortableTaskItem
                key={task.id}
                task={task}
                lists={lists}
                relDetails={relDetails[task.id]}
                onToggleStatus={onToggleStatus}
                onSetStatus={onSetStatus}
                onRename={onRename}
                onDelete={onDelete}
                onMove={onMove}
                onShowStatus={onShowStatus}
                onNavigateToTask={onNavigateToTask}
                onCreateSubtask={(taskId) => startAdd(`\n^${taskId}`)}
              />
            ))}
          </SortableContext>
        </div>
      )}
    </div>
  );
}
