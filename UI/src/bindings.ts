import { bindValue, trigger } from "cs2/api";
import { BudgetHistory } from "chart/chart-math";
import { SoundName } from "sounds";

/** Plays a vanilla UI sound (sounds.ts SOUNDS), as vanilla does: the "audio" group's playSound trigger, next frame. */
export function playSound(sound: SoundName | null) {
  if (sound) requestAnimationFrame(() => trigger("audio", "playSound", sound, 1));
}

// Bindings published by the C# systems in Mod/Systems (group "fiscalOverhaul").
const GROUP = "fiscalOverhaul";

/** Options > Enable budget chart. Off: the Budget tab is the vanilla page. */
export const budgetChartEnabled$ = bindValue<boolean>(GROUP, "budgetChartEnabled", true);

export const history$ = bindValue<BudgetHistory | null>(GROUP, "history", null);

/** Months of history the chart shows; 0 = all. */
export function setSpan(months: number) {
  trigger(GROUP, "setSpan", months);
}

export interface DistrictChoice {
  key: string;
  name: string;
}

/** The city's districts by name (not Entire City / Outside: the UI adds those). */
export const districts$ = bindValue<DistrictChoice[]>(GROUP, "districts", []);

/** Options > Record district budgets (with the chart on). Off: no district selector, vanilla Budget rows only. */
export const districtBudgetEnabled$ = bindValue<boolean>(GROUP, "districtBudgetEnabled", false);

/** "" = Entire City, "outside", or a district key from districts$. */
export function setDistrict(key: string) {
  trigger(GROUP, "setDistrict", key);
}

export interface DensityTaxCell {
  zone: number;
  density: number;
  offset: number;
  estimate: number;
  payers: number;
}

export interface DensityTaxes {
  maxOffset: number;
  cells: DensityTaxCell[];
}

/** Options > Enable density taxes. Off: the Taxation tab has no density rows. */
export const densityTaxesEnabled$ = bindValue<boolean>(GROUP, "densityTaxesEnabled", false);

export const densityTaxes$ = bindValue<DensityTaxes | null>(GROUP, "densityTaxes", null);

export function setDensityTax(zone: number, density: number, offset: number) {
  trigger(GROUP, "setDensityTax", zone, density, offset);
}
