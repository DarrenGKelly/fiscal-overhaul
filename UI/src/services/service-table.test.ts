import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import { DistrictServiceBudget } from "./district-services";
import {
  budgetReset,
  budgetValue,
  DistrictServiceFee,
  districtFeeIncome,
  estimateTotal,
  FeeLike,
  feeDefault,
  feeRowEstimate,
  visibleEstimates,
  feeFromSlider,
  feeReset,
  feeRows,
  feeToSlider,
  serviceEstimate,
  tableDistrict,
} from "./service-table";

const POWER = "7:1";
const WATER = "8:1";
const NORTH = "40:2";

const fee = (over: Partial<FeeLike> = {}): FeeLike => ({ resource: 0, name: "Electricity", fee: 0.2, min: 0, max: 0.4, adjustable: true, incomeInternal: 900, ...over });

const budgets: DistrictServiceBudget[] = [
  { service: POWER, district: NORTH, budget: 120, upkeep: 3000 },
  { service: WATER, district: NORTH, budget: 0, upkeep: 1000 },
  { service: POWER, district: OUTSIDE_KEY, budget: 0, upkeep: 500 },
];
const fees: DistrictServiceFee[] = [
  { service: POWER, district: NORTH, resource: 0, income: 600 },
  { service: WATER, district: NORTH, resource: 6, income: 250 },
  { service: WATER, district: NORTH, resource: 11, income: 150 },
  { service: POWER, district: OUTSIDE_KEY, resource: 0, income: 300 },
];
const data = { budgets, fees };

describe("feeRows", () => {
  const list = [fee(), fee({ resource: 5, adjustable: false })];
  it("shows only the adjustable fees, as vanilla does", () => {
    expect(feeRows(list, false).map((f) => f.resource)).toEqual([0]);
    expect(feeRows(list, true, "disabled").map((f) => f.resource)).toEqual([0]);
  });
  it("shows none without fees, or in a district view when hidden there", () => {
    expect(feeRows(undefined, false)).toEqual([]);
    expect(feeRows([fee({ adjustable: false })], false)).toEqual([]);
    expect(feeRows(list, true, "hidden")).toEqual([]);
    expect(feeRows(list, false, "hidden")).toHaveLength(1);
  });
});

describe("fee slider", () => {
  it("maps the fee onto vanilla's 0..200 and back", () => {
    expect(feeToSlider(0.2, 0, 0.4)).toBe(100);
    expect(feeToSlider(0.4, 0, 0.4)).toBe(200);
    expect(feeFromSlider(100, 0, 0.4)).toBeCloseTo(0.2);
    expect(feeFromSlider(feeToSlider(0.3, 0, 0.4), 0, 0.4)).toBeCloseTo(0.3);
  });
  it("clamps a value outside 0..200, and an empty range is 0", () => {
    expect(feeFromSlider(260, 0, 0.4)).toBeCloseTo(0.4);
    expect(feeFromSlider(-5, 0, 0.4)).toBe(0);
    expect(feeFromSlider(NaN, 0, 0.4)).toBe(0);
    expect(feeToSlider(1, 0, 0)).toBe(0);
  });
});

describe("feeDefault", () => {
  it("finds the resource's default", () => expect(feeDefault([{ resource: 0, fee: 0.2 }], 0)).toBe(0.2));
  it("is null for an unknown resource or no data", () => {
    expect(feeDefault([{ resource: 0, fee: 0.2 }], 6)).toBeNull();
    expect(feeDefault(null, 0)).toBeNull();
  });
});

describe("budgetReset", () => {
  it("city view: resets to 100 %, shown only off it", () => {
    expect(budgetReset(budgets, POWER, CITY_KEY, 120)).toEqual({ kind: "default", target: 100, differs: true });
    expect(budgetReset(budgets, POWER, CITY_KEY, 100)).toEqual({ kind: "default", target: 100, differs: false });
  });
  it("district view: clears the override, its marker at the city's %, shown only with an override", () => {
    expect(budgetReset(budgets, POWER, NORTH, 90)).toEqual({ kind: "city", target: 90, differs: true });
    expect(budgetReset(budgets, WATER, NORTH, 90)).toEqual({ kind: "city", target: 90, differs: false });
    expect(budgetReset(budgets, POWER, OUTSIDE_KEY, 90).differs).toBe(false);
  });
});

describe("budgetValue", () => {
  it("district view shows the area's override, else the city's %", () => {
    expect(budgetValue(budgets, POWER, NORTH, 90)).toBe(120);
    expect(budgetValue(budgets, WATER, NORTH, 90)).toBe(90);
  });
  it("city view ignores district overrides", () => expect(budgetValue(budgets, POWER, CITY_KEY, 90)).toBe(90));
});

