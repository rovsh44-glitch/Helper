import React, { useCallback } from 'react';

type PanelResizeHandleProps = {
  axis: 'x' | 'y';
  onResizeDelta: (delta: number) => void;
  className?: string;
  title?: string;
};

const KEYBOARD_STEP = 16;

export function PanelResizeHandle({
  axis,
  onResizeDelta,
  className = '',
  title,
}: PanelResizeHandleProps) {
  const handlePointerDown = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();

    const startTarget = event.currentTarget;
    const startPosition = axis === 'x' ? event.clientX : event.clientY;
    let previousPosition = startPosition;

    startTarget.setPointerCapture?.(event.pointerId);

    const handlePointerMove = (moveEvent: PointerEvent) => {
      const currentPosition = axis === 'x' ? moveEvent.clientX : moveEvent.clientY;
      const delta = currentPosition - previousPosition;
      if (delta !== 0) {
        onResizeDelta(delta);
        previousPosition = currentPosition;
      }
    };

    const stopDragging = () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', stopDragging);
      window.removeEventListener('pointercancel', stopDragging);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', stopDragging);
    window.addEventListener('pointercancel', stopDragging);
  }, [axis, onResizeDelta]);

  const handleKeyDown = useCallback((event: React.KeyboardEvent<HTMLDivElement>) => {
    if (axis === 'x') {
      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        onResizeDelta(-KEYBOARD_STEP);
      } else if (event.key === 'ArrowRight') {
        event.preventDefault();
        onResizeDelta(KEYBOARD_STEP);
      }
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      onResizeDelta(-KEYBOARD_STEP);
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      onResizeDelta(KEYBOARD_STEP);
    }
  }, [axis, onResizeDelta]);

  const orientation = axis === 'x' ? 'vertical' : 'horizontal';
  const cursorClass = axis === 'x'
    ? 'h-full w-2 cursor-col-resize'
    : 'h-2 w-full cursor-row-resize';

  return (
    <div
      role="separator"
      tabIndex={0}
      aria-orientation={orientation}
      title={title}
      onPointerDown={handlePointerDown}
      onKeyDown={handleKeyDown}
      className={`group relative shrink-0 touch-none outline-none ${cursorClass} ${className}`}
    >
      <div className="absolute inset-0 bg-transparent transition-colors group-hover:bg-primary-500/10 group-focus:bg-primary-500/15 group-active:bg-primary-500/20" />
      <div className={`absolute bg-primary-400/30 transition-opacity group-hover:opacity-100 group-focus:opacity-100 ${
        axis === 'x'
          ? 'bottom-0 left-1/2 top-0 w-px -translate-x-1/2'
          : 'left-0 top-1/2 h-px w-full -translate-y-1/2'
      } opacity-0`} />
    </div>
  );
}
