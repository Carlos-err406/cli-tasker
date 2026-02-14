import { PointerSensor } from '@dnd-kit/core';

/**
 * Custom pointer sensor that only activates drag when initial movement
 * is primarily vertical. This allows horizontal text selection in task
 * titles while still supporting vertical drag-and-drop reordering.
 */
export class VerticalPointerSensor extends PointerSensor {
  static activators = [
    {
      eventName: 'onPointerDown' as const,
      handler(
        { nativeEvent: event }: { nativeEvent: PointerEvent },
        { onActivation }: { onActivation?: (event: { event: PointerEvent }) => void },
      ) {
        if (
          !event.isPrimary ||
          event.button !== 0 ||
          isInteractiveElement(event.target as Element)
        ) {
          return false;
        }

        const startX = event.clientX;
        const startY = event.clientY;
        let decided = false;

        const threshold = 5;

        function onPointerMove(e: PointerEvent) {
          if (decided) return;

          const dx = Math.abs(e.clientX - startX);
          const dy = Math.abs(e.clientY - startY);

          if (dx < threshold && dy < threshold) return;

          decided = true;
          cleanup();

          // Only allow drag if movement is primarily vertical
          if (dy > dx) {
            onActivation?.({ event });
          }
        }

        function onPointerUp() {
          decided = true;
          cleanup();
        }

        function cleanup() {
          document.removeEventListener('pointermove', onPointerMove);
          document.removeEventListener('pointerup', onPointerUp);
        }

        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);

        return false;
      },
    },
  ];
}

function isInteractiveElement(element: Element | null): boolean {
  if (!element) return false;
  const tag = element.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    tag === 'BUTTON' ||
    (element as HTMLElement).isContentEditable
  );
}
