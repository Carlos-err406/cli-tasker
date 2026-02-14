import { PointerSensor } from '@dnd-kit/core';

/**
 * Custom pointer sensor that cancels drag activation when initial movement
 * is primarily horizontal. This allows text selection in task titles while
 * still supporting vertical drag-and-drop reordering.
 *
 * Works by intercepting handleMove before the distance constraint is checked:
 * if the user moves more horizontally than vertically, we cancel instead of
 * starting a drag.
 */

const originalHandleMove = (PointerSensor.prototype as any).handleMove;

export class VerticalPointerSensor extends PointerSensor {}

(VerticalPointerSensor.prototype as any).handleMove = function (
  this: any,
  event: PointerEvent,
) {
  const { activated, initialCoordinates } = this;

  if (!activated && initialCoordinates) {
    const dx = Math.abs(event.clientX - initialCoordinates.x);
    const dy = Math.abs(event.clientY - initialCoordinates.y);

    // Once movement is significant, cancel if primarily horizontal
    if (dx > dy && dx + dy > 3) {
      return this.handleCancel();
    }
  }

  return originalHandleMove.call(this, event);
};
