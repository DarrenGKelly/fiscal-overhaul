import { ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";
import { ResetButton, ResetButtonProps } from "./reset-button";
import { SliderMarker } from "./slider-marker";
import { Box, keepMeasuring, relativeBox } from "./slider-reset";
import styles from "./slider-reset-frame.module.scss";

// A slider with its reset button and target marker over it, shared by the Taxation tab and (later) the Services tab.
// A vanilla slider can't take children, so the frame wraps it (position: relative, taking the slider's own layout
// class) and draws both on top: the marker at the track's measured box, the button centred in the `slot` span (px
// from the frame's right edge, e.g. the gap between the track's end and a column divider). No ResizeObserver in
// Gameface, and it lays out once a frame: the frame measures after each render and on the next frame.

export interface FrameLayout {
  /** The slider's track, whose box the marker is placed on; null: no marker. */
  track: Element | null;
  /** The span the button is centred in: its right edge in px from the frame's right edge, and its width; null: no button. */
  slot: Slot | null;
}

export interface Slot {
  right: number;
  width: number;
}

export interface SliderResetFrameProps {
  /** The slider's layout class from its parent (it moves to the frame; the slider fills the frame). */
  className?: string;
  /** Where the track and the button go, found in the frame's DOM. */
  locate: (frame: HTMLElement) => FrameLayout;
  reset: ResetButtonProps;
  /** The marker's place along the track (slider-reset.ts markerFraction); null: none. */
  fraction: number | null;
  children: ReactNode;
}

interface Measured {
  track: Box | null;
  slot: Slot | null;
}

const same = (a: Measured, b: Measured) =>
  (a.slot === b.slot || (!!a.slot && !!b.slot && a.slot.right === b.slot.right && a.slot.width === b.slot.width)) &&
  (a.track === b.track ||
    (!!a.track && !!b.track && a.track.left === b.track.left && a.track.top === b.track.top && a.track.width === b.track.width && a.track.height === b.track.height));

export const SliderResetFrame = ({ className, locate, reset, fraction, children }: SliderResetFrameProps) => {
  const ref = useRef<HTMLDivElement>(null);
  const [measured, setMeasured] = useState<Measured>({ track: null, slot: null });
  const measure = (): Measured | null => {
    const frame = ref.current;
    if (!frame) return null;
    const layout = locate(frame);
    const outer = frame.getBoundingClientRect();
    const track = layout.track ? relativeBox(outer, layout.track.getBoundingClientRect()) : null;
    const next = { track, slot: layout.slot };
    setMeasured((m) => (same(m, next) ? m : next));
    return next;
  };
  useLayoutEffect(() => {
    measure();
  });
  // Re-measure each frame until the track and the slot are found and settled (keepMeasuring): the panel's layout
  // takes several frames, and nothing may render the row again (a paused game).
  useEffect(() => {
    let id = 0;
    let frames = 0;
    let stable = 0;
    let last: Measured | null = null;
    const tick = () => {
      const next = measure();
      stable = last && next && same(last, next) ? stable + 1 : 0;
      last = next;
      frames++;
      if (keepMeasuring(frames, stable, !!next?.track && !!next?.slot)) id = requestAnimationFrame(tick);
    };
    id = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(id);
  });
  return (
    <div ref={ref} className={[styles.frame, className].filter(Boolean).join(" ")}>
      {children}
      <SliderMarker track={measured.track} fraction={fraction} />
      {measured.slot && (
        <div className={styles.icon} style={{ right: `${measured.slot.right}px`, width: `${measured.slot.width}px` }}>
          <ResetButton {...reset} />
        </div>
      )}
    </div>
  );
};
