import { useState, useRef, useCallback, useEffect, useMemo } from 'react';
import { cn } from '@/lib/utils.js';
import { useTaskerStore } from '@/hooks/use-tasker-store.js';
import { useKeyboardShortcuts } from '@/hooks/use-keyboard-shortcuts.js';
import { useClickOutside } from '@/hooks/use-click-outside.js';
import { useDebounce } from '@/hooks/use-debounce.js';
import { hideWindow, quitApp } from '@/lib/services/window.js';
import {
  DndContext,
  DragOverlay,
  closestCenter,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
  type DragStartEvent,
  type DragEndEvent,
  type Modifier,
} from '@dnd-kit/core';
import {
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { ChevronDown, Plus, CircleHelp, ArrowUpDown, ChevronsDownUp } from 'lucide-react';
import { SortableListSection } from '@/components/SortableListSection.js';
import { TaskItem } from '@/components/TaskItem.js';
import { SearchBar } from '@/components/SearchBar.js';
import { HelpPanel } from '@/components/HelpPanel.js';

const restrictToVerticalAxis: Modifier = ({ transform }) => ({
  ...transform,
  x: 0,
});

export default function App() {
  const store = useTaskerStore();
  const [showHelp, setShowHelp] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [creatingList, setCreatingList] = useState(false);
  const [newListName, setNewListName] = useState('');
  const [showFilterMenu, setShowFilterMenu] = useState(false);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeType, setActiveType] = useState<'task' | 'list' | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const listInputRef = useRef<HTMLInputElement>(null);
  const filterMenuRef = useRef<HTMLDivElement>(null);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  // List IDs for SortableContext (prefixed to avoid collision with task IDs)
  const listSortIds = useMemo(
    () => store.lists.map((name) => `list::${name}`),
    [store.lists],
  );

  const debouncedSearch = useDebounce(searchInput, 200);

  useEffect(() => {
    store.setSearch(debouncedSearch);
  }, [debouncedSearch, store.setSearch]);

  const handleToggleHelp = useCallback(() => {
    setShowHelp((v) => !v);
  }, []);

  const handleEscape = useCallback(() => {
    // ESC closes the topmost layer: help > filter menu > creating list > window
    if (showHelp) {
      setShowHelp(false);
    } else if (showFilterMenu) {
      setShowFilterMenu(false);
    } else if (creatingList) {
      setCreatingList(false);
    } else {
      hideWindow();
    }
  }, [showHelp, showFilterMenu, creatingList]);

  useKeyboardShortcuts({
    onUndo: store.undo,
    onRedo: store.redo,
    onRefresh: store.refresh,
    onFocusSearch: () => searchRef.current?.focus(),
    onToggleHelp: handleToggleHelp,
    onApplySort: store.applySystemSort,
    onToggleCollapseAll: store.toggleCollapseAll,
    onEscape: handleEscape,
  });

  useClickOutside(filterMenuRef, useCallback(() => setShowFilterMenu(false), []));

  const startCreateList = () => {
    setCreatingList(true);
    setNewListName('');
    setTimeout(() => listInputRef.current?.focus(), 0);
  };

  const submitCreateList = () => {
    const trimmed = newListName.trim();
    if (trimmed) {
      store.createList(trimmed);
    }
    setCreatingList(false);
    setNewListName('');
  };

  const handleDragStart = useCallback((event: DragStartEvent) => {
    const id = String(event.active.id);
    if (id.startsWith('list::')) {
      setActiveId(id);
      setActiveType('list');
    } else {
      setActiveId(id);
      setActiveType('task');
    }
  }, []);

  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      const { active, over } = event;
      setActiveId(null);
      setActiveType(null);

      if (!over || active.id === over.id) return;

      const activeStr = String(active.id);
      const overStr = String(over.id);

      if (activeStr.startsWith('list::') && overStr.startsWith('list::')) {
        // List reorder
        const activeName = activeStr.slice(6);
        const overName = overStr.slice(6);
        const oldIndex = store.lists.indexOf(activeName);
        const newIndex = store.lists.indexOf(overName);
        if (oldIndex !== -1 && newIndex !== -1) {
          store.reorderList(activeName, newIndex, oldIndex);
        }
      } else if (!activeStr.startsWith('list::') && !overStr.startsWith('list::')) {
        // Task reorder
        const task = store.tasks.find((t) => t.id === activeStr);
        if (!task) return;
        const listTasks = store.tasksByList[task.listName] ?? [];
        const oldIndex = listTasks.findIndex((t) => t.id === activeStr);
        const newIndex = listTasks.findIndex((t) => t.id === overStr);
        if (oldIndex !== -1 && newIndex !== -1) {
          store.reorderTask(activeStr, newIndex, task.listName, oldIndex);
        }
      }
    },
    [store],
  );

  const handleDragCancel = useCallback(() => {
    setActiveId(null);
    setActiveType(null);
  }, []);

  // Find the active task for DragOverlay rendering
  const activeTask = activeType === 'task' && activeId
    ? store.tasks.find((t) => t.id === activeId) ?? null
    : null;
  const activeListName = activeType === 'list' && activeId
    ? activeId.slice(6)
    : null;

  // Filter dropdown
  const filterLabel = store.filterList ?? 'All Lists';

  // Status bar text
  const statusText = store.searchQuery
    ? `${store.totalCount} matching`
    : store.statusMessage ||
      (store.inProgressCount > 0
        ? `${store.inProgressCount} active, ${store.pendingCount} pending, ${store.totalCount} total`
        : `${store.pendingCount} pending, ${store.totalCount} total`);

  if (store.loading) {
    return (
      <div className="dark h-screen w-screen p-1">
        <div className="h-full flex items-center justify-center bg-background rounded-xl">
          <span className="text-sm text-muted-foreground">Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="dark h-screen w-screen p-1">
    <div className="h-full flex flex-col bg-background text-foreground rounded-xl overflow-hidden border border-border/60 popup-glass">
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2 bg-secondary/20 border-b border-border/50">
        <span className="text-sm font-semibold flex-shrink-0">Tasker</span>

        {/* Filter dropdown */}
        <div ref={filterMenuRef} className="relative flex-1">
          <button
            onClick={() => setShowFilterMenu(!showFilterMenu)}
            className="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1"
          >
            {filterLabel}
            <ChevronDown className="h-3 w-3" />
          </button>
          {showFilterMenu && (
            <div className="absolute left-0 top-5 z-50 min-w-[120px] bg-popover border border-border rounded-md shadow-lg py-1 text-sm">
              <button
                onClick={() => {
                  store.setFilterList(null);
                  setShowFilterMenu(false);
                }}
                className={cn(
                  'w-full text-left px-3 py-1.5 hover:bg-accent text-xs',
                  store.filterList === null && 'text-primary font-medium',
                )}
              >
                All Lists
              </button>
              {store.lists.map((l) => (
                <button
                  key={l}
                  onClick={() => {
                    store.setFilterList(l);
                    setShowFilterMenu(false);
                  }}
                  className={cn(
                    'w-full text-left px-3 py-1.5 hover:bg-accent text-xs',
                    store.filterList === l && 'text-primary font-medium',
                  )}
                >
                  {l}
                </button>
              ))}
            </div>
          )}
        </div>

        <button
          onClick={startCreateList}
          className="text-muted-foreground hover:text-foreground p-0.5"
          title="Create list"
        >
          <Plus className="h-4 w-4" />
        </button>

        <button
          onClick={store.toggleCollapseAll}
          className="text-muted-foreground hover:text-foreground p-0.5"
          title="Collapse/expand all (⌘E)"
        >
          <ChevronsDownUp className="h-4 w-4" />
        </button>

        <button
          onClick={store.applySystemSort}
          className="text-muted-foreground hover:text-foreground p-0.5"
          title="System sort (⌘J)"
        >
          <ArrowUpDown className="h-4 w-4" />
        </button>

        <button
          onClick={handleToggleHelp}
          className="text-muted-foreground hover:text-foreground p-0.5"
          title="Help (&#8984;/)"
        >
          <CircleHelp className="h-4 w-4" />
        </button>
      </div>

      {/* Search */}
      <div className="px-3 py-1.5">
        <SearchBar
          ref={searchRef}
          value={searchInput}
          onChange={setSearchInput}
        />
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto relative">
        {showHelp ? (
          <HelpPanel onClose={() => setShowHelp(false)} />
        ) : (
          <>
            {/* Create list inline */}
            {creatingList && (
              <div className="px-3 py-2 border-b border-border/50">
                <input
                  ref={listInputRef}
                  value={newListName}
                  onChange={(e) => setNewListName(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') submitCreateList();
                    if (e.key === 'Escape') {
                      e.stopPropagation();
                      setCreatingList(false);
                    }
                  }}
                  onBlur={() => {
                    if (newListName.trim()) submitCreateList();
                    else setCreatingList(false);
                  }}
                  placeholder="List name..."
                  className="w-full bg-background border border-border rounded px-2 py-1 text-sm"
                />
              </div>
            )}

            {/* List sections */}
            <DndContext
              sensors={sensors}
              collisionDetection={closestCenter}
              modifiers={[restrictToVerticalAxis]}
              onDragStart={handleDragStart}
              onDragEnd={handleDragEnd}
              onDragCancel={handleDragCancel}
            >
              <SortableContext items={listSortIds} strategy={verticalListSortingStrategy}>
                {store.lists.map((listName) => {
                  // If filtering to a specific list, skip others
                  if (store.filterList && store.filterList !== listName) return null;

                  const tasks = store.tasksByList[listName] ?? [];
                  const collapsed = store.collapsedLists.has(listName);

                  return (
                    <SortableListSection
                      key={listName}
                      listName={listName}
                      tasks={tasks}
                      lists={store.lists}
                      relDetails={store.relDetails}
                      isDefault={listName === store.defaultList}
                      collapsed={collapsed}
                      onToggleCollapsed={() => store.toggleCollapsed(listName)}
                      onAddTask={store.addTask}
                      onToggleStatus={store.toggleStatus}
                      onSetStatus={store.setStatusTo}
                      onRename={store.rename}
                      onDelete={store.deleteTask}
                      onMove={store.moveTask}
                      onRenameList={store.renameList}
                      onDeleteList={store.deleteList}
                      onShowStatus={store.showStatus}
                      onNavigateToTask={store.navigateToTask}
                    />
                  );
                })}
              </SortableContext>

              <DragOverlay dropAnimation={null}>
                {activeTask && (
                  <div className="rounded-lg bg-secondary/90 shadow-lg scale-[1.02] opacity-90 border border-border/60">
                    <TaskItem
                      task={activeTask}
                      lists={store.lists}
                      relDetails={store.relDetails[activeTask.id]}
                      onToggleStatus={() => {}}
                      onSetStatus={() => {}}
                      onRename={() => {}}
                      onDelete={() => {}}
                      onMove={() => {}}
                      onShowStatus={() => {}}
                      onNavigateToTask={() => {}}
                      onCreateSubtask={() => {}}
                    />
                  </div>
                )}
                {activeListName && (
                  <div className="rounded-lg bg-secondary/90 shadow-lg scale-[1.02] opacity-90 border border-border/60">
                    <div className="flex items-center gap-2 px-3 py-2">
                      <span className="text-sm font-semibold">{activeListName}</span>
                      <span className="text-[10px] text-muted-foreground">
                        {(store.tasksByList[activeListName] ?? []).length} tasks
                      </span>
                    </div>
                  </div>
                )}
              </DragOverlay>
            </DndContext>

            {store.searchQuery && store.totalCount === 0 && (
              <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
                No results for &quot;{store.searchQuery}&quot;
              </div>
            )}
          </>
        )}
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between px-3 py-1.5 bg-secondary/20 border-t border-border/50 text-[10px] text-muted-foreground">
        <span>{statusText}</span>
        <button
          onClick={() => quitApp()}
          className="hover:text-foreground"
        >
          Quit
        </button>
      </div>
    </div>
    </div>
  );
}
