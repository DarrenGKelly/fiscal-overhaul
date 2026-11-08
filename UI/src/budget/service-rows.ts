// Pure logic for the Service Upkeep category's per-service child rows: vanilla records service upkeep as one expense
// source, so the mod records the split per service (BudgetHistoryUISystem's history.services) and the category expands
// into one row and line per service. No game imports, so vitest runs it in Node.
import { BudgetHistory, BudgetItemLike, SeriesDef, ServiceUpkeepRow } from "../chart/chart-math";
import { childColor, childKey } from "./budget-rows";
import { BudgetKind } from "./district-select";

/** Game.City.ExpenseSource.ServiceUpkeep: the source the per-service rows split. */
export const SERVICE_UPKEEP_SOURCE = 5;

/** Whether the item is the one holding Service Upkeep (an expense item with that source). */
export function holdsServiceUpkeep(item: BudgetItemLike, kind: BudgetKind): boolean {
  return kind === "expense" && item.sources.some((s) => s.index === SERVICE_UPKEEP_SOURCE);
}

/** The services the history has a split for (none for an older history or no recorded data). */
export function historyServices(history: BudgetHistory | null | undefined): ServiceUpkeepRow[] {
  return history?.services ?? [];
}

/** Whether the item gets per-service child rows: it holds Service Upkeep and the history has services. */
export function hasServiceRows(item: BudgetItemLike, kind: BudgetKind, history: BudgetHistory | null | undefined): boolean {
  return holdsServiceUpkeep(item, kind) && historyServices(history).length > 0;
}

/** The key of a service's row and line: a child key whose source part is `service:<name>`. */
export function serviceKey(itemId: string, name: string): string {
  return childKey("expense", itemId, `service:${name}`);
}

/** Vanilla's localization id of a service's name (what its Services tab shows). */
export function serviceLocaleId(name: string): string {
  return `Services.NAME[${name}]`;
}

/** One service row: its key, the service, its colour and its value now (negative, as vanilla shows an expense). */
export interface ServiceChildRow {
  key: string;
  name: string;
  color: string;
  value: number;
}

/**
 * The item's service rows, in the history's order (vanilla's Services tab), coloured as child rows after
 * `precedingChildren` source rows; empty unless hasServiceRows. Value: the newest point, the live one.
 */
export function serviceRows(
  item: BudgetItemLike,
  kind: BudgetKind,
  history: BudgetHistory | null | undefined,
  precedingChildren = 0
): ServiceChildRow[] {
  if (!hasServiceRows(item, kind, history)) return [];
  const services = historyServices(history);
  const count = precedingChildren + services.length;
  return services.map((s, i) => ({
    key: serviceKey(item.id, s.name),
    name: s.name,
    color: childColor(item.color, precedingChildren + i, count),
    value: -(s.values[s.values.length - 1] ?? 0),
  }));
}

/** A line per service under every active item holding Service Upkeep, from the service's `start` (expenses positive). */
export function buildServiceSeries(
  history: BudgetHistory,
  items: BudgetItemLike[],
  precedingChildren: (item: BudgetItemLike) => number = () => 0
): SeriesDef[] {
  const out: SeriesDef[] = [];
  for (const item of items) {
    if (!item.active) continue;
    for (const row of serviceRows(item, "expense", history, precedingChildren(item))) {
      const service = historyServices(history).find((s) => s.name === row.name)!;
      out.push({
        key: row.key,
        kind: "expense",
        itemId: item.id,
        serviceName: row.name,
        color: row.color,
        values: service.values,
        start: service.start,
      });
    }
  }
  return out;
}