describe("feeReset", () => {
  it("city view: back to vanilla's default, marker at it, shown only off it", () => {
    expect(feeReset(fee({ fee: 0.3 }), 0.2, CITY_KEY)).toEqual({ kind: "default", target: 100, differs: true });
    expect(feeReset(fee({ fee: 0.2 }), 0.2, CITY_KEY).differs).toBe(false);
  });
  it("district view (fees are city-wide) and an unknown default: no button, no marker", () => {
    expect(feeReset(fee({ fee: 0.3 }), 0.2, NORTH)).toMatchObject({ target: null, differs: false });
    expect(feeReset(fee({ fee: 0.3 }), null, CITY_KEY)).toMatchObject({ target: null, differs: false });
  });
});

describe("serviceEstimate", () => {
  it("city view: vanilla's NET as the service list shows it", () => {
    expect(serviceEstimate({ key: POWER, budget: -1234 }, CITY_KEY, data)).toBe(-1234);
  });
  it("district view: the area's fee share minus its upkeep", () => {
    expect(serviceEstimate({ key: POWER, budget: -1234 }, NORTH, data)).toBe(600 - 3000);
    expect(serviceEstimate({ key: WATER, budget: 0 }, NORTH, data)).toBe(250 + 150 - 1000);
    expect(serviceEstimate({ key: WATER, budget: 0 }, OUTSIDE_KEY, data)).toBe(0);
  });
  it("district view is not vanilla's city figure", () => {
    expect(serviceEstimate({ key: POWER, budget: -1234 }, NORTH, data)).not.toBe(-1234);
  });
});

describe("districtFeeIncome", () => {
  it("is the area's share of one fee", () => {
    expect(districtFeeIncome(fees, POWER, NORTH, 0)).toBe(600);
    expect(districtFeeIncome(fees, WATER, NORTH, 11)).toBe(150);
  });
  it("is 0 for an area or resource with no share", () => {
    expect(districtFeeIncome(fees, WATER, OUTSIDE_KEY, 6)).toBe(0);
    expect(districtFeeIncome(fees, POWER, NORTH, 6)).toBe(0);
    expect(districtFeeIncome(null, POWER, NORTH, 0)).toBe(0);
  });
});

describe("estimateTotal", () => {
  const services = [
    { key: POWER, budget: -1000 },
    { key: WATER, budget: 500 },
  ];
  it("is the sum of the service rows, city view", () => expect(estimateTotal(services, CITY_KEY, data)).toBe(-500));
  it("is the sum of the district's rows, so the rows add up to the total", () => {
    const rows = services.map((s) => serviceEstimate(s, NORTH, data));
    expect(estimateTotal(services, NORTH, data)).toBe(rows[0] + rows[1]);
    expect(estimateTotal(services, NORTH, data)).toBe(600 - 3000 + 400 - 1000);
  });
  it("does not add the fee sub-rows again", () => {
    expect(estimateTotal(services, NORTH, data)).not.toBe(600 - 3000 + 400 - 1000 + 600 + 400);
    expect(estimateTotal([], NORTH, data)).toBe(0);
  });
});

describe("tableDistrict", () => {
  it("with District service budgets on, the picked district or Outside Districts is shown", () => {
    expect(tableDistrict(true, NORTH)).toBe(NORTH);
    expect(tableDistrict(true, OUTSIDE_KEY)).toBe(OUTSIDE_KEY);
    expect(tableDistrict(true, CITY_KEY)).toBe(CITY_KEY);
  });
  it("off, the table is the city view whatever was picked: no district overrides", () => {
    expect(tableDistrict(false, NORTH)).toBe(CITY_KEY);
    expect(tableDistrict(false, OUTSIDE_KEY)).not.toBe(OUTSIDE_KEY);
  });
});

describe("visibleEstimates (regression: the fee sub-rows' figures were not in the total)", () => {
  const services = [
    { key: POWER, budget: -31191, fees: [fee({ incomeInternal: 14104 })] },
    { key: WATER, budget: -6766, fees: [fee({ resource: 6, incomeInternal: 6207 }), fee({ resource: 11, adjustable: false })] },
  ];
  const sum = (column: (number | null)[]) => column.reduce<number>((t, v) => t + (v ?? 0), 0);
  it("the visible column adds up to the footer, city and district view", () => {
    expect(sum(visibleEstimates(services, CITY_KEY, data))).toBe(estimateTotal(services, CITY_KEY, data));
    expect(sum(visibleEstimates(services, NORTH, data))).toBe(estimateTotal(services, NORTH, data));
  });
  it("draws each service's NET and a blank per adjustable fee row, never the fee income", () => {
    expect(visibleEstimates(services, CITY_KEY, data)).toEqual([-31191, null, -6766, null]);
    expect(visibleEstimates(services, CITY_KEY, data)).not.toContain(14104);
    expect(feeRowEstimate()).toBeNull();
  });
});
