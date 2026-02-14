import { useEffect, useRef, useState, useLayoutEffect } from 'react';
import { TaskStatus } from '@tasker/core/types';
import type { Suggestion } from '@/hooks/use-metadata-autocomplete.js';
import { cn } from '@/lib/utils.js';

interface AutocompleteDropdownProps {
  suggestions: Suggestion[];
  selectedIndex: number;
  onSelect: (index: number) => void;
}

const STATUS_DOT: Record<number, string> = {
  [TaskStatus.Pending]: 'bg-muted-foreground/40',
  [TaskStatus.InProgress]: 'bg-amber-400',
  [TaskStatus.Done]: 'bg-green-500',
};

export function AutocompleteDropdown({ suggestions, selectedIndex, onSelect }: AutocompleteDropdownProps) {
  const listRef = useRef<HTMLDivElement>(null);
  const selectedRef = useRef<HTMLButtonElement>(null);
  /** Placement decided once on mount — locked for this dropdown session */
  const [placement, setPlacement] = useState<'below' | 'above'>('below');
  const placementDecided = useRef(false);

  useLayoutEffect(() => {
    if (placementDecided.current) return;
    placementDecided.current = true;
    const el = listRef.current;
    if (!el) return;
    const parent = el.offsetParent as HTMLElement | null;
    if (!parent) return;
    const parentRect = parent.getBoundingClientRect();
    const spaceBelow = window.innerHeight - parentRect.bottom;
    if (spaceBelow < 210) setPlacement('above');
  }, []);

  // Scroll selected item into view
  useEffect(() => {
    selectedRef.current?.scrollIntoView({ block: 'nearest' });
  }, [selectedIndex]);

  return (
    <div
      ref={listRef}
      className={cn(
        'absolute right-0 z-50 w-[280px] max-h-[200px] overflow-y-auto rounded-md border border-border bg-popover shadow-lg',
        placement === 'below' ? 'top-full mt-1' : 'bottom-full mb-1',
      )}
      onMouseDown={(e) => e.preventDefault()} // prevent textarea blur
    >
      {suggestions.map((s, i) => (
        <button
          key={s.task.id}
          ref={i === selectedIndex ? selectedRef : undefined}
          onMouseDown={(e) => {
            e.preventDefault();
            onSelect(i);
          }}
          className={cn(
            'flex w-full items-center gap-2 px-2 py-1.5 text-left text-sm transition-colors',
            i === selectedIndex ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/50',
          )}
        >
          <span className="font-mono text-xs text-muted-foreground w-7 flex-shrink-0">{s.shortId}</span>
          <span className={cn('h-1.5 w-1.5 rounded-full flex-shrink-0', STATUS_DOT[s.task.status] ?? STATUS_DOT[0])} />
          <span className="truncate flex-1">{s.title}</span>
          <span className="text-[10px] text-muted-foreground/60 flex-shrink-0">{s.task.listName}</span>
        </button>
      ))}
    </div>
  );
}
