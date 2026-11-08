// Pure logic for per-district tax rates on the Taxation tab. No game imports, so vitest runs it in Node.
import { CITY_KEY, districtOptions, DistrictLike, DistrictOption, effectiveKey, OUTSIDE_KEY } from "../district/district-select";
import { Translate } from "../l10n";

/**
 * The Taxation tab's selector choices: the city (cityLabel), then each district. No Outside Districts (an adjustment
 * is per district; the area outside every district pays the city rate). No districts: only the city.
 */
export function taxDistrictOptions(districts: DistrictLike[], translate: Translate | null = null, cityName = ""): DistrictOption[] {
  return districtOptions(districts, translate, cityName).filter((o) => o.key !== OUTSIDE_KEY);
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

// A row's reset button and slider marker (common/slider-reset.ts): the value the row goes back to.

/** Vanilla's default rates (C# TaxRateModel.DefaultZoneRate ..., sent in the taxRates binding). */
export interface TaxDefaultsLike {
  zoneRate: number;
  itemOffset: number;
  density: number;
}

/** TaxSystem.SetDefaults: the base rate 10, every offset 0. Used until the binding arrives. */
export const VANILLA_TAX_DEFAULTS: TaxDefaultsLike = { zoneRate: 10, itemOffset: 0, density: 0 };

/** A row's reset: the value it goes back to, whether it is away from it (the button shows), and which reset it is. */
export interface RowReset {
  target: number;
  differs: boolean;
  kind: "default" | "city";
}

/** The override's stored key: a zone rate is stored under key 0. */
const storedKey = (target: RowTarget) => (target.type === TYPE_ZONE_RATE ? 0 : target.key);

/**
 * What a row's reset does. Entire City ("default"): a zone row back to vanilla's default rate, an education/resource
 * row to its default offset from the zone's current rate (provisional: offset 0, so it follows the zone slider), a
 * density row to 0; `cityRate` is the row's city value. A district ("city"): the row's own override removed, so the
 * target is what the district has without it (the city's value; for a level/resource row under a district zone
 * override, the district's zone rate plus the city's offset), and it differs only while that override is stored.
 * `cityRate`/`cityZoneRate` as in rowRate; min/max the row's limits (the target is kept inside them).
 */
export function rowReset(
  target: RowTarget,
  area: string,
  rows: OverrideRow[] | null | undefined,
  cityRate: number,
  cityZoneRate: number,
  min: number,
  max: number,
  defaults: TaxDefaultsLike = VANILLA_TAX_DEFAULTS,
): RowReset {
  const clamp = (v: number) => Math.max(min, Math.min(max, v));
  if (!area || area === CITY_KEY) {
    let value: number;
    switch (target.type) {
      case TYPE_ZONE_RATE:
        value = clamp(defaults.zoneRate);
        break;
      case TYPE_RESOURCE_RATE:
        value = clamp(cityZoneRate + defaults.itemOffset);
        break;
      default:
        value = clamp(defaults.density);
    }
    return { target: value, differs: cityRate !== value, kind: "default" };
  }
  const key = storedKey(target);
  const own = districtOverride(rows, area, target.type, target.zone, key);
  const without = rows?.filter((r) => !(r.area === area && r.type === target.type && r.zone === target.zone && r.key === key));
  const value = rowRate(target, area, without, cityRate, cityZoneRate, min, max).rate;
  return { target: value, differs: own !== undefined, kind: "city" };
}

// A district view's estimates (C# DistrictTaxEstimateUISystem). Until they are for the shown district, every one is 0.

export interface DistrictEstimatesLike {
  area: string;
  items: { zone: number; key: number; estimate: number }[];
  density: { zone: number; density: number; estimate: number }[];
  zones: number[];
  total: number;
}

function forArea(data: DistrictEstimatesLike | null | undefined, area: string): DistrictEstimatesLike | null {
  return data && area && area !== CITY_KEY && data.area === area ? data : null;
}

/**
 * A row's estimate in a district view: a zone header (TYPE_ZONE_RATE) gets the zone's total (its rows and its density
 * rows), an education/resource row its own, a density row its own.
 */
export function districtRowEstimate(data: DistrictEstimatesLike | null | undefined, area: string, target: RowTarget): number {
  const d = forArea(data, area);
  if (!d) return 0;
  switch (target.type) {
    case TYPE_ZONE_RATE:
      return d.zones?.[target.zone] ?? 0;
    case TYPE_RESOURCE_RATE:
      return d.items?.find((r) => r.zone === target.zone && r.key === target.key)?.estimate ?? 0;
    case TYPE_DENSITY:
      return d.density?.find((c) => c.zone === target.zone && c.density === target.key)?.estimate ?? 0;
    default:
      return 0;
  }
}

/** ESTIMATED TOTAL in a district view: the district's total. */
export function districtTotalEstimate(data: DistrictEstimatesLike | null | undefined, area: string): number {
  return forArea(data, area)?.total ?? 0;
}