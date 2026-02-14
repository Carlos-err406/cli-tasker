import { useState, useRef, useCallback } from 'react';
import type { Task, TaskStatus } from '@tasker/core/types';
import { TaskStatus as TS } from '@tasker/core/types';
import type { TaskRelDetails } from '@/hooks/use-tasker-store.js';
import { cn } from '@/lib/utils.js';
import { useClickOutside } from '@/hooks/use-click-outside.js';
import { Check, Minus, Ellipsis, CornerLeftUp, CornerRightDown, Ban, Link2, Calendar, Tag } from 'lucide-react';
import { MarkdownContent } from '@/components/MarkdownContent.js';
import {
  getDisplayTitle,
  getDescriptionPreview,
  getShortId,
  isDone,
  isInProgress,
  getPriorityIndicator,
  getPriorityColor,
  getDueDateColor,
  formatDueDate,
  getTagColor,
  getLinkedStatusLabel,
  getLinkedStatusColor,
} from '@/lib/task-display.js';

interface TaskItemProps {
  task: Task;
  lists: string[];
  relDetails?: TaskRelDetails;
  onToggleStatus: (taskId: string, currentStatus: TaskStatus) => void;
  onSetStatus: (taskId: string, status: TaskStatus) => void;
  onRename: (taskId: string, newDescription: string) => void;
  onDelete: (taskId: string) => void;
  onMove: (taskId: string, targetList: string) => void;
  onShowStatus: (message: string) => void;
  onNavigateToTask: (taskId: string) => void;
  onCreateSubtask: (taskId: string) => void;
  onTagClick?: (tag: string) => void;
}

