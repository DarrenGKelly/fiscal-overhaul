import { describe, expect, it } from "vitest";
import { BudgetHistory, BudgetItemLike, rgbaToCss } from "../chart/chart-math";
import {
  buildSourceSeries,
  childColor,
  childKey,
  childRows,
  childShade,
  EXPAND_SINGLE_SOURCE,
  expandArrow,
  hasExpander,
  itemKey,
  subItemLocaleId,
  toggled,
  visibleSources,
} from "./budget-rows";

const COLOR = { r: 0.4, g: 0.6, b: 0.2, a: 1 };
const item = (id: string, sources: [string, number][], active = true): BudgetItemLike => ({
  id,
  color: COLOR,
  sources: sources.map(([sid, index]) => ({ id: sid, index })),
  active,
});

// Income: Residential tax (0, district) + Commercial tax (1, district); Exports: Electricity (4, district) + Water (5, city-only).
const TAXES = item("Taxes", [["ResidentialTaxes", 0], ["CommercialTaxes", 1]]);
const EXPORTS = item("Exports", [["ExportElectricity", 4], ["ExportWater", 5]]);
const LOAN = item("LoanInterest", [["LoanInterest", 1]]);

const history = (district?: string): BudgetHistory => ({
  span: 3,
  samples: 3,
  monthsAgo: [2, 1, 0],
  income: [[10, 11, 12], [20, 21, 22], [], [], [40, 41, 42], [50, 51, 52]],
  expense: [[5, 6, 7], [8, 9, 10]],
  district,
  nowIncome: district ? [100, 200, 0, 0, 400, 500] : null,
  nowExpense: district ? [30, 40] : null,
});

describe("keys and labels", () => {
  it("category and child keys extend buildSeries' item keys", () => {
    expect(itemKey("income", "Taxes")).toBe("income:Taxes");
    expect(childKey("expense", "Upkeep", "ServiceUpkeep")).toBe("expense:Upkeep:ServiceUpkeep");
    expect(childKey("income", "Taxes", "A")).not.toBe(childKey("expense", "Taxes", "A"));
  });

  it("a subcategory's name is vanilla's BUDGET_SUB_ITEM id", () => {
    expect(subItemLocaleId("ResidentialTaxes")).toBe("EconomyPanel.BUDGET_SUB_ITEM[ResidentialTaxes]");
    expect(subItemLocaleId("ResidentialTaxes")).not.toContain("BUDGET_ITEM[");
  });

  it("the arrow points down only while expanded", () => {
    expect(expandArrow(true)).toContain("ArrowDown");
    expect(expandArrow(false)).toContain("ArrowRight");
  });
});

describe("visibleSources / hasExpander", () => {
  it("Entire City shows every source", () => {
    expect(visibleSources(EXPORTS, "income", false).map((s) => s.id)).toEqual(["ExportElectricity", "ExportWater"]);
  });

  it("a district hides city-only sources (the isCityOnlyItem rule)", () => {
    expect(visibleSources(EXPORTS, "income", true).map((s) => s.id)).toEqual(["ExportElectricity"]);
    expect(visibleSources(TAXES, "income", true)).toHaveLength(2);
  });

  it("a category with several sources gets an expander, one with a single source doesn't (provisional)", () => {
    expect(EXPAND_SINGLE_SOURCE).toBe(false);
    expect(hasExpander(TAXES, "income", false)).toBe(true);
    expect(hasExpander(LOAN, "expense", false)).toBe(false);
  });

  it("a category left with one source in a district loses its expander there", () => {
    expect(hasExpander(EXPORTS, "income", false)).toBe(true);
    expect(hasExpander(EXPORTS, "income", true)).toBe(false);
  });

  it("with EXPAND_SINGLE_SOURCE a single-source category gets one too, an empty one never", () => {
    expect(hasExpander(LOAN, "expense", false, true)).toBe(true);
    expect(hasExpander(item("None", []), "income", false, true)).toBe(false);
  });
});

describe("childColor", () => {
  it.each([1, 2, 3, 4, 5, 8])("%i children: distinct from the parent and from each other", (count) => {
    const parent = rgbaToCss(COLOR);
    const colors = Array.from({ length: count }, (_, i) => childColor(COLOR, i, count));
    expect(colors).not.toContain(parent);
    expect(new Set(colors).size).toBe(count);
  });

  it("shades are never 0 (the parent) and stay within the lightest/darkest bounds", () => {
    for (let count = 1; count <= 8; count++) {
      for (let i = 0; i < count; i++) {
        const t = childShade(i, count);
        expect(Math.abs(t)).toBeGreaterThan(0.01);
        expect(t).toBeGreaterThanOrEqual(-0.45 - 1e-9);
        expect(t).toBeLessThanOrEqual(0.6 + 1e-9);
      }
    }
  });

  it("keeps the parent's alpha", () => {
    expect(childColor({ ...COLOR, a: 0.5 }, 0, 2)).toMatch(/, 0\.5\)$/);
  });
});

describe("childRows", () => {
  it("Entire City: one row per source, valued from the vanilla values array", () => {
    const rows = childRows(TAXES, "income", [7, 9], history());
    expect(rows.map((r) => r.key)).toEqual(["income:Taxes:ResidentialTaxes", "income:Taxes:CommercialTaxes"]);
    expect(rows.map((r) => r.value)).toEqual([7, 9]);
  });

  it("a district: city-only sources hidden, values from nowIncome", () => {
    const rows = childRows(EXPORTS, "income", [999, 999, 999, 999, 999, 999], history("d1"));
    expect(rows.map((r) => r.source.id)).toEqual(["ExportElectricity"]);
    expect(rows[0].value).toBe(400);
  });

  it("a district's expense children are negated, as vanilla shows expenses", () => {
    const upkeep = item("Upkeep", [["A", 0]]);
    expect(childRows(upkeep, "expense", [], history("d1"))[0].value).toBe(-30);
  });
});

describe("buildSourceSeries", () => {
  it("one series per source of each expandable item, from that source's history", () => {
    const series = buildSourceSeries(history(), [TAXES, LOAN], "income");
    expect(series.map((s) => s.key)).toEqual(["income:Taxes:ResidentialTaxes", "income:Taxes:CommercialTaxes"]);
    expect(series[0].values).toEqual([10, 11, 12]);
    expect(series[1].values).toEqual([20, 21, 22]);
    expect(series[1]).toMatchObject({ kind: "income", itemId: "Taxes", sourceId: "CommercialTaxes" });
  });

  it("colours match the child rows'", () => {
    const series = buildSourceSeries(history(), [TAXES], "income");
    const rows = childRows(TAXES, "income", [0, 0], history());
    expect(series.map((s) => s.color)).toEqual(rows.map((r) => r.color));
  });

  it("skips inactive items, and city-only sources in a district", () => {
    expect(buildSourceSeries(history(), [item("Off", [["A", 0], ["B", 1]], false)], "income")).toEqual([]);
    expect(buildSourceSeries(history("d1"), [EXPORTS], "income")).toEqual([]);
  });

  it("expense series read history.expense", () => {
    const series = buildSourceSeries(history(), [item("Upkeep", [["A", 0], ["B", 1]])], "expense");
    expect(series.map((s) => s.values)).toEqual([[5, 6, 7], [8, 9, 10]]);
  });
});

describe("toggled", () => {
  it("adds a missing key and removes a present one, leaving the original untouched", () => {
    const start = new Set(["a"]);
    expect([...toggled(start, "b")].sort()).toEqual(["a", "b"]);
    expect([...toggled(start, "a")]).toEqual([]);
    expect([...start]).toEqual(["a"]);
  });
});
