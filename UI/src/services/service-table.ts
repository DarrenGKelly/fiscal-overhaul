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
  /** Absent in older test data: 0. */
  incomeExports?: number;
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

/**
 * The fee sub-rows a service shows: every fee, so its revenue has a row in the Estimate column (a user request); only
 * an adjustable one has a slider (vanilla's rule, `hasFeeSlider`). None in a district view when hidden there.
 */
export function feeRows<F extends FeeLike>(fees: F[] | null | undefined, districtView: boolean, mode: "disabled" | "hidden" = DISTRICT_FEE_ROWS): F[] {
  if (districtView && mode === "hidden") return [];
  return fees ?? [];
}

/** Whether a fee row has a slider: only an adjustable fee (vanilla's detail filters the same way). */
export function hasFeeSlider(fee: FeeLike): boolean {
  return fee.adjustable;
}

/** A fee's revenue in the city view: what vanilla's detail lists as Fees and Export, internal plus exports. */
export function feeRevenue(fee: FeeLike): number {
  return fee.incomeInternal + (fee.incomeExports ?? 0);
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
 * A service's NET = fee income + exports − imports − upkeep: the footer adds these. City view: vanilla's own figure
 * (services[].budget). District view: the area's share of the fee income (by usage) minus its upkeep at its effective %.
 */
export function serviceNet(service: { key: string; budget: number }, district: string, data: DistrictData): number {
  if (!isDistrictView(district)) return service.budget;
  return districtServiceFeeIncome(data.fees, service.key, district) - upkeepFor(data.budgets, service.key, district);
}

/**
 * A service row's Estimate: its cost only (a user request), the fee revenue being on the fee rows below it. City view:
 * vanilla's NET less every fee's revenue, so upkeep and imports. District view: the area's upkeep at its effective %.
 * The service row and its fee rows add up to the service's NET.
 */
export function serviceCost(service: { key: string; budget: number; fees?: FeeLike[] | null }, district: string, data: DistrictData): number {
  if (isDistrictView(district)) return 0 - upkeepFor(data.budgets, service.key, district);
  return service.budget - (service.fees ?? []).reduce((sum, f) => sum + feeRevenue(f), 0);
}

/** A fee sub-row's Estimate: the fee's revenue, positive. District view: the area's share of the fee's income. */
export function feeRowEstimate(fee: FeeLike, service: string, district: string, data: DistrictData): number {
  return isDistrictView(district) ? districtFeeIncome(data.fees, service, district, fee.resource) : feeRevenue(fee);
}

/** A row of the table as drawn: a service row or one of its fee sub-rows. */
export interface TableRowInput {
  key: string;
  budget: number;
  fees: FeeLike[];
}

/** The Estimate column top to bottom, as drawn: each service's cost, then each fee row's revenue. */
export function visibleEstimates(services: TableRowInput[], district: string, data: DistrictData): number[] {
  const column: number[] = [];
  for (const s of services) {
    column.push(serviceCost(s, district, data));
    for (const f of feeRows(s.fees, isDistrictView(district))) column.push(feeRowEstimate(f, s.key, district, data));
  }
  return column;
}

/** The footer total: the sum of the services' NETs, which is the visible column's sum (a cost and its fee rows). */
export function estimateTotal(services: { key: string; budget: number }[], district: string, data: DistrictData): number {
  return services.reduce((sum, s) => sum + serviceNet(s, district, data), 0);
}

/**
 * The area the Services table shows (a user decision: the table is always the Services tab). With District service
 * budgets (BETA) on (and Simulation changes on), the header picker's choice; off, always the city view: no district
 * overrides, and the header offers no picker on Services (economy/header-picker.ts).
 */
export function tableDistrict(districtBudgetsOn: boolean, selected: string): string {
  return districtBudgetsOn ? selected ?? CITY_KEY : CITY_KEY;
}
