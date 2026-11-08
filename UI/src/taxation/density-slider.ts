// What a density row's vanilla TaxSlider draws. Density tax is its own tax, charged on top of income tax (the zone and
// education/resource rates), so the row shows only its own rate ("3 %", "0 %" by default), never the zone rate plus it.

/** The density rate slider's range, in percentage points. The C# side clamps what it stores to its own cap. */
export const DENSITY_RATE_RANGE = { min: -5, max: 5 };

export interface DensitySliderValues {
  /** The value the slider's thumb and % column show: the density tax rate itself. */
  rate: number;
  min: number;
  max: number;
}

/** What a density row's TaxSlider draws for its density tax rate. */
export function densitySliderValues(rate: number, range = DENSITY_RATE_RANGE): DensitySliderValues {
  return { rate, min: range.min, max: range.max };
}

/** The rate to store for a value the slider reports: rounded (the C# trigger takes an int) and kept in the range. */
export function rateFromSlider(value: number, range = DENSITY_RATE_RANGE): number {
  return Math.max(range.min, Math.min(range.max, Math.round(value))) || 0;
}
