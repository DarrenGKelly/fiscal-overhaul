// The mapping between a density row's offset (percentage points the building pays on top of its own rate, ±maxOffset)
// and what its vanilla TaxSlider draws. The rows sit in the same columns as vanilla's education/resource rows, which
// show an absolute rate ("11 %"), so the density row shows the zone rate plus its offset on a slider spanning
// zone rate ± maxOffset. One switch: false draws the bare offset ("2 %") on a -max..+max slider instead.

/** Provisional: draw the zone rate + offset, like the education/resource rows draw their absolute rate. */
export const DENSITY_ROW_ABSOLUTE_RATE = true;

export interface DensitySliderValues {
  /** The value the slider's thumb and % column show. */
  rate: number;
  min: number;
  max: number;
}

function baseRate(zoneRate: number | null | undefined, absolute: boolean): number {
  return absolute && zoneRate != null ? zoneRate : 0;
}

/** What a density row's TaxSlider draws for an offset. A null zone rate (not bound yet) draws the bare offset. */
export function densitySliderValues(
  offset: number,
  maxOffset: number,
  zoneRate: number | null | undefined,
  absolute = DENSITY_ROW_ABSOLUTE_RATE,
): DensitySliderValues {
  const base = baseRate(zoneRate, absolute);
  return { rate: base + offset, min: base - maxOffset, max: base + maxOffset };
}

/** The offset to store for a value the slider reports: back off the base, round (the C# trigger takes an int), clamp to ±maxOffset. */
export function offsetFromSlider(value: number, maxOffset: number, zoneRate: number | null | undefined, absolute = DENSITY_ROW_ABSOLUTE_RATE): number {
  const offset = Math.round(value - baseRate(zoneRate, absolute));
  return Math.max(-maxOffset, Math.min(maxOffset, offset)) || 0;
}
