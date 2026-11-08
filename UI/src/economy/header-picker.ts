// Pure logic for the Economy panel header: the one district picker (top left, beside the tabs) and the gap between
// the district tabs and the city-only tabs. No game imports, so vitest runs it in Node.
import { districtOptions, DistrictLike, DistrictOption, effectiveKey, showsDistrictSelector } from "../district/district-select";
import { Translate } from "../l10n";
import { showsTaxDistrictSelector, taxDistrictOptions, taxViewKey } from "../taxation/district-tax";
import { orderEconomyTabs } from "./tab-order";

/** Vanilla's economy tab enum (Dv in the game bundle). */
export const TAB = { Budget: 0, Loan: 1, Taxation: 2, Services: 3, Production: 4 } as const;

/** The tabs the district picker drives: Budget, Taxation, Services. Production and Loans are city-only. */
export function isDistrictTab(tab: number): boolean {
  return tab === TAB.Budget || tab === TAB.Taxation || tab === TAB.Services;
}

/**
 * The tabs (already in our order) with `divider` inserted before the first city-only tab that follows a district tab,
 * so [Budget | Taxation | Services] reads apart from [Production | Loans]. By tab id, never by position; a set with no
 * district tab, or no city-only tab after one, is returned as it is.
 */
export function withTabGroupDivider<T extends { props?: any }>(ordered: T[], divider: T): T[] {
  const i = ordered.findIndex((c, n) => n > 0 && !isDistrictTab(c?.props?.tab) && isDistrictTab(ordered[n - 1]?.props?.tab));
  if (i < 0) return ordered;
  return [...ordered.slice(0, i), divider, ...ordered.slice(i)];
}

/** What the header knows about the district features (the bindings DistrictListUISystem and the tabs' systems send). */
export interface DistrictFeatures {
  /** Options > Record district budgets (districtBudgetEnabled): the Budget tab's districts. */
  budgetOn: boolean;
  /** Per-district service budgets (districtServiceBudgetEnabled): the Services tab's districts. */
  servicesOn: boolean;
  /** The tax engine charges (taxRates.engineOn): the Taxation tab's districts. */
  taxEngineOn: boolean;
}

/**
 * The header picker for the selected tab: its choices and the choice it shows, or null where it is hidden (Production,
 * Loans, or a tab whose district feature is off). Each tab keeps its own rules: every one offers the city, the
 * districts and Outside Districts; Taxation shows only with a district and while the tax engine charges. A shared
 * choice the tab can't use (a deleted district) shows as the city, and is kept for the other tabs.
 */
export function headerPicker(
  tab: number,
  features: DistrictFeatures,
  districts: DistrictLike[],
  selected: string,
  translate: Translate | null = null,
  cityName = ""
): { options: DistrictOption[]; selected: string } | null {
  if (tab === TAB.Taxation) {
    const options = taxDistrictOptions(districts, translate, cityName);
    if (!showsTaxDistrictSelector(features.taxEngineOn, options)) return null;
    return { options, selected: taxViewKey(features.taxEngineOn, options, selected) };
  }
  const on = tab === TAB.Budget ? features.budgetOn : tab === TAB.Services ? features.servicesOn : false;
  const options = districtOptions(districts, translate, cityName);
  if (!showsDistrictSelector(on, options)) return null;
  return { options, selected: effectiveKey(options, selected) };
}

/**
 * Whether the header reserves the picker's place at all: while any district feature is on, so the tabs don't move
 * when the picker hides on another tab. Every feature off: no place, the vanilla header.
 */
export function reservesPickerSlot(features: DistrictFeatures): boolean {
  return features.budgetOn || features.servicesOn || features.taxEngineOn;
}

/**
 * The Economy tab bar's children: the picker (when given) first, then vanilla's tab elements, every one of them once and
 * unchanged, in our order, with the divider between the groups. The picker and the divider are styled so they take no
 * part in the row's flex sizing (economy-tab-bar.module.scss); the tabs stay vanilla's own elements.
 */
export function economyTabBarChildren<T extends { props?: any }>(tabs: T[], picker: T | null, divider: T): T[] {
  const ordered = withTabGroupDivider(orderEconomyTabs(tabs), divider);
  return picker ? [picker, ...ordered] : ordered;
}