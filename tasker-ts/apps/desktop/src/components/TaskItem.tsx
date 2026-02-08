import { useState, useRef } from 'react';
import type { Task, TaskStatus } from '@tasker/core';
import { TaskStatus as TS } from '@tasker/core';
import { cn } from '@/lib/utils.js';
import {
  getDisplayTitle,
  getShortId,
  isDone,
  isInProgress,
  getPriorityIndicator,
  getPriorityColor,
  getDueDateColor,
  formatDueDate,
  getTagColor,
} from '@/lib/task-display.js';

interface TaskItemProps {
  task: Task;
  lists: string[];
  onToggleStatus: (taskId: string, currentStatus: TaskStatus) => void;
  onSetStatus: (taskId: string, status: TaskStatus) => void;
  onRename: (taskId: string, newDescription: string) => void;
  onDelete: (taskId: string) => void;
  onMove: (taskId: string, targetList: string) => void;
}

export function TaskItem({
  task,
  lists,
  onToggleStatus,
  onSetStatus,
  onRename,
  onDelete,
  onMove,
}: TaskItemProps) {
  const [editing, setEditing] = useState(false);
  const [editValue, setEditValue] = useState('');
  const [showMenu, setShowMenu] = useState(false);
  const [showMoveMenu, setShowMoveMenu] = useState(false);
  const [showStatusMenu, setShowStatusMenu] = useState(false);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const done = isDone(task);
  const inProg = isInProgress(task);
  const title = getDisplayTitle(task);
  const shortId = getShortId(task);
  const priorityIndicator = getPriorityIndicator(task.priority);
  const priorityColor = getPriorityColor(task.priority);
  const dueDateLabel = formatDueDate(task.dueDate);
  const dueDateColor = getDueDateColor(task.dueDate);

  const startEdit = () => {
    setEditValue(task.description);
    setEditing(true);
    setShowMenu(false);
    setTimeout(() => inputRef.current?.focus(), 0);
  };

  const submitEdit = () => {
    const trimmed = editValue.trim();
    if (trimmed && trimmed !== task.description) {
      onRename(task.id, trimmed);
    }
    setEditing(false);
  };

  const handleEditKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      submitEdit();
    }
    if (e.key === 'Escape') {
      setEditing(false);
    }
  };

  const handleCheckboxClick = () => {
    onToggleStatus(task.id, task.status);
  };

  const handleCheckboxContextMenu = (e: React.MouseEvent) => {
    e.preventDefault();
    onSetStatus(task.id, TS.InProgress);
  };

  const copyId = () => {
    navigator.clipboard.writeText(shortId);
  };

  return (
    <div
      className={cn(
        'group flex items-start gap-2 px-3 py-2 transition-colors hover:bg-accent/50',
        done && 'opacity-60',
      )}
    >
      {/* Checkbox + ID column */}
      <div className="flex flex-col items-center pt-0.5">
        <button
          onClick={handleCheckboxClick}
          onContextMenu={handleCheckboxContextMenu}
          className={cn(
            'h-4 w-4 rounded border transition-colors flex items-center justify-center',
            done
              ? 'border-green-500 bg-green-500/20 text-green-400'
              : inProg
                ? 'border-amber-400 bg-amber-400/20 text-amber-400'
                : 'border-muted-foreground/40 hover:border-foreground/60',
          )}
        >
          {done && <span className="text-[10px]">&#10003;</span>}
          {inProg && <span className="text-[10px]">&#9679;</span>}
        </button>
        <button
          onClick={copyId}
          className="mt-0.5 font-mono text-[9px] text-muted-foreground/50 hover:text-muted-foreground"
          title="Copy ID"
        >
          {shortId}
        </button>
      </div>

      {/* Content column */}
      <div className="flex-1 min-w-0">
        {editing ? (
          <textarea
            ref={inputRef}
            value={editValue}
            onChange={(e) => setEditValue(e.target.value)}
            onKeyDown={handleEditKeyDown}
            onBlur={submitEdit}
            className="w-full bg-background border border-border rounded px-2 py-1 text-sm resize-none"
            rows={2}
          />
        ) : (
          <>
            <div className="flex items-center gap-1.5">
              {priorityIndicator && (
                <span className={cn('text-xs font-bold', priorityColor)}>
                  {priorityIndicator}
                </span>
              )}
              <span
                className={cn(
                  'text-sm leading-tight',
                  done && 'line-through text-muted-foreground',
                )}
              >
                {title}
              </span>
            </div>

            {/* Metadata row */}
            <div className="flex flex-wrap items-center gap-1.5 mt-0.5">
              {dueDateLabel && (
                <span className={cn('text-[10px]', dueDateColor)}>
                  @{dueDateLabel}
                </span>
              )}
              {task.tags?.map((tag) => (
                <span
                  key={tag}
                  className={cn(
                    'text-[10px] px-1.5 py-0 rounded-full',
                    getTagColor(tag),
                  )}
                >
                  #{tag}
                </span>
              ))}
            </div>
          </>
        )}
      </div>

      {/* Menu button */}
      <div className="relative">
        <button
          onClick={() => {
            setShowMenu(!showMenu);
            setShowMoveMenu(false);
            setShowStatusMenu(false);
          }}
          className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-foreground text-sm px-1 transition-opacity"
        >
          &hellip;
        </button>

        {showMenu && (
          <div
            ref={menuRef}
            className="absolute right-0 top-6 z-50 min-w-[140px] bg-popover border border-border rounded-md shadow-lg py-1 text-sm"
          >
            <button
              onClick={startEdit}
              className="w-full text-left px-3 py-1.5 hover:bg-accent"
            >
              Edit
            </button>
            <button
              onClick={() => setShowMoveMenu(!showMoveMenu)}
              className="w-full text-left px-3 py-1.5 hover:bg-accent"
            >
              Move to...
            </button>
            {showMoveMenu && (
              <div className="border-t border-border">
                {lists
                  .filter((l) => l !== task.listName)
                  .map((l) => (
                    <button
                      key={l}
                      onClick={() => {
                        onMove(task.id, l);
                        setShowMenu(false);
                        setShowMoveMenu(false);
                      }}
                      className="w-full text-left px-5 py-1 hover:bg-accent text-xs"
                    >
                      {l}
                    </button>
                  ))}
              </div>
            )}
            <button
              onClick={() => setShowStatusMenu(!showStatusMenu)}
              className="w-full text-left px-3 py-1.5 hover:bg-accent"
            >
              Set Status
            </button>
            {showStatusMenu && (
              <div className="border-t border-border">
                {[
                  { label: 'Pending', status: TS.Pending },
                  { label: 'In Progress', status: TS.InProgress },
                  { label: 'Done', status: TS.Done },
                ].map(({ label, status }) => (
                  <button
                    key={label}
                    onClick={() => {
                      onSetStatus(task.id, status);
                      setShowMenu(false);
                      setShowStatusMenu(false);
                    }}
                    className={cn(
                      'w-full text-left px-5 py-1 hover:bg-accent text-xs',
                      task.status === status && 'text-primary font-medium',
                    )}
                  >
                    {task.status === status && '~ '}
                    {label}
                  </button>
                ))}
              </div>
            )}
            <div className="border-t border-border mt-1 pt-1">
              <button
                onClick={() => {
                  onDelete(task.id);
                  setShowMenu(false);
                }}
                className="w-full text-left px-3 py-1.5 hover:bg-accent text-red-400"
              >
                Delete
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
