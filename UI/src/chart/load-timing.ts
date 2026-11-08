/**
 * How the line chart finds its size after it mounts, and the load timings it reports (first data, first draw).
 *
 * Gameface lays out once a frame, so a just-mounted element reads 0 wide in the layout effect. The chart used to retry
 * only on its 500 ms resize poll (or the next history update, about once a second), so it drew up to a second late.
 * It now measures every frame until it has a size, and for a short while after (the panel can still be settling),
 * then falls back to the slow poll for resizes.
 */

/** Frames after mount during which the chart measures every frame (about 1.5 s at 60 fps). */
export const FAST_MEASURE_FRAMES = 90;

/** The resize poll once the fast frames are over (no ResizeObserver in Gameface). */
export const SLOW_MEASURE_MS = 500;

export interface ChartSize {
  w: number;
  h: number;
  rem: number;
}

/** True when the chart can draw: the container has been laid out (both sides above 0). */
export function hasDrawableSize(size: ChartSize): boolean {
  return size.w > 0 && size.h > 0;
}

/**
 * When to measure next, given the frames since mount: "frame" (next animation frame) during the first
 * FAST_MEASURE_FRAMES, or until a size is found, whichever is later, but never past 4x FAST_MEASURE_FRAMES; then "poll".
 */
export function nextMeasure(frame: number, size: ChartSize): "frame" | "poll" {
  if (frame < FAST_MEASURE_FRAMES) return "frame";
  if (!hasDrawableSize(size) && frame < 4 * FAST_MEASURE_FRAMES) return "frame";
  return "poll";
}

/** Milestones of one chart load, in ms since the chart mounted; each is recorded once. */
export class LoadTimer {
  private readonly marks: [string, number][] = [];

  constructor(private readonly start: number) {}

  /** Records the milestone at `now` the first time; later calls are ignored. True when recorded. */
  mark(name: string, now: number): boolean {
    if (this.has(name)) return false;
    this.marks.push([name, Math.max(0, now - this.start)]);
    return true;
  }

  has(name: string): boolean {
    return this.marks.some(([n]) => n === name);
  }

  /** ms from mount to the milestone, or undefined if it hasn't happened. */
  elapsed(name: string): number | undefined {
    return this.marks.find(([n]) => n === name)?.[1];
  }

  /** "data +3 ms, size +18 ms, draw +19 ms" in the order recorded, plus any extra text. */
  line(extra = ""): string {
    const parts = this.marks.map(([n, ms]) => `${n} +${Math.round(ms)} ms`).join(", ");
    return extra ? `${parts}; ${extra}` : parts;
  }
}

/** A millisecond clock: performance.now where Gameface has it, else Date.now. */
export function nowMs(): number {
  return typeof performance !== "undefined" && typeof performance.now === "function" ? performance.now() : Date.now();
}
