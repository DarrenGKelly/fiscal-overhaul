// Pure geometry for the frame around the district picker and the tabs it drives [picker | Budget Taxation Services]
// (economy-tab-bar.tsx): the box, in px relative to the picker's zero-size room marker, from measured rects. No game
// imports, so vitest runs it in Node.

export interface Rect {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

export interface FrameBox {
  left: number;
  top: number;
  width: number;
  height: number;
}

/**
 * The frame: from the picker's left edge to the last district tab's right edge, the tabs' height, `pad` px out on
 * every side; relative to `room` (the picker's marker, whose left edge is the picker's). Null without tabs, or for a
 * degenerate measurement (layout not done yet).
 */
export function tabGroupFrame(room: Rect, tabs: Rect[], pad: number): FrameBox | null {
  if (tabs.length === 0) return null;
  const top = Math.min(...tabs.map((t) => t.top));
  const bottom = Math.max(...tabs.map((t) => t.bottom));
  const right = Math.max(...tabs.map((t) => t.right));
  if (!(bottom > top) || !(right > room.left)) return null;
  return { left: -pad, top: top - room.top - pad, width: right - room.left + 2 * pad, height: bottom - top + 2 * pad };
}

/** The tab buttons' height (the picker is drawn as tall), or null before layout. */
export function tabHeight(tabs: Rect[]): number | null {
  const h = tabs.length ? Math.max(...tabs.map((t) => t.bottom - t.top)) : 0;
  return h > 0 ? h : null;
}

/** Whether two measurements are the same box, within half a pixel (stops the re-measure loop re-rendering). */
export function sameBox(a: FrameBox | null, b: FrameBox | null): boolean {
  if (!a || !b) return a === b;
  return Math.abs(a.left - b.left) < 0.5 && Math.abs(a.top - b.top) < 0.5 && Math.abs(a.width - b.width) < 0.5 && Math.abs(a.height - b.height) < 0.5;
}
