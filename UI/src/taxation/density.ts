import { formatMoney } from "../chart/chart-math";
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

/** The collapsed rows' summary: "no offsets", "1 offset set", "3 offsets set". */
export function offsetSummary(active: number, translate: Translate | null): string {
  if (active <= 0) return localize(translate, "NoOffsets");
  return localize(translate, active === 1 ? "OneOffsetSet" : "OffsetsSet", { COUNT: active });
}

/** A row's payer count, " · 1,234 payers", or nothing while it has none. */
export function payersSuffix(payers: number, translate: Translate | null): string {
  return payers > 0 ? ` · ${localize(translate, "Payers", { COUNT: formatCount(payers) })}` : "";
}

/**
 * The expand/collapse arrow, as the vanilla foldout draws it: a Media/Glyphs icon (the game's font, Overpass, has no
 * arrow glyphs and shows a tofu box).
 */
export function arrowFor(open: boolean): string {
  return open ? "Media/Glyphs/ThickStrokeArrowDown.svg" : "Media/Glyphs/ThickStrokeArrowRight.svg";
}

/** A count with thousands separators ("1,234,567"), not locale-dependent: Gameface's Intl is patchy (formatMoney's grouping). */
export function formatCount(n: number): string {
  return formatMoney(n).replace("\u00a2", "");
}
