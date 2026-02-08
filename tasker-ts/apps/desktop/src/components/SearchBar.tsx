import { forwardRef } from 'react';
import { cn } from '@/lib/utils.js';

interface SearchBarProps {
  value: string;
  onChange: (value: string) => void;
  className?: string;
}

export const SearchBar = forwardRef<HTMLInputElement, SearchBarProps>(
  ({ value, onChange, className }, ref) => {
    return (
      <input
        ref={ref}
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            if (value) {
              onChange('');
              e.stopPropagation();
            }
          }
        }}
        placeholder="Search all tasks... (&#8984;K)"
        className={cn(
          'w-full bg-secondary/50 border border-border/50 rounded-md px-3 py-1.5 text-sm',
          'placeholder:text-muted-foreground/50 focus:outline-none focus:ring-1 focus:ring-ring',
          className,
        )}
      />
    );
  },
);

SearchBar.displayName = 'SearchBar';
