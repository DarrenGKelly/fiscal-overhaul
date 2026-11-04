import { useValue } from "cs2/api";
import { economyBudget } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { useCallback, useEffect, useMemo, useState } from "react";
import { history$, setSpan as sendSpan } from "bindings";
import {
  BALANCE,
  buildSeries,
  BudgetItemLike,
  EXPENSE_TOTAL,
  formatMoney,
  INCOME_TOTAL,
  rgbaToCss,
  SeriesDef,
  SPAN_OPTIONS,
  itemValue,
  TOTAL_COLORS,
} from "chart/chart-math";
import { ChartLine, LineChart } from "chart/line-chart";
import { BudgetItemDetail, NumberField } from "vanilla";
import styles from "./budget-page.module.scss";

/**
 * While the item pop-out shows, the chart's plot starts after it: the pop-out is 360rem wide at 6rem from the chart
 * panel's edge (budget-page.module.scss .detail-popout), the canvas starts 10rem in, plus a 12rem gap.
 */
const POPOUT_INSET_REM = 360 + 6 - 10 + 12;

// Kept outside the component so the choice survives switching tabs or closing the panel.
let rememberedEnabled = new Set<string>([INCOME_TOTAL, EXPENSE_TOTAL]);
let rememberedSpan = 12;

/**
 * Replacement for the vanilla Budget tab: the rows on the left pick which lines the chart plots, with the span stats
 * for the hovered row or line under them; the chart takes the rest, and the vanilla detail panel for a hovered item
 * row pops out over the chart's left edge, next to the rows.
 */
