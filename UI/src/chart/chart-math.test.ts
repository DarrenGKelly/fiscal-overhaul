import { describe, expect, it } from "vitest";
import {
  BALANCE,
  buildSeries,
  BudgetHistory,
  BudgetItemLike,
  EXPENSE_TOTAL,
  formatMoney,
  formatPerMonth,
  formatMoneyShort,
  formatMonthsAgo,
  formatMonthsAxis,
  INCOME_TOTAL,
  itemValue,
  nearestIndex,
  nearestSeries,
  niceTicks,
  plotLeft,
  timeTicks,
  rgbaToCss,
  seriesStats,
  DEFAULT_SPAN_MONTHS,
  SPAN_OPTIONS,
  spanLabel,
  sumSources,
  valueRange,
} from "./chart-math";
import { Translate } from "../l10n";

/** A stand-in for the game's translate: the dictionary's text, else the fallback. */
const fakeTranslate =
  (dict: Record<string, string>): Translate =>
  (id, fallback) =>
    dict[id] ?? fallback ?? null;

const red = { r: 1, g: 0, b: 0, a: 1 };

const history: BudgetHistory = {
  span: 12,
  samples: 3,
  monthsAgo: [-2, -1, 0],
  income: [
    [100, 110, 120], // source 0
    [10, 10, 10], // source 1
    [5, 5, 5], // source 2
  ],
  expense: [
    [50, 60, 70], // source 0
    [0, 0, 0], // source 1
  ],
};

const incomeItems: BudgetItemLike[] = [
  { id: "Taxes", color: red, active: true, sources: [{ id: "A", index: 0 }, { id: "B", index: 1 }] },
  { id: "Hidden", color: red, active: false, sources: [{ id: "C", index: 2 }] },
];
const expenseItems: BudgetItemLike[] = [{ id: "Upkeep", color: red, active: true, sources: [{ id: "U", index: 0 }] }];

describe("buildSeries", () => {
  const series = buildSeries(history, incomeItems, expenseItems);
  const byKey = Object.fromEntries(series.map((s) => [s.key, s]));

  it("sums an item's sources", () => {
    expect(byKey["income:Taxes"].values).toEqual([110, 120, 130]);
  });

  it("totals include every source, inactive items too", () => {
    expect(byKey[INCOME_TOTAL].values).toEqual([115, 125, 135]);
    expect(byKey[EXPENSE_TOTAL].values).toEqual([50, 60, 70]);
  });

  it("leaves inactive items out of the selectable lines", () => {
    expect(byKey["income:Hidden"]).toBeUndefined();
  });

  it("balance is revenue minus expenses", () => {
    expect(byKey[BALANCE].values).toEqual([65, 65, 65]);
  });

  it("orders revenue, revenue items, expenses, expense items, balance", () => {
    expect(series.map((s) => s.key)).toEqual([INCOME_TOTAL, "income:Taxes", EXPENSE_TOTAL, "expense:Upkeep", BALANCE]);
  });
});

describe("itemValue", () => {
  it("sums the item's sources from the flat values array", () => {
    expect(itemValue({ sources: [{ index: 0 }, { index: 2 }] }, [100, 7, 5])).toBe(105);
  });

  it("ignores sources missing from the values", () => {
    expect(itemValue({ sources: [{ index: 9 }] }, [1, 2])).toBe(0);
  });
});

describe("sumSources", () => {
  it("treats missing sources and points as 0", () => {
    expect(sumSources([{ index: 0 }, { index: 9 }], [[1, 2]], 3)).toEqual([1, 2, 0]);
  });
});

describe("valueRange", () => {
  it("starts at 0 for all-positive data and pads the top", () => {
    const [min, max] = valueRange([{ values: [10, 100] }]);
    expect(min).toBe(0);
    expect(max).toBeGreaterThan(100);
  });

  it("extends below 0 when a line goes negative", () => {
    const [min] = valueRange([{ values: [-50, 100] }]);
    expect(min).toBeLessThan(-50);
  });

  it("never returns an empty range", () => {
    const [min, max] = valueRange([{ values: [0, 0] }]);
    expect(max).toBeGreaterThan(min);
  });
});

describe("niceTicks", () => {
  it("uses round steps", () => {
    expect(niceTicks(0, 100, 5)).toEqual([0, 20, 40, 60, 80, 100]);
  });

  it("includes zero cleanly across a negative range", () => {
    const ticks = niceTicks(-30, 70, 5);
    expect(ticks).toContain(0);
    expect(ticks.every((t) => t >= -30 && t <= 70)).toBe(true);
  });

  it("degenerate range gives a single tick", () => {
    expect(niceTicks(5, 5)).toEqual([5]);
  });
});

