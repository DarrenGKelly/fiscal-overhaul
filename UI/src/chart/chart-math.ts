// Pure chart maths for the Budget tab line chart. No game imports, so vitest can run it in Node.
import { localize, localizeVanilla, Translate } from "../l10n";

export interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

/** One budget item as the game describes it (cs2/bindings BudgetItem, minus the parts we don't use). */
export interface BudgetItemLike {
  id: string;
  color: Rgba;
  sources: { id: string; index: number }[];
  active: boolean;
}

/** What BudgetHistoryUISystem sends (group "fiscalOverhaul", binding "history"). */
export interface BudgetHistory {
  span: number;
  samples: number;
  monthsAgo: number[];
  income: number[][];
  expense: number[][];
  /** The selected district's key; "" or absent = Entire City. */
  district?: string;
  /** Live per-source values now in a district view (expenses positive); null for Entire City. */
  nowIncome?: number[] | null;
  nowExpense?: number[] | null;
}

export type SeriesKind = "income-total" | "income" | "expense-total" | "expense" | "balance";

export interface SeriesDef {
  key: string;
  kind: SeriesKind;
  /** Budget item id for item series; undefined for totals and balance. */
  itemId?: string;
  color: string;
  values: number[];
}

export const INCOME_TOTAL = "income-total";
export const EXPENSE_TOTAL = "expense-total";
export const BALANCE = "balance";

export const TOTAL_COLORS = {
  [INCOME_TOTAL]: "rgba(113, 241, 168, 1)",
  [EXPENSE_TOTAL]: "rgba(236, 72, 112, 1)",
  [BALANCE]: "rgba(132, 202, 255, 1)",
};

export function rgbaToCss(c: Rgba): string {
  const ch = (v: number) => Math.round(Math.max(0, Math.min(1, v)) * 255);
  return `rgba(${ch(c.r)}, ${ch(c.g)}, ${ch(c.b)}, ${Math.max(0, Math.min(1, c.a))})`;
}

/** Sums the history series of an item's sources, point by point. */
export function sumSources(sources: { index: number }[], set: number[][], length: number): number[] {
  const out = new Array<number>(length).fill(0);
  for (const s of sources) {
    const series = set[s.index];
    if (!series) continue;
    for (let i = 0; i < length; i++) out[i] += series[i] ?? 0;
  }
  return out;
}

/** An item's current value: the sum of its sources in the game's flat values array (vanilla getItemValue). */
export function itemValue(item: { sources: { index: number }[] }, values: number[]): number {
  return item.sources.reduce((total, s) => total + (values[s.index] ?? 0), 0);
}

function sumAll(set: number[][], length: number): number[] {
  return sumSources(set.map((_, index) => ({ index })), set, length);
}

/**
 * Every selectable line: revenue total, each active revenue item, expense total, each active expense item,
 * and the monthly balance. Expense lines are positive amounts so they sit on the same axis as revenue.
 */
export function buildSeries(history: BudgetHistory, incomeItems: BudgetItemLike[], expenseItems: BudgetItemLike[]): SeriesDef[] {
  const n = history.monthsAgo.length;
  const incomeTotal = sumAll(history.income, n);
  const expenseTotal = sumAll(history.expense, n);
  const series: SeriesDef[] = [
    { key: INCOME_TOTAL, kind: "income-total", color: TOTAL_COLORS[INCOME_TOTAL], values: incomeTotal },
  ];
  for (const item of incomeItems) {
    if (!item.active) continue;
    series.push({ key: `income:${item.id}`, kind: "income", itemId: item.id, color: rgbaToCss(item.color), values: sumSources(item.sources, history.income, n) });
  }
  series.push({ key: EXPENSE_TOTAL, kind: "expense-total", color: TOTAL_COLORS[EXPENSE_TOTAL], values: expenseTotal });
  for (const item of expenseItems) {
    if (!item.active) continue;
    series.push({ key: `expense:${item.id}`, kind: "expense", itemId: item.id, color: rgbaToCss(item.color), values: sumSources(item.sources, history.expense, n) });
  }
  series.push({ key: BALANCE, kind: "balance", color: TOTAL_COLORS[BALANCE], values: incomeTotal.map((v, i) => v - expenseTotal[i]) });
  return series;
}