export function TaskItem({
  task,
  lists,
  relDetails,
  onToggleStatus,
  onSetStatus,
  onRename,
  onDelete,
  onMove,
  onShowStatus,
  onNavigateToTask,
  onCreateSubtask,
  onTagClick,
}: TaskItemProps) {
  const [editing, setEditing] = useState(false);
  const [editValue, setEditValue] = useState('');
  const [showMenu, setShowMenu] = useState(false);
  const [showMoveMenu, setShowMoveMenu] = useState(false);
  const [showStatusMenu, setShowStatusMenu] = useState(false);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const closeMenus = useCallback(() => {
    setShowMenu(false);
    setShowMoveMenu(false);
    setShowStatusMenu(false);
  }, []);
  useClickOutside(menuRef, closeMenus);

  const done = isDone(task);
  const inProg = isInProgress(task);
  const title = getDisplayTitle(task);
  const descPreview = getDescriptionPreview(task);
  const shortId = getShortId(task);
  const priorityIndicator = getPriorityIndicator(task.priority);
  const priorityColor = getPriorityColor(task.priority);
  const dueDateLabel = formatDueDate(task.dueDate);
  const dueDateColor = getDueDateColor(task.dueDate);

  const startEdit = () => {
    setEditValue(task.description);
    setEditing(true);
    setShowMenu(false);
    setTimeout(() => {
      const el = inputRef.current;
      if (el) {
        el.focus();
        el.style.height = 'auto';
        el.style.height = el.scrollHeight + 'px';
      }
    }, 0);
  };

  const submitEdit = () => {
    const trimmed = editValue.trim();
    if (trimmed && trimmed !== task.description) {
      onRename(task.id, trimmed);
    }
    setEditing(false);
  };

  const handleEditKeyDown = (e: React.KeyboardEvent) => {
    // Stop propagation so dnd-kit keyboard listeners don't intercept (e.g. Space)
    e.stopPropagation();
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      submitEdit();
    }
    if (e.key === 'Escape') {
      e.stopPropagation();
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
    onShowStatus(`Copied: ${shortId}`);
  };

  return (
    <div
      className={cn(
        'group flex items-start gap-2 px-3 py-2 transition-colors hover:bg-accent/50',
      )}
    >
      {/* Checkbox + ID column */}
      <div className={cn('flex flex-col items-center mt-0.5', done && 'opacity-60')}>
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
          {done && <Check className="h-3 w-3" />}
          {inProg && <Minus className="h-3 w-3" />}
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
      <div className="flex-1 min-w-0 select-text">
        {editing ? (
          <textarea
            ref={inputRef}
            value={editValue}
            onChange={(e) => {
              setEditValue(e.target.value);
              e.target.style.height = 'auto';
              e.target.style.height = e.target.scrollHeight + 'px';
            }}
            onKeyDown={handleEditKeyDown}
            onPointerDown={(e) => e.stopPropagation()}
            onBlur={submitEdit}
            className="w-full bg-background border border-border rounded px-2 py-1 text-sm resize-none overflow-hidden"
            rows={1}
          />
        ) : (
          <div className={cn(done && 'opacity-60')}>
            <div className="flex items-start gap-1.5">
              {priorityIndicator && (
                <span className={cn('text-xs font-bold mt-0.5', priorityColor)}>
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

            {/* Description preview */}
            {descPreview && <MarkdownContent content={descPreview} />}

            {/* Relationship lines */}
            {relDetails?.parent && (
              <button onClick={() => onNavigateToTask(relDetails.parent!.id)} className="flex items-start gap-1 text-[10px] text-muted-foreground mt-0.5 hover:text-foreground transition-colors text-left">
                <CornerLeftUp className="h-3 w-3 flex-shrink-0 mt-0.5" />
                Subtask of ({relDetails.parent.id}) {relDetails.parent.title}
                {getLinkedStatusLabel(relDetails.parent.status) && (
                  <span className={getLinkedStatusColor(relDetails.parent.status)}>{getLinkedStatusLabel(relDetails.parent.status)}</span>
                )}
              </button>
            )}
            {relDetails?.subtasks.map((s) => (
              <button key={s.id} onClick={() => onNavigateToTask(s.id)} className="flex items-start gap-1 text-[10px] text-muted-foreground mt-0.5 hover:text-foreground transition-colors text-left">
                <CornerRightDown className="h-3 w-3 flex-shrink-0 mt-0.5" />
                Subtask ({s.id}) {s.title}
                {getLinkedStatusLabel(s.status) && (
                  <span className={getLinkedStatusColor(s.status)}>{getLinkedStatusLabel(s.status)}</span>
                )}
              </button>
            ))}
            {relDetails?.blocks.map((b) => (
              <button key={b.id} onClick={() => onNavigateToTask(b.id)} className="flex items-start gap-1 text-[10px] text-amber-400/80 mt-0.5 hover:text-foreground transition-colors text-left">
                <Ban className="h-3 w-3 flex-shrink-0 mt-0.5" />
                Blocks ({b.id}) {b.title}
                {getLinkedStatusLabel(b.status) && (
                  <span className={getLinkedStatusColor(b.status)}>{getLinkedStatusLabel(b.status)}</span>
                )}
              </button>
            ))}
            {relDetails?.blockedBy.map((b) => (
              <button key={b.id} onClick={() => onNavigateToTask(b.id)} className="flex items-start gap-1 text-[10px] text-amber-400/80 mt-0.5 hover:text-foreground transition-colors text-left">
                <Ban className="h-3 w-3 flex-shrink-0 mt-0.5" />
                Blocked by ({b.id}) {b.title}
                {getLinkedStatusLabel(b.status) && (
                  <span className={getLinkedStatusColor(b.status)}>{getLinkedStatusLabel(b.status)}</span>
                )}
              </button>
            ))}
            {relDetails?.related.map((r) => (
              <button key={r.id} onClick={() => onNavigateToTask(r.id)} className="flex items-start gap-1 text-[10px] text-teal-400/80 mt-0.5 hover:text-foreground transition-colors text-left">
                <Link2 className="h-3 w-3 flex-shrink-0 mt-0.5" />
                Related to ({r.id}) {r.title}
                {getLinkedStatusLabel(r.status) && (
                  <span className={getLinkedStatusColor(r.status)}>{getLinkedStatusLabel(r.status)}</span>
                )}
              </button>
            ))}

            {/* Due date */}
            {dueDateLabel && (
              <div className={cn('flex items-center gap-1 text-[10px] mt-0.5', dueDateColor)}>
                <Calendar className="h-3 w-3 flex-shrink-0" />
                {dueDateLabel.charAt(0).toUpperCase() + dueDateLabel.slice(1)}
              </div>
            )}

            {/* Tags */}
            {task.tags && task.tags.length > 0 && (
              <div className="flex flex-wrap items-center gap-1.5 mt-1">
                {task.tags.map((tag) => (
                  <button
                    key={tag}
                    onClick={(e) => {
                      e.stopPropagation();
                      onTagClick?.(tag);
                    }}
                    className={cn(
                      'inline-flex items-center gap-0.5 text-[10px] px-1.5 py-0 rounded-full hover:brightness-125 transition-all',
                      getTagColor(tag),
                    )}
                  >
                    <Tag className="h-2.5 w-2.5" />
                    {tag}
                  </button>
                ))}
              </div>
            )}
          </div>
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
          className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-foreground p-1 transition-opacity"
        >
          <Ellipsis className="h-4 w-4" />
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
              onClick={() => {
                onCreateSubtask(task.id);
                closeMenus();
              }}
              className="w-full text-left px-3 py-1.5 hover:bg-accent"
            >
              Create subtask
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
