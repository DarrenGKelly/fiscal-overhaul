// Pure logic for the Services tab table (service-table-view.tsx): which rows and fee sub-rows show, each slider's
// value and reset target, the Estimate column and its total, in the city view and a district view. No game imports,
// so vitest runs it in Node.
import { DistrictServiceBudget, isDistrictView, isOverridden, sliderValue, upkeepFor } from "./district-services";
import { ResetKind } from "../common/slider-reset";
import { CITY_KEY } from "../district/district-select";

/** One service of vanilla's serviceBudget.services binding: `budget` is vanilla's NET (fee income − upkeep). */
export interface ServiceLike {
  entity: { index: number; version: number };
  name: string;
  icon: string;
  locked: boolean;
  budget: number;
}

/** A fee of vanilla serviceDetails(entity).fees (the fields the table reads). */
export interface FeeLike {
  resource: number;
  name: string;
  fee: number;
  min: number;
  max: number;
  adjustable: boolean;
  incomeInternal: number;
}

/** One row of the districtServiceFees binding (C# ServiceTableUISystem): an area's share of a fee's income. */
export interface DistrictServiceFee {
  service: string;
  district: string;
  /** Game.City.PlayerResource. */
  resource: number;
  income: number;
}

/** One row of the serviceFeeDefaults binding: vanilla's default fee for a resource (what resetService sets). */
export interface ServiceFeeDefault {
  resource: number;
  fee: number;
}

/** The budget slider's default (vanilla resetService sets 100). */
export const DEFAULT_BUDGET = 100;

/** Vanilla ServiceFeeSliderItem's slider range: the fee mapped onto 0..200 %. */
export const FEE_SLIDER_MAX = 200;

/**
 * A provisional choice (flagged in the hand-back): in a district view a fee row shows the city's fee read-only
 * (disabled) rather than being hidden. Fees are city-wide; per-district fees were deferred.
 */
export const DISTRICT_FEE_ROWS: "disabled" | "hidden" = "disabled";

/** The fee sub-rows a service shows: its adjustable fees (vanilla's rule), none in a district view when hidden there. */
export function feeRows<F extends FeeLike>(fees: F[] | null | undefined, districtView: boolean, mode: "disabled" | "hidden" = DISTRICT_FEE_ROWS): F[] {
  if (districtView && mode === "hidden") return [];
  return (fees ?? []).filter((f) => f.adjustable);
}

/** The fee as vanilla's slider shows it: 200 × (fee − min) / (max − min), 0 for an empty range. */
export function feeToSlider(fee: number, min: number, max: number): number {
  if (!(max > min)) return 0;
  return (FEE_SLIDER_MAX * (fee - min)) / (max - min);
}

/** The fee a slider value sets (vanilla: lerp(min, max, value / 200)), the value clamped to 0..200. */
export function feeFromSlider(value: number, min: number, max: number): number {
  const v = Number.isFinite(value) ? Math.min(FEE_SLIDER_MAX, Math.max(0, value)) : 0;
  return min + ((max - min) * v) / FEE_SLIDER_MAX;
}

/** Vanilla's default fee for a resource, or null when the binding has none. */
export function feeDefault(defaults: ServiceFeeDefault[] | null | undefined, resource: number): number | null {
  const row = (defaults ?? []).find((d) => d.resource === resource);
  return row ? row.fee : null;
}

/** A slider's reset: what the button does, where its marker sits (slider units) and whether the row differs from it. */
export interface SliderReset {
  kind: ResetKind;
  /** The marker's value on the slider; null: no marker. */
  target: number | null;
  /** The button shows (the row is off its target, or a district stores an override). */
  differs: boolean;
}

/** Rounding slack for comparing slider values (a fee comes back as a float). */
const EPSILON = 0.5;

/**
 * The budget slider's reset. City view: back to 100 % (vanilla's default). District view: clear the area's override,
 * so it follows the city slider; the marker at the city's %.
 */
export function budgetReset(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string, cityPercent: number): SliderReset {
  if (!isDistrictView(district)) return { kind: "default", target: DEFAULT_BUDGET, differs: Math.abs(cityPercent - DEFAULT_BUDGET) >= EPSILON };
  return { kind: "city", target: cityPercent, differs: isOverridden(rows, service, district) };
}

/** The % a service's budget slider shows: the city's, or in a district view the area's own (else the city's). */
export function budgetValue(rows: DistrictServiceBudget[] | null | undefined, service: string, district: string, cityPercent: number): number {
  return isDistrictView(district) ? sliderValue(rows, service, district, cityPercent) : cityPercent;
}

/**
 * A fee slider's reset. City view: back to vanilla's default fee (no marker or button when its default is unknown).
 * District view: fees are city-wide, so the row is read-only: no button, and no marker.
 */
export function feeReset(fee: FeeLike, defaultFee: number | null, district: string): SliderReset {
  if (isDistrictView(district) || defaultFee === null) return { kind: isDistrictView(district) ? "city" : "default", target: null, differs: false };
  const target = feeToSlider(defaultFee, fee.min, fee.max);
  return { kind: "default", target, differs: Math.abs(feeToSlider(fee.fee, fee.min, fee.max) - target) >= EPSILON };
}

/** The area's share of one fee's income (0 with none). */
export function districtFeeIncome(fees: DistrictServiceFee[] | null | undefined, service: string, district: string, resource: number): number {
  return (fees ?? []).find((f) => f.service === service && f.district === district && f.resource === resource)?.income ?? 0;
}

/** The area's share of all a service's fee income. */
export function districtServiceFeeIncome(fees: DistrictServiceFee[] | null | undefined, service: string, district: string): number {
  return (fees ?? []).filter((f) => f.service === service && f.district === district).reduce((sum, f) => sum + f.income, 0);
}

/** The district data the Estimate column reads. */
export interface DistrictData {
  budgets: DistrictServiceBudget[] | null | undefined;
  fees: DistrictServiceFee[] | null | undefined;
}

/**
 * A service row's Estimate: NET = fee income − upkeep. City view: vanilla's own figure (services[].budget). District
 * view: the area's share of the fee income (by usage) minus its upkeep at its effective %.
 */
export function serviceEstimate(service: { key: string; budget: number }, district: string, data: DistrictData): number {
  if (!isDistrictView(district)) return service.budget;
  return districtServiceFeeIncome(data.fees, service.key, district) - upkeepFor(data.budgets, service.key, district);
}

/** A fee sub-row's Estimate: its fee income (vanilla incomeInternal), or the area's share of it. Part of its service's NET. */
export function feeEstimate(fee: FeeLike, service: string, district: string, fees: DistrictServiceFee[] | null | undefined): number {
  return isDistrictView(district) ? districtFeeIncome(fees, service, district, fee.resource) : fee.incomeInternal;
}

/** The footer total: the sum of the service rows' Estimates (the fee sub-rows are parts of them, not added again). */
export function estimateTotal(services: { key: string; budget: number }[], district: string, data: DistrictData): number {
  return services.reduce((sum, s) => sum + serviceEstimate(s, district, data), 0);
}

/**
 * The area the Services table shows (a user decision: the table is always the Services tab). With District service
 * budgets (BETA) on (and Simulation changes on), the header picker's choice; off, always the city view: no district
 * overrides, and the header offers no picker on Services (economy/header-picker.ts).
 */
export function tableDistrict(districtBudgetsOn: boolean, selected: string): string {
  return districtBudgetsOn ? selected ?? CITY_KEY : CITY_KEY;
}