describe("nearestIndex / nearestSeries", () => {
  it("finds the closest x", () => {
    expect(nearestIndex([0, 10, 20], 14)).toBe(1);
    expect(nearestIndex([0, 10, 20], 16)).toBe(2);
    expect(nearestIndex([], 5)).toBe(-1);
  });

  it("finds the line closest to the cursor value", () => {
    const a = { name: "a", values: [100] };
    const b = { name: "b", values: [10] };
    expect(nearestSeries([a, b], 0, 80)?.name).toBe("a");
    expect(nearestSeries([a, b], 0, 20)?.name).toBe("b");
    expect(nearestSeries([a, b], 5, 20)).toBeUndefined();
  });
});

describe("formatting", () => {
  it("formats money with separators and sign", () => {
    expect(formatMoney(1234567.4)).toBe("¢1,234,567");
    expect(formatMoney(-12345)).toBe("-¢12,345");
    expect(formatMoney(0)).toBe("¢0");
  });

  it("shortens axis money", () => {
    expect(formatMoneyShort(1_250_000)).toBe("¢1.3M");
    expect(formatMoneyShort(350_000)).toBe("¢350k");
    expect(formatMoneyShort(-900)).toBe("-¢900");
  });

  it("describes time offsets", () => {
    expect(formatMonthsAgo(0, null)).toBe("Now");
    expect(formatMonthsAgo(-0.5, null)).toBe("12 h ago");
    expect(formatMonthsAgo(-6, null)).toBe("6 months ago");
    expect(formatMonthsAgo(-36, null)).toBe("3 years ago");
  });

  it("converts game colours to css", () => {
    expect(rgbaToCss({ r: 1, g: 0.5, b: 0, a: 1 })).toBe("rgba(255, 128, 0, 1)");
  });
});

describe("timeTicks", () => {
  const isWholeWeeks = (m: number) => Math.abs(m * 4 - Math.round(m * 4)) < 1e-9;

  it("uses whole weeks on a one-month span", () => {
    expect(timeTicks(1, 6)).toEqual([-1, -0.75, -0.5, -0.25, 0]);
  });

  it("uses whole months on a span of a few months, ending at now", () => {
    // Regression: the axis was labelled at the start, middle and end, e.g. -4.2 mo / -2.1 mo / Now.
    const ticks = timeTicks(4.2, 6);
    expect(ticks).toEqual([-4, -3, -2, -1, 0]);
    expect(ticks).not.toContain(-2.1);
  });

  it("steps up to whole years on long spans and never exceeds maxTicks", () => {
    expect(timeTicks(36, 6)).toEqual([-36, -24, -12, 0]);
    for (const span of [0.1, 0.7, 1, 3, 4.2, 6, 11.9, 12, 36, 100, 500]) {
      const ticks = timeTicks(span, 6);
      expect(ticks.length).toBeLessThanOrEqual(6);
      expect(ticks[ticks.length - 1]).toBe(0);
      expect(ticks[0]).toBeGreaterThanOrEqual(-span - 1e-9);
      expect(ticks.every(isWholeWeeks)).toBe(true);
    }
  });

  it("gives just now when the span is under a week", () => {
    expect(timeTicks(0.1, 6)).toEqual([0]);
  });
});

describe("formatMonthsAxis", () => {
  it("labels whole weeks, months, combos and years", () => {
    expect(formatMonthsAxis(0, null)).toBe("Now");
    expect(formatMonthsAxis(-0.75, null)).toBe("-3 wk");
    expect(formatMonthsAxis(-2, null)).toBe("-2 mo");
    expect(formatMonthsAxis(-1.5, null)).toBe("-1 mo 2 wk");
    expect(formatMonthsAxis(-24, null)).toBe("-2 yr");
  });

  it("never shows a fractional number", () => {
    expect(formatMonthsAxis(-2.1, null)).toBe("-2 mo");
    expect(formatMonthsAxis(-2.1, null)).not.toContain(".");
  });
});


describe("plotLeft", () => {
  it("starts the plot right after the widest y label", () => {
    // Regression: a fixed 92rem margin left a big gap before short labels like "¢0".
    expect(plotLeft(0.5, 30, 10)).toBe(35);
    expect(plotLeft(0.5, 30, 10)).not.toBe(92 * 0.5);
  });

  it("moves the y axis right past a pop-out inset", () => {
    expect(plotLeft(0.5, 30, 10, 368)).toBe(219);
    expect(plotLeft(0.5, 30, 10, 368)).toBeGreaterThan(plotLeft(0.5, 30, 10));
  });

  it("ignores a negative inset", () => {
    expect(plotLeft(1, 40, 10, -50)).toBe(50);
  });
});

