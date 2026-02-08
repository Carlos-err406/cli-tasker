import { useState, useRef, useCallback, useEffect } from 'react';
import { cn } from '@/lib/utils.js';
import { useTaskerStore } from '@/hooks/use-tasker-store.js';
import { useKeyboardShortcuts } from '@/hooks/use-keyboard-shortcuts.js';
import { useDebounce } from '@/hooks/use-debounce.js';
import { hideWindow } from '@/lib/services/window.js';
import { ListSection } from '@/components/ListSection.js';
import { SearchBar } from '@/components/SearchBar.js';
import { HelpPanel } from '@/components/HelpPanel.js';

export default function App() {
  const store = useTaskerStore();
  const [showHelp, setShowHelp] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [creatingList, setCreatingList] = useState(false);
  const [newListName, setNewListName] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);
  const listInputRef = useRef<HTMLInputElement>(null);

  const debouncedSearch = useDebounce(searchInput, 200);

  useEffect(() => {
    store.setSearch(debouncedSearch);
  }, [debouncedSearch, store.setSearch]);

  const handleToggleHelp = useCallback(() => {
    setShowHelp((v) => !v);
  }, []);

  useKeyboardShortcuts({
    onUndo: store.undo,
    onRedo: store.redo,
    onRefresh: store.refresh,
    onFocusSearch: () => searchRef.current?.focus(),
    onToggleHelp: handleToggleHelp,
  });

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

  // Filter dropdown
  const [showFilterMenu, setShowFilterMenu] = useState(false);
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
      <div className="h-screen w-screen flex items-center justify-center bg-background">
        <span className="text-sm text-muted-foreground">Loading...</span>
      </div>
    );
  }

  return (
    <div className="dark h-screen w-screen flex flex-col bg-background text-foreground rounded-xl overflow-hidden popup-glass">
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2 bg-secondary/20 border-b border-border/50">
        <span className="text-sm font-semibold flex-shrink-0">Tasker</span>

        {/* Filter dropdown */}
        <div className="relative flex-1">
          <button
            onClick={() => setShowFilterMenu(!showFilterMenu)}
            className="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1"
          >
            {filterLabel}
            <span className="text-[10px]">&#9660;</span>
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
          className="text-muted-foreground hover:text-foreground text-sm"
          title="Create list"
        >
          +
        </button>

        <button
          onClick={handleToggleHelp}
          className="text-muted-foreground hover:text-foreground text-sm"
          title="Help (&#8984;/)"
        >
          ?
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
                    if (e.key === 'Escape') setCreatingList(false);
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
            {store.lists.map((listName) => {
              // If filtering to a specific list, skip others
              if (store.filterList && store.filterList !== listName) return null;

              const tasks = store.tasksByList[listName] ?? [];
              const collapsed = store.collapsedLists.has(listName);

              return (
                <ListSection
                  key={listName}
                  listName={listName}
                  tasks={tasks}
                  lists={store.lists}
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
                />
              );
            })}

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
          onClick={() => hideWindow()}
          className="hover:text-foreground"
        >
          Close
        </button>
      </div>
    </div>
  );
}