/** Min and max over the given series, padded so lines don't touch the edges; always includes 0. */
export function valueRange(series: { values: number[] }[]): [number, number] {
  let min = 0;
  let max = 0;
  for (const s of series) {
    for (const v of s.values) {
      if (v < min) min = v;
      if (v > max) max = v;
    }
  }
  if (min === max) return [min - 1, max + 1];
  const pad = (max - min) * 0.05;
  return [min < 0 ? min - pad : 0, max + pad];
}

/** "Nice" round tick values covering [min, max], about `count` of them. */
export function niceTicks(min: number, max: number, count = 5): number[] {
  if (!(max > min) || count < 1) return [min];
  const rough = (max - min) / count;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const residual = rough / magnitude;
  const step = (residual > 5 ? 10 : residual > 2 ? 5 : residual > 1 ? 2 : 1) * magnitude;
  const ticks: number[] = [];
  for (let v = Math.ceil(min / step) * step; v <= max + step * 1e-9; v += step) {
    ticks.push(Math.abs(v) < step * 1e-9 ? 0 : v);
  }
  return ticks;
}

/** Index of the point whose x is closest to `x` (xs ascending). -1 for an empty list. */
export function nearestIndex(xs: number[], x: number): number {
  if (xs.length === 0) return -1;
  let lo = 0;
  let hi = xs.length - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (xs[mid] <= x) lo = mid;
    else hi = mid;
  }
  return Math.abs(xs[lo] - x) <= Math.abs(xs[hi] - x) ? lo : hi;
}

/** The series (among `series`) whose value at `index` is closest to `value`. */
export function nearestSeries<T extends { values: number[] }>(series: T[], index: number, value: number): T | undefined {
  let best: T | undefined;
  let bestDist = Infinity;
  for (const s of series) {
    const v = s.values[index];
    if (v === undefined) continue;
    const d = Math.abs(v - value);
    if (d < bestDist) {
      bestDist = d;
      best = s;
    }
  }
  return best;
}

/** Linear map from [d0, d1] to [r0, r1]. */
export function scale(d0: number, d1: number, r0: number, r1: number): (v: number) => number {
  const k = d1 === d0 ? 0 : (r1 - r0) / (d1 - d0);
  return (v) => r0 + (v - d0) * k;
}

/** "¢1,234,567" / "-¢12,345". Own formatter: the game's browser has patchy Intl support. */
export function formatMoney(value: number): string {
  const rounded = Math.round(value);
  const digits = Math.abs(rounded).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return `${rounded < 0 ? "-" : ""}¢${digits}`;
}

/**
 * "¢315,997/mo", as one string. Rendered as {formatMoney(v)}/mo, React makes two text nodes, and Gameface wraps
 * between them even under white-space: nowrap.
 */
export function formatPerMonth(value: number, translate: Translate | null): string {
  return localize(translate, "PerMonth", { VALUE: formatMoney(value) });
}

/** Short axis label: ¢1.2M, ¢350k, ¢900. */
export function formatMoneyShort(value: number): string {
  const abs = Math.abs(value);
  const sign = value < 0 ? "-" : "";
  if (abs >= 1e9) return `${sign}¢${trim(abs / 1e9)}B`;
  if (abs >= 1e6) return `${sign}¢${trim(abs / 1e6)}M`;
  if (abs >= 1e3) return `${sign}¢${trim(abs / 1e3)}k`;
  return `${sign}¢${Math.round(abs)}`;
}

function trim(v: number): string {
  return (Math.round(v * 10) / 10).toString();
}

/** "Now", "12 h ago", "6 months ago", "3 years ago": the time of a point, in months (32 samples = 1 month). */
export function formatMonthsAgo(monthsAgo: number, translate: Translate | null): string {
  const m = -monthsAgo;
  if (m < 1 / 64) return localizeVanilla(translate, "now");
  // One in-game month is one day on the clock, so part of a month reads as hours.
  if (m < 1) return localize(translate, "HoursAgo", { COUNT: Math.max(1, Math.round(m * 24)) });
  if (m < 24) return localize(translate, "MonthsAgo", { COUNT: trim(m) });
  return localize(translate, "YearsAgo", { COUNT: trim(m / 12) });
}

