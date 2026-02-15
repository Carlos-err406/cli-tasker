import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { Task, TaskStatus } from '@tasker/core/types';
import type { TaskRelDetails } from '@/hooks/use-tasker-store.js';
import { ListSection } from './ListSection.js';

interface SortableListSectionProps {
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
  onTagClick?: (tag: string) => void;
  hideCompleted: boolean;
  onToggleHideCompleted: () => void;
}

export function SortableListSection({ listName, ...rest }: SortableListSectionProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: `list::${listName}` });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  };

  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners}>
      <ListSection
        listName={listName}
        {...rest}
      />
    </div>
  );
}
