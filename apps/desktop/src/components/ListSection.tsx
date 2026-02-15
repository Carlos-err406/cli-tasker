import { useState, useRef, useMemo, useEffect } from 'react';
import type { Task, TaskStatus } from '@tasker/core/types';
import type { TaskRelDetails } from '@/hooks/use-tasker-store.js';
import { useMetadataAutocomplete } from '@/hooks/use-metadata-autocomplete.js';
import { AutocompleteDropdown } from '@/components/AutocompleteDropdown.js';
import {
  SortableContext,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { ChevronDown, Plus, Ellipsis, Eye, EyeOff } from 'lucide-react';
import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
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
  onDelete: (taskId: string, cascade?: boolean) => void;
  onMove: (taskId: string, targetList: string) => void;
  onRenameList: (oldName: string, newName: string) => void;
  onDeleteList: (name: string) => void;
  onShowStatus: (message: string) => void;
  onNavigateToTask: (taskId: string) => void;
  onTagClick?: (tag: string) => void;
  hideCompleted: boolean;
  onToggleHideCompleted: () => void;
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
  onTagClick,
  hideCompleted,
  onToggleHideCompleted,
}: ListSectionProps) {
  const [adding, setAdding] = useState(false);
  const [addValue, setAddValue] = useState('');
  const [editingName, setEditingName] = useState(false);
  const [nameValue, setNameValue] = useState('');
  const addInputRef = useRef<HTMLTextAreaElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);

  const ac = useMetadataAutocomplete(addValue, addInputRef);

  // Trigger autocomplete detection on value changes
  useEffect(() => {
    if (adding) ac.detect();
  }, [addValue, adding]);

  const visibleTasks = useMemo(
    () => hideCompleted ? tasks.filter((t) => t.status !== 2) : tasks,
    [tasks, hideCompleted],
  );

  const taskIds = useMemo(() => visibleTasks.map((t) => t.id), [visibleTasks]);

  const pendingCount = tasks.filter((t) => t.status === 0).length;
  const doneCount = tasks.filter((t) => t.status === 2).length;
  const totalCount = tasks.length;
  const hiddenDoneCount = hideCompleted ? doneCount : 0;
  const summaryParts: string[] = [];
  summaryParts.push(`${totalCount} task${totalCount !== 1 ? 's' : ''}`);
  if (pendingCount < totalCount) summaryParts.push(`${pendingCount} pending`);
  if (hiddenDoneCount > 0) summaryParts.push(`+${hiddenDoneCount} done`);
  const summary = summaryParts.join(', ');

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
        el.style.height = 'auto';
        el.style.height = el.scrollHeight + 'px';
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
    // Let autocomplete handle its keys first
    if (ac.onKeyDown(e)) {
      if ((e.key === 'Enter' || e.key === 'Tab') && ac.isOpen) {
        const newVal = ac.select(ac.selectedIndex);
        if (newVal !== null) setAddValue(newVal);
      }
      return;
    }
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
    setTimeout(() => nameInputRef.current?.focus(), 50);
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
          {doneCount > 0 && (
            <button
              onClick={onToggleHideCompleted}
              className="text-muted-foreground hover:text-foreground p-0.5"
              title={hideCompleted ? 'Show completed tasks' : 'Hide completed tasks'}
            >
              {hideCompleted ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
            </button>
          )}
          <button
            onClick={() => startAdd()}
            className="text-muted-foreground hover:text-foreground p-0.5"
            title="Add task"
          >
            <Plus className="h-4 w-4" />
          </button>

          {!isDefault && (
            <DropdownMenu.Root>
              <DropdownMenu.Trigger asChild>
                <button className="text-muted-foreground hover:text-foreground p-0.5">
                  <Ellipsis className="h-4 w-4" />
                </button>
              </DropdownMenu.Trigger>
              <DropdownMenu.Portal>
                <DropdownMenu.Content
                  side="bottom"
                  align="end"
                  collisionPadding={8}
                  className="z-50 min-w-[120px] bg-popover border border-border rounded-md shadow-lg py-1 text-sm"
                >
                  <DropdownMenu.Item
                    onSelect={startEditName}
                    className="px-3 py-1.5 hover:bg-accent outline-none cursor-default"
                  >
                    Rename
                  </DropdownMenu.Item>
                  <DropdownMenu.Item
                    onSelect={() => onDeleteList(listName)}
                    className="px-3 py-1.5 hover:bg-accent outline-none cursor-default text-red-400"
                  >
                    Delete
                  </DropdownMenu.Item>
                </DropdownMenu.Content>
              </DropdownMenu.Portal>
            </DropdownMenu.Root>
          )}
        </div>
      </div>

      {/* Tasks */}
      <div
        className="grid transition-[grid-template-rows] duration-200 ease-in-out"
        style={{ gridTemplateRows: collapsed ? '0fr' : '1fr' }}
      >
        <div className="overflow-hidden">
          {adding && (
            <div className="px-3 py-2 border-b border-border/30">
              <div className="relative">
                <textarea
                  ref={addInputRef}
                  value={addValue}
                  onChange={(e) => {
                    setAddValue(e.target.value);
                    e.target.style.height = 'auto';
                    e.target.style.height = e.target.scrollHeight + 'px';
                  }}
                  onKeyDown={handleAddKeyDown}
                  onBlur={() => {
                    if (!ac.isOpen) {
                      if (addValue.trim()) submitAdd();
                      else setAdding(false);
                    }
                  }}
                  placeholder="New task... (Cmd+Enter to submit)"
                  className="w-full bg-background border border-border rounded px-2 py-1 text-sm resize-none overflow-hidden placeholder:text-muted-foreground/50"
                  rows={1}
                />
                {ac.isOpen && (
                  <AutocompleteDropdown
                    suggestions={ac.suggestions}
                    selectedIndex={ac.selectedIndex}
                    onSelect={(i) => {
                      const newVal = ac.select(i);
                      if (newVal !== null) setAddValue(newVal);
                    }}
                  />
                )}
              </div>
            </div>
          )}

          {visibleTasks.length === 0 && !adding && (
            <div className="px-3 py-3 text-xs text-muted-foreground/50 text-center">
              {hideCompleted && doneCount > 0 ? 'All tasks completed' : 'No tasks'}
            </div>
          )}

          <SortableContext items={taskIds} strategy={verticalListSortingStrategy}>
            {visibleTasks.map((task) => (
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
                onTagClick={onTagClick}
              />
            ))}
          </SortableContext>
        </div>
      </div>
    </div>
  );
}
