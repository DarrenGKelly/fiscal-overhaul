import { bindValue, trigger } from "cs2/api";
import { BudgetHistory } from "chart/chart-math";
import { SoundName } from "sounds";
import { DistrictServiceBudget } from "services/district-services";

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

/** The city's districts by name (not Entire City / Outside: the UI adds those); [] while no district feature is on. */
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
}

export interface DensityTaxes {
  minRate: number;
  maxRate: number;
  cells: DensityTaxCell[];
}

/** Options > Enable density taxes. Off: the Taxation tab has no density rows. */
export const densityTaxesEnabled$ = bindValue<boolean>(GROUP, "densityTaxesEnabled", false);

export const densityTaxes$ = bindValue<DensityTaxes | null>(GROUP, "densityTaxes", null);

export function setDensityTax(zone: number, density: number, offset: number) {
  trigger(GROUP, "setDensityTax", zone, density, offset);
}

/**
 * Why the Taxation tab warns that taxes are the game's own (C# TaxEngineWarning): 0 none, 1 untested game version,
 * 2 untested version but forced on, 3 incompatible. See taxation/engine-warning.ts.
 */
export const taxEngineWarning$ = bindValue<number>(GROUP, "taxEngineWarning", 0);

/**
 * One district override (C# TaxRatesUISystem): the district's own value for a (type, zone, key), as stored: a full
 * zone rate (TAX_TYPE_ZONE_RATE), an offset from the zone rate (TAX_TYPE_RESOURCE_RATE, like vanilla), a density tax
 * rate (TAX_TYPE_DENSITY). A value with no row follows the city.
 */
export interface DistrictTaxRate {
  /** The district's key ("index:version"). It changes on every load: compare with districts$ keys, never cache it. */
  area: string;
  type: number;
  /** 0 Residential .. 3 Office. */
  zone: number;
  /** Density 0..2, education level 0..4 (Residential), resource index (other zones); 0 for a zone rate. */
  key: number;
  value: number;
}

export interface TaxRates {
  /** The tax engine charges (Simulation changes on, guard happy): district rates take effect. */
  engineOn: boolean;
  /** Every stored district override. */
  districts: DistrictTaxRate[];
  /** DemandParameterData.m_TaxEffect, the weights of vanilla's demand tax factor (x residential, y commercial, z industrial/office). */
  taxEffect: { x: number; y: number; z: number };
}

/** Vanilla's city-wide zone demands, 0..1 (Game.UI.InGame.CityInfoUISystem, group "cityInfo"). */
export const cityDemand$ = {
  resLow: bindValue<number>("cityInfo", "residentialLowDemand", 0),
  resMed: bindValue<number>("cityInfo", "residentialMediumDemand", 0),
  resHigh: bindValue<number>("cityInfo", "residentialHighDemand", 0),
  commercial: bindValue<number>("cityInfo", "commercialDemand", 0),
  industrial: bindValue<number>("cityInfo", "industrialDemand", 0),
  office: bindValue<number>("cityInfo", "officeDemand", 0),
};

export const taxRates$ = bindValue<TaxRates | null>(GROUP, "taxRates", null);

/** C# TaxType: Density = 0 (1 = the retired ±adjustment), ZoneRate = 2, ResourceRate = 3 (education level or resource). */
export const TAX_TYPE_DENSITY = 0;
export const TAX_TYPE_ZONE_RATE = 2;
export const TAX_TYPE_RESOURCE_RATE = 3;

/**
 * Sets a value as its slider shows it: type (TAX_TYPE_*), zone (0..3), key, area ("" = the city: density only; else a
 * district key), value (a full rate; clamped, and turned into vanilla's offset form, in C#). Setting a district's value
 * back to the city's makes it follow the city again. The trigger takes key and area as one "key@area" string.
 */
export function setTaxRate(type: number, zone: number, key: number, area: string, value: number) {
  trigger(GROUP, "setTaxRate", type, zone, area ? `${key}@${area}` : `${key}`, value);
}

/** Options > District service budgets (BETA) with Simulation changes on. Off: the Services tab is vanilla. */
export const districtServiceBudgetEnabled$ = bindValue<boolean>(GROUP, "districtServiceBudgetEnabled", false);

/** Each (service, area) with buildings or an override: its override % (0 = city) and its upkeep. */
export const districtServiceBudgets$ = bindValue<DistrictServiceBudget[]>(GROUP, "districtServiceBudgets", []);

/** Sets an area's % for a service (50-150), or 0 to follow the city slider again. districtKey: a district key or "outside". */
export function setDistrictServiceBudget(serviceKey: string, districtKey: string, budget: number) {
  trigger(GROUP, "setDistrictServiceBudget", serviceKey, districtKey, budget);
}

/** "Use city rates": removes every override of the district. */
export function resetDistrictTaxRates(area: string) {
  trigger(GROUP, "resetDistrictTaxRates", area);
}