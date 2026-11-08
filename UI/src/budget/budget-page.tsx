import { useValue } from "cs2/api";
import { economyBudget } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { districtBudgetEnabled$, districts$, history$, playSound, setDistrict as sendDistrict, setSpan as sendSpan } from "bindings";
import { SOUNDS, soundForPick } from "sounds";
import {
  BALANCE,
  buildSeries,
  BudgetItemLike,
  DEFAULT_SPAN_MONTHS,
  EXPENSE_TOTAL,
  formatMoney,
  INCOME_TOTAL,
  rgbaToCss,
  SeriesDef,
  SPAN_OPTIONS,
  spanLabel,
  seriesStats,
  TOTAL_COLORS,
} from "chart/chart-math";
import { ChartLine, LineChart } from "chart/line-chart";
import { localizeVanilla, Translate } from "l10n";
import { BudgetItemDetail, NumberField, Scrollable } from "vanilla";
import { buildSourceSeries, childRows, expandArrow, hasExpander, itemKey, subItemLocaleId, toggled } from "./budget-rows";
import styles from "./budget-page.module.scss";
import {
  BudgetKind,
  CITY_KEY,
  districtOptions,
  effectiveKey,
  isCityOnlyItem,
  isDistrictView,
  rowValue,
  showsDistrictSelector,
} from "./district-select";
import { DistrictSelector } from "./district-selector";

/**
 * While the item pop-out shows, the chart's plot starts after it: the pop-out is 360rem wide at 6rem from the chart
 * panel's edge (budget-page.module.scss .detail-popout), the canvas starts 10rem in, plus a 12rem gap.
 */
const POPOUT_INSET_REM = 360 + 6 - 10 + 12;

// Kept outside the component so the choice survives switching tabs or closing the panel.
let rememberedEnabled = new Set<string>([INCOME_TOTAL, EXPENSE_TOTAL]);
let rememberedSpan = DEFAULT_SPAN_MONTHS;
let rememberedDistrict = CITY_KEY;
// The categories expanded into their subcategory rows (itemKey), kept like rememberedEnabled.
let rememberedExpanded = new Set<string>();

