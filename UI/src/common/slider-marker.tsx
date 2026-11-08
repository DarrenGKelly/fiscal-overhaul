import { Box, markerLeft } from "./slider-reset";
import styles from "./slider-marker.module.scss";

// A small tick on a slider's track at the row's reset target (the default in the city view, the city's value in a
// district view), shared by the Taxation tab and (later) the Services tab. Positioned in px inside a
// position: relative frame from the track's measured box (slider-reset-frame.tsx).

/** The tick's width in px: the CSS draws it 3rem wide, but it is centred from the px box, so a nominal width will do. */
const NOMINAL_WIDTH_PX = 2;

export const SliderMarker = ({ track, fraction }: { track: Box | null; fraction: number | null }) => {
  const left = markerLeft(track, fraction, NOMINAL_WIDTH_PX);
  if (left === null || !track) return null;
  return <div className={styles.marker} style={{ left: `${left}px`, top: `${track.top}px`, height: `${track.height}px` }} />;
};
