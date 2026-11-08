// Pure logic for per-district tax rates on the Taxation tab. No game imports, so vitest runs it in Node.
import { CITY_KEY, districtOptions, DistrictLike, DistrictOption, effectiveKey, OUTSIDE_KEY } from "../district/district-select";
import { Translate } from "../l10n";

/**
 * The Taxation tab's selector choices: Entire City, then each district. No Outside Districts (an adjustment is per
 * district; the area outside every district pays the city rate). No districts: only Entire City.
 */
export function taxDistrictOptions(districts: DistrictLike[], translate: Translate | null = null): DistrictOption[] {
  return districtOptions(districts, translate).filter((o) => o.key !== OUTSIDE_KEY);
}

/**
 * Whether the Taxation tab shows the district selector: only while the tax engine charges (district adjustments do
 * nothing otherwise, and the tab stays vanilla) and the city has at least one district.
 */
export function showsTaxDistrictSelector(engineOn: boolean, options: { key: string }[]): boolean {
  return engineOn && options.some((o) => o.key !== CITY_KEY);
}

/**
 * The district the tab shows: the shared choice when it is one of this tab's districts and the selector shows, else
 * Entire City ("" = the city view, today's layout). Outside Districts picked on the Budget tab reads as Entire City here.
 */
export function taxViewKey(engineOn: boolean, options: { key: string }[], selected: string): string {
  if (!showsTaxDistrictSelector(engineOn, options)) return CITY_KEY;
  return effectiveKey(options, selected);
}

// The district view's values. A district may set its own value for any rate (C# TaxRateModel): its zone rate, each
// education-level/resource rate (stored like vanilla, as an offset from the zone rate), each density tax rate. One it
// doesn't set follows the city. These mirror the C# maths so the sliders show what the engine charges.

/** One stored district override, as the taxRates binding sends it (bindings.ts DistrictTaxRate). */
export interface OverrideRow {
  area: string;
  type: number;
  zone: number;
  key: number;
  value: number;
}

/** C# TaxType. */
export const TYPE_DENSITY = 0;
export const TYPE_ZONE_RATE = 2;
export const TYPE_RESOURCE_RATE = 3;

/** The district's stored value for (type, zone, key), or undefined: it follows the city. */
export function districtOverride(rows: OverrideRow[] | null | undefined, area: string, type: number, zone: number, key: number): number | undefined {
  if (!area || area === CITY_KEY) return undefined;
  return rows?.find((r) => r.area === area && r.type === type && r.zone === zone && r.key === key)?.value;
}

/** Whether the district has any override ("Use city rates" has something to reset). */
export function hasDistrictOverrides(rows: OverrideRow[] | null | undefined, area: string): boolean {
  return !!area && area !== CITY_KEY && !!rows?.some((r) => r.area === area);
}

/** The district's zone rate: its own, else the city's. */
export function districtZoneRate(rows: OverrideRow[] | null | undefined, area: string, zone: number, cityZoneRate: number): number {
  return districtOverride(rows, area, TYPE_ZONE_RATE, zone, 0) ?? cityZoneRate;
}

/**
 * The district's education-level or resource rate: (its zone rate) + (its offset, else the city's), kept in vanilla's
 * limits; exactly the city's rate when it sets neither.
 */
export function districtItemRate(
  rows: OverrideRow[] | null | undefined,
  area: string,
  zone: number,
  key: number,
  cityZoneRate: number,
  cityItemRate: number,
  min: number,
  max: number,
): number {
  const zoneRate = districtOverride(rows, area, TYPE_ZONE_RATE, zone, 0);
  const offset = districtOverride(rows, area, TYPE_RESOURCE_RATE, zone, key);
  if (zoneRate === undefined && offset === undefined) return cityItemRate;
  const full = (zoneRate ?? cityZoneRate) + (offset ?? cityItemRate - cityZoneRate);
  return Math.max(min, Math.min(max, full));
}

/** The district's density tax rate for a cell: its own, else the city's. */
export function districtDensityRate(rows: OverrideRow[] | null | undefined, area: string, zone: number, density: number, cityRate: number): number {
  return districtOverride(rows, area, TYPE_DENSITY, zone, density) ?? cityRate;
}

/** What a slider shows: the rate, and whether it differs from the city's (the row is marked as an override). */
export interface ShownRate {
  rate: number;
  overridden: boolean;
}

export function shown(rate: number, cityRate: number): ShownRate {
  return { rate, overridden: rate !== cityRate };
}

/** How many of a zone's density cells differ from the city's in a district (the Density tax header's "N overrides"). */
export function densityOverrideCount(
  rows: OverrideRow[] | null | undefined,
  area: string,
  cells: { zone: number; density: number; offset: number }[],
): number {
  return cells.filter((c) => districtDensityRate(rows, area, c.zone, c.density, c.offset) !== c.offset).length;
}
/** Which value a slider row sets: a zone's header (zone rate), an education/resource row, or a density row. */
export interface RowTarget {
  type: number;
  zone: number;
  key: number;
}

/**
 * What a row's slider shows in a view. Entire City: the city's value, never marked. A district: its own value (else
 * the city's), marked when it differs from the city's. `cityRate` is what the row shows for the city (vanilla's
 * slider value, or the city's density rate); `cityZoneRate` the zone's city rate; min/max vanilla's limits for the row.
 */
export function rowRate(
  target: RowTarget,
  area: string,
  rows: OverrideRow[] | null | undefined,
  cityRate: number,
  cityZoneRate: number,
  min: number,
  max: number,
): ShownRate {
  if (!area || area === CITY_KEY) return shown(cityRate, cityRate);
  switch (target.type) {
    case TYPE_ZONE_RATE:
      return shown(districtZoneRate(rows, area, target.zone, cityRate), cityRate);
    case TYPE_RESOURCE_RATE:
      return shown(districtItemRate(rows, area, target.zone, target.key, cityZoneRate, cityRate, min, max), cityRate);
    case TYPE_DENSITY:
      return shown(districtDensityRate(rows, area, target.zone, target.key, cityRate), cityRate);
    default:
      return shown(cityRate, cityRate);
  }
}