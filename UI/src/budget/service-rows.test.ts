import { describe, expect, it } from "vitest";
import { BudgetHistory, BudgetItemLike, nearestSeries, shownValues, valueRange } from "../chart/chart-math";
import { childColor } from "./budget-rows";
import {
  buildServiceSeries,
  hasServiceRows,
  holdsServiceUpkeep,
  SERVICE_UPKEEP_SOURCE,
  serviceKey,
  serviceLocaleId,
  serviceRows,
} from "./service-rows";

const COLOR = { r: 0.4, g: 0.6, b: 0.2, a: 1 };
const item = (id: string, sources: [string, number][], active = true): BudgetItemLike => ({
  id,
  color: COLOR,
  sources: sources.map(([sid, index]) => ({ id: sid, index })),
  active,
});

const UPKEEP = item("ServiceUpkeep", [["ServiceUpkeep", SERVICE_UPKEEP_SOURCE]]);
const SUBSIDIES = item("Subsidies", [["SubsidyResidential", 0], ["SubsidyCommercial", 6]]);

const history = (services: BudgetHistory["services"]): BudgetHistory => ({
  span: 3,
  samples: 3,
  monthsAgo: [2, 1, 0],
  income: [],
  expense: [[], [], [], [], [], [0, 100, 120]],
  services,
});

const SPLIT = history([
  { name: "Healthcare", start: 1, values: [0, 60, 70] },
  { name: "Police", start: 1, values: [0, 40, 50] },
]);

describe("holdsServiceUpkeep", () => {
  it("is the expense item with the ServiceUpkeep source", () => {
    expect(holdsServiceUpkeep(UPKEEP, "expense")).toBe(true);
    expect(holdsServiceUpkeep(SUBSIDIES, "expense")).toBe(false);
    // Income source 5 is an income source, not upkeep.
    expect(holdsServiceUpkeep(item("Fees", [["Fee", SERVICE_UPKEEP_SOURCE]]), "income")).toBe(false);
  });
});

describe("hasServiceRows", () => {
  it("needs the upkeep item and services in the history", () => {
    expect(hasServiceRows(UPKEEP, "expense", SPLIT)).toBe(true);
    expect(hasServiceRows(UPKEEP, "expense", history([]))).toBe(false);
    expect(hasServiceRows(UPKEEP, "expense", history(null))).toBe(false);
    expect(hasServiceRows(UPKEEP, "expense", null)).toBe(false);
    expect(hasServiceRows(SUBSIDIES, "expense", SPLIT)).toBe(false);
  });
});

describe("keys and names", () => {
  it("keys a service row under its item, apart from source rows", () => {
    expect(serviceKey("ServiceUpkeep", "Police")).toBe("expense:ServiceUpkeep:service:Police");
    expect(serviceKey("ServiceUpkeep", "Police")).not.toBe("expense:ServiceUpkeep:Police");
  });

  it("uses vanilla's Services tab name", () => {
    expect(serviceLocaleId("Healthcare")).toBe("Services.NAME[Healthcare]");
  });
});

describe("serviceRows", () => {
  it("is one row per service in history order, valued at the newest point as a negative expense", () => {
    const rows = serviceRows(UPKEEP, "expense", SPLIT);
    expect(rows.map((r) => r.name)).toEqual(["Healthcare", "Police"]);
    expect(rows.map((r) => r.value)).toEqual([-70, -50]);
    expect(rows[0].color).toBe(childColor(COLOR, 0, 2));
    expect(rows[1].color).not.toBe(rows[0].color);
  });

  it("the rows add up to the item's value now", () => {
    const rows = serviceRows(UPKEEP, "expense", SPLIT);
    expect(rows.reduce((t, r) => t + r.value, 0)).toBe(-120);
  });

  it("colours after any source rows the item shows first", () => {
    const rows = serviceRows(UPKEEP, "expense", SPLIT, 2);
    expect(rows[0].color).toBe(childColor(COLOR, 2, 4));
  });

  it("is empty for other items or without data", () => {
    expect(serviceRows(SUBSIDIES, "expense", SPLIT)).toEqual([]);
    expect(serviceRows(UPKEEP, "expense", history([]))).toEqual([]);
  });
});

describe("buildServiceSeries", () => {
  it("is a line per service, from its start, keyed and named for the label", () => {
    const series = buildServiceSeries(SPLIT, [SUBSIDIES, UPKEEP]);
    expect(series.map((s) => s.key)).toEqual(["expense:ServiceUpkeep:service:Healthcare", "expense:ServiceUpkeep:service:Police"]);
    expect(series[0]).toMatchObject({ kind: "expense", itemId: "ServiceUpkeep", serviceName: "Healthcare", start: 1, values: [0, 60, 70] });
  });

  it("leaves out an inactive item", () => {
    expect(buildServiceSeries(SPLIT, [{ ...UPKEEP, active: false }])).toEqual([]);
  });
});

describe("series with a start", () => {
  const late = { values: [0, 0, 900], start: 2 };
  const full = { values: [5, 6, 7] };

  it("shows only the points from start", () => {
    expect(shownValues(late)).toEqual([900]);
    expect(shownValues(full)).toEqual([5, 6, 7]);
  });

  it("scales the axis by the shown points only", () => {
    expect(valueRange([{ values: [-500, 0, 10], start: 1 }])[0]).toBe(0);
    expect(valueRange([{ values: [-500, 0, 10] }])[0]).toBeLessThan(0);
  });

  it("is never the hovered line before its start", () => {
    expect(nearestSeries([late, full], 0, 0)).toBe(full);
    expect(nearestSeries([late, full], 2, 900)).toBe(late);
  });
});
