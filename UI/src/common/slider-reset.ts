// The pure half of a slider row's reset button and marker (reset-button.tsx, slider-marker.tsx, slider-reset-frame.tsx).
// No game imports, so vitest runs it in Node. Shared by the Taxation tab and, later, the Services tab.

/**
 * What a row's reset does: "default" sets it back to the game's default value (the city view), "city" clears the
 * district's own value so the row follows the city again (a district view; the icon carries a small city glyph).
 */
export type ResetKind = "default" | "city";

/**
 * How a row's reset button looks while the row is already at its reset target: "hide" (the space is kept, so nothing
 * moves) or "dim" (shown faded and inert). Provisional, pending an in-game look.
 */
export const RESET_WHEN_UNCHANGED: "hide" | "dim" = "hide";

export type ResetVisibility = "shown" | "dimmed" | "hidden";

/** The button's look: shown while the row differs from its target, else hidden or dimmed (`mode`). */
export function resetVisibility(differs: boolean, mode: "hide" | "dim" = RESET_WHEN_UNCHANGED): ResetVisibility {
  if (differs) return "shown";
  return mode === "dim" ? "dimmed" : "hidden";
}

/** Whether a click resets anything: only while the button is shown. */
export function resetClickable(visibility: ResetVisibility): boolean {
  return visibility === "shown";
}

/**
 * Where the marker sits along the track: (target − min) / (max − min), 0 at the left end, 1 at the right. Null when
 * the range is empty or the target lies outside it (no marker rather than one pinned to an end).
 */
export function markerFraction(target: number, min: number, max: number): number | null {
  if (!Number.isFinite(target) || !Number.isFinite(min) || !Number.isFinite(max) || max <= min) return null;
  if (target < min || target > max) return null;
  return (target - min) / (max - min);
}

/** A box in px, relative to the frame (slider-reset-frame.tsx measures the track with getBoundingClientRect). */
export interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** The box of `inner` relative to `outer` (both page boxes, e.g. getBoundingClientRect results). */
export function relativeBox(outer: Box, inner: Box): Box {
  return { left: inner.left - outer.left, top: inner.top - outer.top, width: inner.width, height: inner.height };
}

/**
 * The marker's left edge in px within the frame: the track's left plus the fraction of its width, less half the
 * marker's own width so it is centred on the value. Null with no fraction or an unmeasured track.
 */
export function markerLeft(track: Box | null, fraction: number | null, markerWidth: number): number | null {
  if (!track || fraction === null || track.width <= 0) return null;
  return track.left + fraction * track.width - markerWidth / 2;
}

/** Frames a measurement must hold unchanged before the frame stops re-measuring (slider-reset-frame.tsx). */
export const SETTLE_FRAMES = 3;

/** The most frames the frame re-measures after a render, settled or not. */
export const MAX_MEASURE_FRAMES = 120;

/**
 * Whether the frame measures again next frame. Gameface lays a panel out over several frames (the Economy panel
 * opens animated), so the first measurements can find no track or no button slot, and a paused game may not render
 * the row again: the button and marker then never showed until a slider was touched. So the frame keeps measuring
 * each frame until it has found both and they have held for {@link SETTLE_FRAMES} frames, at most
 * {@link MAX_MEASURE_FRAMES} frames after each render.
 */
export function keepMeasuring(frame: number, stableFrames: number, found: boolean): boolean {
  if (frame >= MAX_MEASURE_FRAMES) return false;
  return !found || stableFrames < SETTLE_FRAMES;
}