describe("formatPerMonth", () => {
  it("gives the tooltip value as one string, with no space to wrap at", () => {
    // Regression: "¢315,997" and "/mo" were separate text nodes and Gameface put "/mo" on a new line.
    expect(formatPerMonth(315997, null)).toBe("¢315,997/mo");
    expect(formatPerMonth(315997, null)).not.toContain(" ");
  });

  it("keeps the sign for expenses", () => {
    expect(formatPerMonth(-283002, null)).toBe("-¢283,002/mo");
  });
});

describe("localized chart text", () => {
  const german = fakeTranslate({
    "StatisticsPanel.TIME_SCALE_NOW": "Jetzt",
    "FiscalOverhaul.UI.MonthsAgo": "vor {COUNT} Monaten",
    "FiscalOverhaul.UI.AxisMonthsWeeks": "-{MONTHS} Mon. {WEEKS} Wo.",
    "FiscalOverhaul.UI.PerMonth": "{VALUE}/Monat",
    "FiscalOverhaul.UI.SpanYears": "{COUNT}J",
  });

  it("uses the game's translation when it has the key", () => {
    expect(formatMonthsAgo(-6, german)).toBe("vor 6 Monaten");
    expect(formatMonthsAgo(0, german)).toBe("Jetzt");
    expect(formatMonthsAxis(-1.5, german)).toBe("-1 Mon. 2 Wo.");
    expect(formatMonthsAxis(0, german)).toBe("Jetzt");
    expect(formatPerMonth(315997, german)).toBe("¢315,997/Monat");
    expect(spanLabel(36, german)).toBe("3J");
  });

  it("falls back to English for a key the game lacks", () => {
    expect(formatMonthsAgo(-36, german)).toBe("3 years ago");
    expect(formatMonthsAxis(-2, german)).toBe("-2 mo");
    expect(spanLabel(6, german)).toBe("6M");
    expect(spanLabel(6, german)).not.toBe("6J");
  });
});

describe("spanLabel", () => {
  it("labels the span buttons as before", () => {
    expect(SPAN_OPTIONS.map((m) => spanLabel(m, null))).toEqual(["1M", "3M", "6M", "1Y", "3Y", "All"]);
  });
});

describe("DEFAULT_SPAN_MONTHS", () => {
  it("opens the chart on 3M, one of the offered spans", () => {
    expect(SPAN_OPTIONS).toContain(DEFAULT_SPAN_MONTHS);
    expect(spanLabel(DEFAULT_SPAN_MONTHS, null)).toBe("3M");
  });
  it("is not the old 1Y default or all history", () => {
    expect(DEFAULT_SPAN_MONTHS).not.toBe(12);
    expect(DEFAULT_SPAN_MONTHS).not.toBe(0);
  });
});

describe("seriesStats", () => {
  it("shows exactly change then average over the span", () => {
    expect(seriesStats([10, 30, 20], null)).toEqual([
      ["Change", 10],
      ["Average", 20],
    ]);
  });

  it("drops now, lowest and highest", () => {
    const labels = seriesStats([10, 30, 20], null).map(([l]) => l);
    expect(labels).toHaveLength(2);
    for (const gone of ["Now", "Lowest", "Highest"]) expect(labels).not.toContain(gone);
  });

  it("reads its labels through translate", () => {
    const labels = seriesStats([1, 2], fakeTranslate({ "FiscalOverhaul.UI.StatAverage": "Durchschnitt" })).map(([l]) => l);
    expect(labels).toContain("Durchschnitt");
    expect(labels).not.toContain("Average");
  });

  it("has no rows for an empty series", () => {
    expect(seriesStats([], null)).toEqual([]);
  });
});

describe("buildSeries with district history", () => {
  const district: BudgetHistory = {
    ...history,
    district: "12:3",
    income: [
      [100, 110, 120],
      [10, 10, 10],
      [0, 0, 0], // city-only source, zeroed
    ],
    nowIncome: [120, 10, 0],
    nowExpense: [70, 0],
  };

  it("builds the same series shape", () => {
    const a = buildSeries(history, incomeItems, expenseItems).map((s) => s.key);
    const b = buildSeries(district, incomeItems, expenseItems);
    expect(b.map((s) => s.key)).toEqual(a);
    expect(b.find((s) => s.key === INCOME_TOTAL)!.values).toEqual([110, 120, 130]);
  });

  it("does not throw without nowIncome", () => {
    const { nowIncome, nowExpense, ...bare } = district;
    expect(() => buildSeries(bare, incomeItems, expenseItems)).not.toThrow();
  });
});
