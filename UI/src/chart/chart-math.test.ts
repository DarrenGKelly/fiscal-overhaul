import { describe, expect, it } from "vitest";
import {
  BALANCE,
  buildSeries,
  BudgetHistory,
  BudgetItemLike,
  EXPENSE_TOTAL,
  formatMoney,
  formatMoneyShort,
  formatMonthsAgo,
  formatMonthsAxis,
  INCOME_TOTAL,
  itemValue,
  nearestIndex,
  nearestSeries,
  niceTicks,
  popoutSideIsLeft,
  timeTicks,
  rgbaToCss,
  sumSources,
  valueRange,
} from "./chart-math";

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
    expect(formatMonthsAgo(0)).toBe("Now");
    expect(formatMonthsAgo(-0.5)).toBe("12 h ago");
    expect(formatMonthsAgo(-6)).toBe("6 months ago");
    expect(formatMonthsAgo(-36)).toBe("3 years ago");
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
    expect(formatMonthsAxis(0)).toBe("Now");
    expect(formatMonthsAxis(-0.75)).toBe("-3 wk");
    expect(formatMonthsAxis(-2)).toBe("-2 mo");
    expect(formatMonthsAxis(-1.5)).toBe("-1 mo 2 wk");
    expect(formatMonthsAxis(-24)).toBe("-2 yr");
  });

  it("never shows a fractional number", () => {
    expect(formatMonthsAxis(-2.1)).toBe("-2 mo");
    expect(formatMonthsAxis(-2.1)).not.toContain(".");
  });
});

describe("popoutSideIsLeft", () => {
  it("puts the detail pop-out on the left when hovering the chart's right half", () => {
    expect(popoutSideIsLeft(0.9)).toBe(true);
  });

  it("keeps it on the right when hovering the left half", () => {
    expect(popoutSideIsLeft(0.2)).toBe(false);
  });
});
