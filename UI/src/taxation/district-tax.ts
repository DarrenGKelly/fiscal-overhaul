// Pure logic for per-district tax rates on the Taxation tab. No game imports, so vitest runs it in Node.
import { CITY_KEY, districtOptions, DistrictLike, DistrictOption, effectiveKey, OUTSIDE_KEY } from "../district/district-select";
import { localize, Translate } from "../l10n";

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

/** A district's adjustment for a zone (0 Residential .. 3 Office): its stored row, else 0. */
export function districtAdjustment(rows: { key: string; zone: number; value: number }[] | null | undefined, key: string, zone: number): number {
  return rows?.find((r) => r.key === key && r.zone === zone)?.value ?? 0;
}

/** "+5", "-3", "0": an adjustment with its sign. */
export function signedPoints(value: number): string {
  return value > 0 ? `+${value}` : `${value}`;
}

/** The District section's text: "+5% vs city". */
export function vsCityLabel(value: number, translate: Translate | null): string {
  return localize(translate, "DistrictVsCity", { VALUE: signedPoints(value) });
}

/**
 * Whether a TaxSlider is drawn disabled: as vanilla says (a locked zone), and also a zone group's header (primary)
 * slider in a district view, where it shows the city rate read-only.
 */
export function headerSliderDisabled(vanillaDisabled: boolean | undefined, primary: boolean | undefined, viewKey: string): boolean {
  return !!vanillaDisabled || (!!primary && viewKey !== CITY_KEY);
}

/** A slider's value as the trigger takes it: a whole number within ±cap. */
export function clampAdjustment(value: number, cap: number): number {
  const v = Math.round(value);
  return Math.max(-cap, Math.min(cap, v));
}