export const BudgetPage = () => {
  const { translate } = useLocalization();
  const incomeItems = useValue(economyBudget.incomeItems$) as BudgetItemLike[];
  const incomeValues = useValue(economyBudget.incomeValues$);
  const expenseItems = useValue(economyBudget.expenseItems$) as BudgetItemLike[];
  const expenseValues = useValue(economyBudget.expenseValues$);
  const totalIncome = useValue(economyBudget.totalIncome$);
  const totalExpenses = useValue(economyBudget.totalExpenses$);
  const history = useValue(history$);

  const [enabled, setEnabled] = useState(rememberedEnabled);
  const [span, setSpanState] = useState(rememberedSpan);
  const [rowHover, setRowHover] = useState<string | null>(null);
  const [lineHover, setLineHover] = useState<string | null>(null);
  const hoverKey = lineHover ?? rowHover;

  useEffect(() => {
    rememberedSpan = span;
    sendSpan(span);
  }, [span]);

  const toggle = useCallback((key: string) => {
    setEnabled((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      rememberedEnabled = next;
      return next;
    });
  }, []);

  const label = useCallback(
    (key: string, itemId?: string) => {
      if (key === INCOME_TOTAL) return translate("EconomyPanel.INCOME_SECTION_TITLE", "Revenues") ?? "Revenues";
      if (key === EXPENSE_TOTAL) return translate("EconomyPanel.EXPENSES_SECTION_TITLE", "Expenses") ?? "Expenses";
      if (key === BALANCE) return translate("EconomyPanel.MONTHLY_BALANCE", "Monthly balance") ?? "Monthly balance";
      return translate(`EconomyPanel.BUDGET_ITEM[${itemId}]`, itemId ?? key) ?? itemId ?? key;
    },
    [translate]
  );

  const allSeries: SeriesDef[] = useMemo(
    () => (history ? buildSeries(history, incomeItems, expenseItems) : []),
    [history, incomeItems, expenseItems]
  );

  const lines: ChartLine[] = useMemo(
    () =>
      allSeries
        .filter((s) => enabled.has(s.key))
        .map((s) => ({
          key: s.key,
          label: label(s.key, s.itemId),
          color: s.color,
          values: s.values,
          negateInTooltip: s.kind === "expense" || s.kind === "expense-total",
        })),
    [allSeries, enabled, label]
  );

  // The vanilla detail panel pops out only for a hovered row (a line has its tooltip); totals have no detail.
  const detail = useMemo(() => {
    if (!rowHover) return null;
    const [kind, id] = rowHover.split(":");
    if (kind === "income") return { item: incomeItems.find((i) => i.id === id) ?? null, values: incomeValues };
    if (kind === "expense") return { item: expenseItems.find((i) => i.id === id) ?? null, values: expenseValues };
    return null;
  }, [rowHover, incomeItems, incomeValues, expenseItems, expenseValues]);

  const hoveredSeries = hoverKey ? allSeries.find((s) => s.key === hoverKey) : undefined;

  const row = (key: string, title: string, color: string, value: number, header = false) => (
    <div
      key={key}
      className={`${styles.row} ${header ? styles.headerRow : ""} ${hoverKey === key ? styles.hovered : ""}`}
      onMouseEnter={() => setRowHover(key)}
      onMouseLeave={() => setRowHover((k) => (k === key ? null : k))}
      onClick={() => toggle(key)}
    >
      <div
        className={`${styles.check} ${enabled.has(key) ? styles.checked : ""}`}
        style={{ borderColor: color, backgroundColor: enabled.has(key) ? color : "transparent" }}
      />
      <div className={styles.rowTitle}>{title}</div>
      <div className={styles.rowValue}>
        <NumberField value={value} importance={header ? "secondary" : undefined} />
      </div>
    </div>
  );

  return (
    <div className={styles.budgetPage}>
      <div className={styles.listColumn}>
        <div className={styles.section}>
          {row(INCOME_TOTAL, label(INCOME_TOTAL), TOTAL_COLORS[INCOME_TOTAL], totalIncome, true)}
          {incomeItems
            .filter((i) => i.active)
            .map((i) => row(`income:${i.id}`, label("", i.id), rgbaToCss(i.color), itemValue(i, incomeValues)))}
        </div>
        <div className={styles.section}>
          {row(EXPENSE_TOTAL, label(EXPENSE_TOTAL), TOTAL_COLORS[EXPENSE_TOTAL], totalExpenses, true)}
          {expenseItems
            .filter((i) => i.active)
            .map((i) => row(`expense:${i.id}`, label("", i.id), rgbaToCss(i.color), itemValue(i, expenseValues)))}
        </div>
        <div className={styles.section}>{row(BALANCE, label(BALANCE), TOTAL_COLORS[BALANCE], totalIncome + totalExpenses, true)}</div>
        {hoveredSeries && <SeriesStats series={hoveredSeries} negate={hoveredSeries.kind === "expense" || hoveredSeries.kind === "expense-total"} />}
      </div>

      <div className={styles.chartColumn}>
        <div className={styles.spanBar}>
          {SPAN_OPTIONS.map((o) => (
            <div key={o.label} className={`${styles.spanButton} ${span === o.months ? styles.spanActive : ""}`} onClick={() => setSpanState(o.months)}>
              {o.label}
            </div>
          ))}
        </div>
        <div className={styles.chartArea}>
          <LineChart
            lines={lines}
            monthsAgo={history?.monthsAgo ?? []}
            highlightKey={hoverKey}
            onHoverLine={setLineHover}
            insetLeftRem={detail?.item ? POPOUT_INSET_REM : 0}
          />
        </div>
        {detail?.item && (
          <div className={styles.detailPopout}>
            <div className={styles.detailPopoutBox}>
              <BudgetItemDetail item={detail.item} values={detail.values} />
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

/** Now / average / low / high / change over the selected span (the points on the chart), per month. */
const SeriesStats = ({ series, negate }: { series: SeriesDef; negate: boolean }) => {
  const v = series.values;
  if (v.length === 0) return null;
  const sign = negate ? -1 : 1;
  const avg = v.reduce((a, b) => a + b, 0) / v.length;
  const stats: [string, number][] = [
    ["Now", v[v.length - 1]],
    ["Average", avg],
    ["Lowest", Math.min(...v)],
    ["Highest", Math.max(...v)],
    ["Change", v[v.length - 1] - v[0]],
  ];
  return (
    <div className={styles.stats}>
      {stats.map(([name, value]) => (
        <div key={name} className={styles.statRow}>
          <div>{name}</div>
          <div>{formatMoney(sign * value)}</div>
        </div>
      ))}
    </div>
  );
};
