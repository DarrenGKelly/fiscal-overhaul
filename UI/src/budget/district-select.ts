// Pure logic for the Budget tab's district selector. No game imports, so vitest runs it in Node.
import { BudgetHistory, BudgetItemLike, itemValue } from "../chart/chart-math";
import { localize, Translate } from "../l10n";

/** One district as the districts binding sends it (bindings.ts DistrictChoice). */
export interface DistrictLike {
  key: string;
  name: string;
}

/** A selector choice: its key (sent with setDistrict) and the text shown. */
export interface DistrictOption {
  key: string;
  label: string;
}

export const CITY_KEY = "";
export const OUTSIDE_KEY = "outside";

/**
 * Income / expense source indices a district can have: Mod/Logic/DistrictBudgetMath.cs DistrictIncomeSources and
 * DistrictExpenseSources (Game.City.IncomeSource / ExpenseSource values). Every other source is city-only.
 */
export const DISTRICT_INCOME_SOURCES: readonly number[] = [0, 1, 2, 11, 4, 13, 3, 6, 12, 9, 10];
export const DISTRICT_EXPENSE_SOURCES: readonly number[] = [0, 6, 7, 8, 5];

export type BudgetKind = "income" | "expense";

/**
 * A district's name as shown. NameSystem.GetRenderedLabelName gives a localization id (no spaces, dotted or
 * bracketed, e.g. "Assets.DISTRICT_NAME[3]") for a district the player hasn't named, or "": an id is translated, and
 * anything still blank or id-looking becomes "District <n>" (n = its 1-based place in the list).
 */
export function districtLabel(name: string, position: number, translate: Translate | null): string {
  let text = (name ?? "").trim();
  if (looksLikeId(text) && translate) text = (translate(text, null) ?? "").trim();
  if (text === "" || looksLikeId(text)) return localize(translate, "DistrictNumber", { NUMBER: position });
  return text;
}

function looksLikeId(text: string): boolean {
  return text !== "" && !/\s/.test(text) && /[.[\]]/.test(text) && /^[A-Za-z0-9_.:[\]-]+$/.test(text);
}

/**
 * Every selector choice in cycle order: Entire City, each district (the binding's order, by name), Outside Districts.
 * No districts: only Entire City (the selector still shows, its arrows and list staying on Entire City).
 */
export function districtOptions(districts: DistrictLike[], translate: Translate | null = null): DistrictOption[] {
  const city: DistrictOption = { key: CITY_KEY, label: localize(translate, "EntireCity") };
  if (districts.length === 0) return [city];
  return [
    city,
    ...districts.map((d, i) => ({ key: d.key, label: districtLabel(d.name, i + 1, translate) })),
    { key: OUTSIDE_KEY, label: localize(translate, "OutsideDistricts") },
  ];
}

/**
 * Whether the Budget tab shows the district selector: whenever district recording is on (the districtBudgetEnabled
 * binding), whatever the districts are, so a city without districts still shows Entire City and the view can be found.
 * Recording off is vanilla behaviour: no selector.
 */
export function showsDistrictSelector(recordingOn: boolean, options: { key: string }[]): boolean {
  return recordingOn && options.length > 0;
}

/**
 * Whether the list draws a divider line above option `index`: where the group changes, so Entire City, the districts
 * and Outside Districts read as three blocks. Never above the first option.
 */
export function dividerBefore(options: { key: string }[], index: number): boolean {
  if (index <= 0 || index >= options.length) return false;
  return optionGroup(options[index].key) !== optionGroup(options[index - 1].key);
}

function optionGroup(key: string): "city" | "district" | "outside" {
  return key === CITY_KEY ? "city" : key === OUTSIDE_KEY ? "outside" : "district";
}

/** The key one step forwards (1) or back (-1) from `currentKey`, wrapping both ways; an unknown key gives Entire City. */
export function cycle(options: { key: string }[], currentKey: string, dir: 1 | -1): string {
  const i = options.findIndex((o) => o.key === currentKey);
  if (i < 0 || options.length === 0) return CITY_KEY;
  return options[(i + dir + options.length) % options.length].key;
}

/** The selected key if it is still a choice, else Entire City (a district deleted, or recording switched off). */
export function effectiveKey(options: { key: string }[], key: string): string {
  return options.some((o) => o.key === key) ? key : CITY_KEY;
}

/** True when every source of the item is city-only (loan interest, imports, ...): the row hides for a district. */
export function isCityOnlyItem(item: { sources: { index: number }[] }, kind: BudgetKind): boolean {
  const allowed = kind === "income" ? DISTRICT_INCOME_SOURCES : DISTRICT_EXPENSE_SOURCES;
  return item.sources.every((s) => !allowed.includes(s.index));
}

/** True while the history binding carries a district (not Entire City). */
export function isDistrictView(history: BudgetHistory | null | undefined): boolean {
  return !!history && !!history.district;
}

/**
 * A row's value now, signed as vanilla shows it (expenses negative): Entire City reads the vanilla values array; a
 * district sums its sources in history.nowIncome / nowExpense (expenses positive there, so negated).
 */
export function rowValue(item: BudgetItemLike, values: number[], history: BudgetHistory | null | undefined, kind: BudgetKind): number {
  if (!isDistrictView(history)) return itemValue(item, values);
  const now = (kind === "income" ? history!.nowIncome : history!.nowExpense) ?? [];
  const v = itemValue(item, now);
  return kind === "income" ? v : -v;
}
