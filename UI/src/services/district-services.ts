// Pure logic for the Services tab's per-district budget (service-page.tsx). No game imports, so vitest runs it in Node.
import { CITY_KEY } from "../district/district-select";

/** One row of the districtServiceBudgets binding (DistrictListUISystem / DistrictServiceBudgetRows). */
export interface DistrictServiceBudget {
  /** The service prefab entity as "index:version" (vanilla serviceDetails' entity, {@link serviceKey}). */
  service: string;
  /** A district key from the districts binding, or "outside". */
  district: string;
  /** The area's override %, or 0 when it follows the city slider. */
  budget: number;
  /** The area's money upkeep for the service at its effective %. */
  upkeep: number;
}

/** The slider's range, vanilla's (BudgetSliderItem's min/max) and the store's (DistrictServiceBudgetTable). */
export const MIN_BUDGET = 50;
export const MAX_BUDGET = 150;

/** A service entity's key as the C# side writes and reads it: "index:version" (DistrictKey.Format). */
export function serviceKey(entity: { index: number; version: number } | null | undefined): string {
  return entity ? `${entity.index}:${entity.version}` : "";
}

/** Whether the selection is a district view (a district or Outside Districts), not Entire City. */
export function isDistrictView(districtKey: string): boolean {
  return (districtKey ?? CITY_KEY) !== CITY_KEY;
}

function rowFor(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string): DistrictServiceBudget | undefined {
  return (rows ?? []).find((r) => r.service === service && r.district === district);
}

/** Whether the area has its own % for the service (else it follows the city slider). */
export function isOverridden(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string): boolean {
  return (rowFor(rows, service, district)?.budget ?? 0) > 0;
}

/** The % the area's slider shows: its override, else the city slider's %. */
export function sliderValue(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string, cityPercent: number): number {
  const budget = rowFor(rows, service, district)?.budget ?? 0;
  return budget > 0 ? budget : cityPercent;
}

/** The area's upkeep for the service (0 when it has no such building). */
export function upkeepFor(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string): number {
  return rowFor(rows, service, district)?.upkeep ?? 0;
}

/** A slider value as the trigger takes it: a whole % within 50-150. */
export function clampBudget(value: number): number {
  if (!Number.isFinite(value)) return 100;
  return Math.min(MAX_BUDGET, Math.max(MIN_BUDGET, Math.round(value)));
}
