// Pure logic for the Budget tab's subcategory rows: each category (vanilla budget item) expands into one child row
// per source (vanilla BudgetItem.sources), each with its own chart line. No game imports, so vitest runs it in Node.
import { BudgetHistory, BudgetItemLike, Rgba, rgbaToCss, SeriesDef, sumSources } from "../chart/chart-math";
import { BudgetKind, DISTRICT_EXPENSE_SOURCES, DISTRICT_INCOME_SOURCES, isDistrictView, rowValue } from "./budget-district";

/**
 * Provisional: whether a category with a single (visible) source gets an expander. Off: its one child row would only
 * repeat the category's own row and line. Flip to true to give every category a drop-down.
 */
export const EXPAND_SINGLE_SOURCE = false;

/** The key of a category row and its line: `income:<itemId>` / `expense:<itemId>` (buildSeries' keys). */
export function itemKey(kind: BudgetKind, itemId: string): string {
  return `${kind}:${itemId}`;
}

/** The key of a subcategory row and its line: `income:<itemId>:<sourceId>`. */
export function childKey(kind: BudgetKind, itemId: string, sourceId: string): string {
  return `${kind}:${itemId}:${sourceId}`;
}

/** The vanilla localization id of a subcategory's name (what vanilla's BudgetItemDetail shows). */
export function subItemLocaleId(sourceId: string): string {
  return `EconomyPanel.BUDGET_SUB_ITEM[${sourceId}]`;
}

/**
 * The item's sources that get a child row: all of them for Entire City; in a district view only the sources a district
 * records (budget-district DISTRICT_*_SOURCES), the same rule that hides city-only items.
 */
export function visibleSources(item: BudgetItemLike, kind: BudgetKind, inDistrict: boolean): { id: string; index: number }[] {
  if (!inDistrict) return item.sources;
  const allowed = kind === "income" ? DISTRICT_INCOME_SOURCES : DISTRICT_EXPENSE_SOURCES;
  return item.sources.filter((s) => allowed.includes(s.index));
}

/** Whether a category row gets an expander: it has child rows to show (more than one, unless EXPAND_SINGLE_SOURCE). */
export function hasExpander(item: BudgetItemLike, kind: BudgetKind, inDistrict: boolean, expandSingle = EXPAND_SINGLE_SOURCE): boolean {
  const n = visibleSources(item, kind, inDistrict).length;
  return expandSingle ? n >= 1 : n >= 2;
}

/** Mixes a colour channel towards white (t > 0) or black (t < 0) by |t|. */
function shade(c: Rgba, t: number): Rgba {
  const mix = (v: number) => (t >= 0 ? v + (1 - v) * t : v * (1 + t));
  return { r: mix(c.r), g: mix(c.g), b: mix(c.b), a: c.a };
}

/** The lightest and darkest a child line is shaded from its parent's colour (towards white / black). */
const LIGHTEST = 0.6;
const DARKEST = -0.45;

/**
 * The shade amount of child `index` of `count`: `count + 1` evenly spaced steps from DARKEST to LIGHTEST, minus the one
 * nearest 0 (the parent's own colour), so no child matches the parent and no two children match each other.
 */
export function childShade(index: number, count: number): number {
  const steps: number[] = [];
  for (let i = 0; i <= count; i++) steps.push(DARKEST + ((LIGHTEST - DARKEST) * i) / count);
  let nearest = 0;
  for (let i = 1; i < steps.length; i++) if (Math.abs(steps[i]) < Math.abs(steps[nearest])) nearest = i;
  steps.splice(nearest, 1);
  return steps[index] ?? 0;
}

/** Child `index` of `count`'s colour (CSS): the parent's colour shaded lighter or darker, distinct from each other. */
export function childColor(parent: Rgba, index: number, count: number): string {
  return rgbaToCss(shade(parent, childShade(index, count)));
}

/** One subcategory row: its key, the source, its colour and its value now (signed as vanilla shows it). */
export interface ChildRow {
  key: string;
  source: { id: string; index: number };
  color: string;
  value: number;
}

/** The child rows of a category, in the item's source order. Values: rowValue's rule for a one-source item. */
export function childRows(
  item: BudgetItemLike,
  kind: BudgetKind,
  values: number[],
  history: BudgetHistory | null | undefined
): ChildRow[] {
  const sources = visibleSources(item, kind, isDistrictView(history));
  return sources.map((source, i) => ({
    key: childKey(kind, item.id, source.id),
    source,
    color: childColor(item.color, i, sources.length),
    value: rowValue({ ...item, sources: [source] }, values, history, kind),
  }));
}

/**
 * A line per subcategory of every category that has an expander: the source's own history series (expenses positive,
 * like buildSeries), coloured as its row. `sourceId` names it for the label.
 */
export function buildSourceSeries(history: BudgetHistory, items: BudgetItemLike[], kind: BudgetKind): SeriesDef[] {
  const n = history.monthsAgo.length;
  const set = kind === "income" ? history.income : history.expense;
  const inDistrict = isDistrictView(history);
  const out: SeriesDef[] = [];
  for (const item of items) {
    if (!item.active || !hasExpander(item, kind, inDistrict)) continue;
    const sources = visibleSources(item, kind, inDistrict);
    sources.forEach((source, i) =>
      out.push({
        key: childKey(kind, item.id, source.id),
        kind,
        itemId: item.id,
        sourceId: source.id,
        color: childColor(item.color, i, sources.length),
        values: sumSources([source], set, n),
      })
    );
  }
  return out;
}

/** The set with `key` added if it was missing, removed if it was there (a new set; the old one is untouched). */
export function toggled(set: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(set);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  return next;
}

/** The expander's arrow: vanilla's glyphs (the game's font has no arrow characters), down while expanded. */
export function expandArrow(open: boolean): string {
  return open ? "Media/Glyphs/ThickStrokeArrowDown.svg" : "Media/Glyphs/ThickStrokeArrowRight.svg";
}