/**
 * Replacement for the vanilla Budget tab: the rows on the left pick which lines the chart plots; the chart takes the
 * rest, with the span stats (change, average) for the hovered row or line at its top left, and the vanilla detail
 * panel for a hovered item row pops out over the chart's left edge, next to the rows.
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
  const districts = useValue(districts$);
  const districtsOn = useValue(districtBudgetEnabled$);

  const [enabled, setEnabled] = useState(rememberedEnabled);
  const [span, setSpanState] = useState(rememberedSpan);
  const [district, setDistrictState] = useState(rememberedDistrict);
  const [expanded, setExpanded] = useState(rememberedExpanded);
  const [rowHover, setRowHover] = useState<string | null>(null);
  const [lineHover, setLineHover] = useState<string | null>(null);
  const hoverKey = lineHover ?? rowHover;

  // Sent on mount too: the C# side builds history only for the span it was last told.
  useEffect(() => {
    rememberedSpan = span;
    sendSpan(span);
  }, [span]);

  // Entire City, the districts, Outside; a choice no longer offered (deleted, recording off) falls back to Entire City.
  const districtChoices = useMemo(() => districtOptions(districts ?? [], translate), [districts, translate]);
  const selectedDistrict = effectiveKey(districtChoices, district);

  // Sent on mount too, like the span: the C# side builds history for the district it was last told.
  useEffect(() => {
    rememberedDistrict = selectedDistrict;
    sendDistrict(selectedDistrict);
  }, [selectedDistrict]);

  // Rows, values and lines follow the district the history binding carries, so they always match each other.
  const inDistrict = isDistrictView(history);
  const shownIncome = useMemo(
    () => incomeItems.filter((i) => i.active && !(inDistrict && isCityOnlyItem(i, "income"))),
    [incomeItems, inDistrict]
  );
  const shownExpense = useMemo(
    () => expenseItems.filter((i) => i.active && !(inDistrict && isCityOnlyItem(i, "expense"))),
    [expenseItems, inDistrict]
  );
  const incomeOf = (i: BudgetItemLike) => rowValue(i, incomeValues, history, "income");
  const expenseOf = (i: BudgetItemLike) => rowValue(i, expenseValues, history, "expense");
  // A district's totals are the sums of its rows; Entire City keeps the vanilla totals.
  const shownTotalIncome = inDistrict ? shownIncome.reduce((t, i) => t + incomeOf(i), 0) : totalIncome;
  const shownTotalExpenses = inDistrict ? shownExpense.reduce((t, i) => t + expenseOf(i), 0) : totalExpenses;

  const toggle = useCallback((key: string) => {
    setEnabled((prev) => {
      const next = toggled(prev, key);
      rememberedEnabled = next;
      return next;
    });
  }, []);

  const toggleExpanded = useCallback((key: string) => {
    playSound(SOUNDS.expandRows);
    setExpanded((prev) => {
      const next = toggled(prev, key);
      rememberedExpanded = next;
      return next;
    });
  }, []);

  const label = useCallback(
    (key: string, itemId?: string, sourceId?: string) => {
      if (key === INCOME_TOTAL) return localizeVanilla(translate, "revenues");
      if (key === EXPENSE_TOTAL) return localizeVanilla(translate, "expenses");
      if (key === BALANCE) return localizeVanilla(translate, "monthlyBalance");
      if (sourceId) return translate(subItemLocaleId(sourceId), sourceId) ?? sourceId;
      return translate(`EconomyPanel.BUDGET_ITEM[${itemId}]`, itemId ?? key) ?? itemId ?? key;
    },
    [translate]
  );

  // The categories' lines, then a line per subcategory (drawn only while ticked).
  const allSeries: SeriesDef[] = useMemo(
    () =>
      history
        ? [
            ...buildSeries(history, shownIncome, shownExpense),
            ...buildSourceSeries(history, shownIncome, "income"),
            ...buildSourceSeries(history, shownExpense, "expense"),
          ]
        : [],
    [history, shownIncome, shownExpense]
  );

  const lines: ChartLine[] = useMemo(
    () =>
      allSeries
        .filter((s) => enabled.has(s.key))
        .map((s) => ({
          key: s.key,
          label: label(s.key, s.itemId, s.sourceId),
          color: s.color,
          values: s.values,
          negateInTooltip: s.kind === "expense" || s.kind === "expense-total",
        })),
    [allSeries, enabled, label]
  );

  // The vanilla detail panel pops out only for a hovered row (a line has its tooltip); totals have no detail. Off for
  // a district: its breakdown is city-wide.
  const detail = useMemo(() => {
    if (!rowHover || inDistrict) return null;
    const [kind, id] = rowHover.split(":");
    if (kind === "income") return { item: incomeItems.find((i) => i.id === id) ?? null, values: incomeValues };
    if (kind === "expense") return { item: expenseItems.find((i) => i.id === id) ?? null, values: expenseValues };
    return null;
  }, [rowHover, inDistrict, incomeItems, incomeValues, expenseItems, expenseValues]);

  const hoveredSeries = hoverKey ? allSeries.find((s) => s.key === hoverKey) : undefined;

  // A category row with subcategories has an arrow that expands it (a click on the arrow doesn't toggle the line);
  // the others keep the arrow's space so the titles line up. Headers (totals, balance) have neither.
  const row = (key: string, title: string, color: string, value: number, header = false, expander?: boolean, child = false) => (
    <div
      key={key}
      className={`${styles.row} ${header ? styles.headerRow : ""} ${child ? styles.childRow : ""} ${hoverKey === key ? styles.hovered : ""}`}
      onMouseEnter={() => setRowHover(key)}
      onMouseLeave={() => setRowHover((k) => (k === key ? null : k))}
      onClick={() => {
        playSound(SOUNDS.toggleLine);
        toggle(key);
      }}
    >
      {expander !== undefined &&
        (expander ? (
          <img
            className={styles.expander}
            src={expandArrow(expanded.has(key))}
            onClick={(e) => {
              e.stopPropagation();
              toggleExpanded(key);
            }}
          />
        ) : (
          <div className={styles.expander} />
        ))}
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

  // A category's row and, while it is expanded, one indented row per subcategory (source).
  const categoryRows = (item: BudgetItemLike, kind: BudgetKind, values: number[], value: number) => {
    const key = itemKey(kind, item.id);
    const expander = hasExpander(item, kind, inDistrict);
    return (
      <Fragment key={key}>
        {row(key, label("", item.id), rgbaToCss(item.color), value, false, expander)}
        {expander &&
          expanded.has(key) &&
          childRows(item, kind, values, history).map((c) => row(c.key, label(c.key, item.id, c.source.id), c.color, c.value, false, false, true))}
      </Fragment>
    );
  };

  return (
    <div className={styles.budgetPage}>
      {showsDistrictSelector(districtsOn, districtChoices) && (
        <DistrictSelector
          options={districtChoices}
          selected={selectedDistrict}
          onSelect={(key) => {
            rememberedDistrict = key;
            setDistrictState(key);
          }}
        />
      )}
      <div className={styles.budgetBody}>
        <div className={styles.listColumn}>
          <Scrollable className={styles.listScroll} trackVisibility="scrollable">
            <div className={styles.section}>
              {row(INCOME_TOTAL, label(INCOME_TOTAL), TOTAL_COLORS[INCOME_TOTAL], shownTotalIncome, true)}
              {shownIncome.map((i) => categoryRows(i, "income", incomeValues, incomeOf(i)))}
            </div>
            <div className={styles.section}>
              {row(EXPENSE_TOTAL, label(EXPENSE_TOTAL), TOTAL_COLORS[EXPENSE_TOTAL], shownTotalExpenses, true)}
              {shownExpense.map((i) => categoryRows(i, "expense", expenseValues, expenseOf(i)))}
            </div>
            <div className={styles.section}>{row(BALANCE, label(BALANCE), TOTAL_COLORS[BALANCE], shownTotalIncome + shownTotalExpenses, true)}</div>
          </Scrollable>
        </div>

        <div className={styles.chartColumn}>
          <div className={styles.spanBar}>
            {SPAN_OPTIONS.map((months) => (
              <div key={months} className={`${styles.spanButton} ${span === months ? styles.spanActive : ""}`} onClick={() => {
                  playSound(soundForPick(span, months, SOUNDS.pickSpan));
                  setSpanState(months);
                }}>
                {spanLabel(months, translate)}
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
              overlay={
                hoveredSeries && (
                  <SeriesStats
                    series={hoveredSeries}
                    negate={hoveredSeries.kind === "expense" || hoveredSeries.kind === "expense-total"}
                    translate={translate}
                  />
                )
              }
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
    </div>
  );
};

/** Change and average over the selected span (the points on the chart), per month, side by side on one line. */
const SeriesStats = ({ series, negate, translate }: { series: SeriesDef; negate: boolean; translate: Translate }) => {
  const stats = seriesStats(series.values, translate);
  if (stats.length === 0) return null;
  const sign = negate ? -1 : 1;
  return (
    <div className={styles.stats}>
      {stats.map(([name, value]) => (
        <div key={name} className={styles.stat}>
          <div className={styles.statName}>{name}</div>
          <div className={styles.statValue}>{formatMoney(sign * value)}</div>
        </div>
      ))}
    </div>
  );
};
