import { PointerSensor } from '@dnd-kit/core';

/**
 * Custom pointer sensor that cancels drag activation when movement is
 * primarily horizontal. This allows text selection in task content while
 * still supporting vertical drag-and-drop reordering.
 *
 * Works by intercepting handleMove before the distance constraint fires:
 * if horizontal movement exceeds vertical, we cancel the sensor so the
 * browser handles text selection natively.
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

    // Cancel drag if movement is primarily horizontal (text selection)
    if (dx > dy && dx >= 3) {
      return this.handleCancel();
    }
  }

  return originalHandleMove.call(this, event);
};
