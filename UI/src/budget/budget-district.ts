// Budget-only district helpers: which rows and sources a district view keeps. No game imports.
import { BudgetHistory, BudgetItemLike, itemValue } from "../chart/chart-math";

/**
 * Income / expense source indices a district can have: Mod/Logic/DistrictBudgetMath.cs DistrictIncomeSources and
 * DistrictExpenseSources (Game.City.IncomeSource / ExpenseSource values). Every other source is city-only.
 */
export const DISTRICT_INCOME_SOURCES: readonly number[] = [0, 1, 2, 11, 4, 13, 3, 6, 12, 9, 10];
export const DISTRICT_EXPENSE_SOURCES: readonly number[] = [0, 6, 7, 8, 5];

export type BudgetKind = "income" | "expense";

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