/** Weeks per in-game month, for the x axis (the game has no weeks; a quarter month reads naturally). */
export const WEEKS_PER_MONTH = 4;

/** X-axis tick steps in months: whole weeks, then whole months, then whole years. */
export const TIME_TICK_STEPS = [0.25, 0.5, 1, 2, 3, 6, 12, 24, 36, 60, 120, 240];

/**
 * X-axis ticks for a span reaching back `spanMonths` from now: multiples of the smallest step in
 * TIME_TICK_STEPS that gives at most `maxTicks` ticks, as negative month offsets, oldest first, ending at 0 (now).
 * Every tick is a whole number of weeks, months or years back, never a fraction.
 */
export function timeTicks(spanMonths: number, maxTicks = 6): number[] {
  const span = Math.max(0, spanMonths);
  const limit = Math.max(2, Math.floor(maxTicks));
  let step = TIME_TICK_STEPS[TIME_TICK_STEPS.length - 1];
  for (const s of TIME_TICK_STEPS) {
    if (Math.floor(span / s + 1e-9) + 1 <= limit) {
      step = s;
      break;
    }
  }
  const count = Math.floor(span / step + 1e-9);
  const ticks: number[] = [];
  for (let k = count; k >= 1; k--) ticks.push(-k * step);
  ticks.push(0);
  return ticks;
}

/** Axis label for a tick: "Now", "-3 wk", "-1 mo", "-1 mo 2 wk", "-2 yr". Rounds to the nearest week. */
export function formatMonthsAxis(monthsAgo: number, translate: Translate | null): string {
  const weeks = Math.round(-monthsAgo * WEEKS_PER_MONTH);
  if (weeks <= 0) return localizeVanilla(translate, "now");
  const months = Math.floor(weeks / WEEKS_PER_MONTH);
  const restWeeks = weeks % WEEKS_PER_MONTH;
  if (restWeeks === 0 && months >= 12 && months % 12 === 0) return localize(translate, "AxisYears", { COUNT: months / 12 });
  if (months === 0) return localize(translate, "AxisWeeks", { COUNT: restWeeks });
  return restWeeks === 0
    ? localize(translate, "AxisMonths", { COUNT: months })
    : localize(translate, "AxisMonthsWeeks", { MONTHS: months, WEEKS: restWeeks });
}

/**
 * Where the plot starts, in pixels: any inset (room for a panel over the chart's left side), then the widest y label,
 * then the gap before the plot. Sizes in rem are scaled by `rem` (pixels per rem); `labelWidth` is already in pixels.
 */
export function plotLeft(rem: number, labelWidth: number, gapRem: number, insetLeftRem = 0): number {
  return (Math.max(0, insetLeftRem) + gapRem) * rem + labelWidth;
}

/**
 * The span stats under the rows, labelled: now, average, lowest, highest and change over the points given (the
 * selected span), per month. None for an empty series.
 */
export function seriesStats(values: number[], translate: Translate | null): [string, number][] {
  const v = values;
  if (v.length === 0) return [];
  return [
    [localizeVanilla(translate, "now"), v[v.length - 1]],
    [localize(translate, "StatAverage"), v.reduce((a, b) => a + b, 0) / v.length],
    [localize(translate, "StatLowest"), Math.min(...v)],
    [localize(translate, "StatHighest"), Math.max(...v)],
    [localize(translate, "StatChange"), v[v.length - 1] - v[0]],
  ];
}

/** Time spans offered above the chart, in months. 0 = all history. */
export const SPAN_OPTIONS: number[] = [1, 3, 6, 12, 36, 0];

/** The span the chart opens on before the player picks one (3M). Mirrors BudgetHistoryMath.DefaultSpanMonths. */
export const DEFAULT_SPAN_MONTHS = 3;

/** A span button's label: "1M", "6M", "1Y", "3Y", "All". */
export function spanLabel(months: number, translate: Translate | null): string {
  if (months <= 0) return localize(translate, "SpanAll");
  if (months % 12 === 0) return localize(translate, "SpanYears", { COUNT: months / 12 });
  return localize(translate, "SpanMonths", { COUNT: months });
}
