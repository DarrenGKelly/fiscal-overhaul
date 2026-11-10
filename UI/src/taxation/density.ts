import { localize, Translate, UiKey } from "../l10n";
// Mapping between the game's tax area indices and the mod's density tax table (Mod/Logic/DensityTaxTable.cs).

/** Vanilla TaxAreaType: City = 0, Residential = 1, Commercial = 2, Industrial = 3, Office = 4. */
export function zoneForAreaIndex(areaIndex: number): number {
  return areaIndex >= 1 && areaIndex <= 4 ? areaIndex - 1 : -1;
}

/**
 * The density cells a tax group shows rows for: its zone's cells, or none for the city-wide group, a locked zone,
 * no data yet, or density taxes switched off in Options.
 */
export function cellsForArea<T extends { zone: number }>(cells: T[] | null | undefined, areaIndex: number, locked: boolean, enabled: boolean): T[] {
  const zone = zoneForAreaIndex(areaIndex);
  if (!enabled || locked || zone < 0 || !cells) return [];
  return cells.filter((c) => c.zone === zone);
}

const DENSITY_KEYS: UiKey[] = ["DensityLow", "DensityMedium", "DensityHigh"];

/** "Low density", "Medium density", "High density"; "Density 7" for one the table doesn't name. */
export function densityLabel(density: number, translate: Translate | null): string {
  const key = DENSITY_KEYS[density];
  return key ? localize(translate, key) : localize(translate, "DensityOther", { NUMBER: density });
}

/**
 * The expand/collapse arrow, as the vanilla foldout draws it: a Media/Glyphs icon (the game's font, Overpass, has no
 * arrow glyphs and shows a tofu box).
 */
export function arrowFor(open: boolean): string {
  return open ? "Media/Glyphs/ThickStrokeArrowDown.svg" : "Media/Glyphs/ThickStrokeArrowRight.svg";
}

// Totals. Vanilla's zone totals (each group's header slider income, the `areaTaxIncomes` binding) and ESTIMATED TOTAL
// (`taxIncome`, the sum of the four zones) are estimates from the vanilla rates alone; the density offsets' extra
// revenue is added on top in the UI (density-totals.tsx). The values still go through vanilla's NumberField, so the
// sign and money format are vanilla's.

interface EstimateCell {
  zone: number;
  offset: number;
  estimate: number;
}

interface AreaLike {
  index: number;
  locked: boolean;
}

/**
 * The density tax's monthly revenue in one tax area: the sum of its rows' estimates (each payer's own density part as
 * charged, so a cell at the city's 0 still counts what its districts' rates bring; a subsidy lowers it). 0 while
 * density taxes are off, for the city-wide area, a locked zone, or with no payer paying density tax.
 */
export function densityExtraForArea(cells: EstimateCell[] | null | undefined, areaIndex: number, locked: boolean, enabled: boolean): number {
  let sum = 0;
  for (const c of cellsForArea(cells, areaIndex, locked, enabled)) sum += c.estimate;
  return sum;
}

/** The extra for ESTIMATED TOTAL: the sum of every area's {@link densityExtraForArea}, so it matches the zone totals. */
export function densityExtraTotal(cells: EstimateCell[] | null | undefined, areas: AreaLike[] | null | undefined, enabled: boolean): number {
  let sum = 0;
  for (const a of areas ?? []) sum += densityExtraForArea(cells, a.index, a.locked, enabled);
  return sum;
}

/**
 * What district and Outside income-tax overrides add to a tax area's vanilla total (the densityTaxes binding's
 * districtParts, per zone): booked by the city as tax income whatever the density switch. 0 for the city-wide area, a
 * locked zone, or no data.
 */
export function districtPartForArea(parts: number[] | null | undefined, areaIndex: number, locked: boolean): number {
  const zone = zoneForAreaIndex(areaIndex);
  if (locked || zone < 0) return 0;
  return parts?.[zone] ?? 0;
}

/** A zone's whole extra on top of vanilla's total: its density tax plus its district part. */
export function zoneExtra(
  cells: EstimateCell[] | null | undefined,
  districtParts: number[] | null | undefined,
  areaIndex: number,
  locked: boolean,
  enabled: boolean,
): number {
  return densityExtraForArea(cells, areaIndex, locked, enabled) + districtPartForArea(districtParts, areaIndex, locked);
}

/** ESTIMATED TOTAL's extra: every area's {@link zoneExtra}, so it matches the zone totals. */
export function totalExtra(
  cells: EstimateCell[] | null | undefined,
  districtParts: number[] | null | undefined,
  areas: AreaLike[] | null | undefined,
  enabled: boolean,
): number {
  let sum = 0;
  for (const a of areas ?? []) sum += zoneExtra(cells, districtParts, a.index, a.locked, enabled);
  return sum;
}

/** A zone group's total: the header (primary) slider's income plus the zone's extra. Item sliders are left alone. */
export function zoneTotalIncome(income: number, primary: boolean | undefined, extra: number | null | undefined): number {
  return primary && extra ? income + extra : income;
}

/**
 * The value a vanilla NumberField draws: ESTIMATED TOTAL (the field whose class is the Taxation page's `totalValue`)
 * plus the extra; any other money field as it is.
 */
export function estimatedTotalValue(value: number, className: string | undefined, totalClass: string | undefined, extra: number | null | undefined): number {
  return extra && totalClass && className === totalClass ? value + extra : value;
}
