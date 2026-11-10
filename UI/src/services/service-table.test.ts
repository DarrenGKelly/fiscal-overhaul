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
  feeRevenue,
  feeRows,
  feeToSlider,
  hasFeeSlider,
  serviceCost,
  serviceNet,
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
  it("shows every fee, so each fee's revenue has a row; only an adjustable one has a slider", () => {
    expect(feeRows(list, false).map((f) => f.resource)).toEqual([0, 5]);
    expect(feeRows(list, true, "disabled").map((f) => f.resource)).toEqual([0, 5]);
    expect(list.map(hasFeeSlider)).toEqual([true, false]);
  });
  it("shows none without fees, or in a district view when hidden there", () => {
    expect(feeRows(undefined, false)).toEqual([]);
    expect(feeRows(list, true, "hidden")).toEqual([]);
    expect(feeRows(list, false, "hidden")).toHaveLength(2);
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

describe("serviceNet", () => {
  it("city view: vanilla's NET as the service list shows it", () => {
    expect(serviceNet({ key: POWER, budget: -1234 }, CITY_KEY, data)).toBe(-1234);
  });
  it("district view: the area's fee share minus its upkeep", () => {
    expect(serviceNet({ key: POWER, budget: -1234 }, NORTH, data)).toBe(600 - 3000);
    expect(serviceNet({ key: WATER, budget: 0 }, NORTH, data)).toBe(250 + 150 - 1000);
    expect(serviceNet({ key: WATER, budget: 0 }, OUTSIDE_KEY, data)).toBe(0);
  });
  it("district view is not vanilla's city figure", () => {
    expect(serviceNet({ key: POWER, budget: -1234 }, NORTH, data)).not.toBe(-1234);
  });
});

describe("serviceCost (a service row shows only its cost)", () => {
  // NET -1234 = fees 900 + exports 300 - imports 434 - upkeep 2000.
  const power = { key: POWER, budget: -1234, fees: [fee({ incomeInternal: 900, incomeExports: 300 })] };
  it("city view: the NET less the fee revenue, i.e. upkeep and imports", () => {
    expect(serviceCost(power, CITY_KEY, data)).toBe(-2434);
    expect(serviceCost({ key: WATER, budget: -500 }, CITY_KEY, data)).toBe(-500);
  });
  it("city view: never the NET when the service has fee revenue", () => {
    expect(serviceCost(power, CITY_KEY, data)).not.toBe(-1234);
  });
  it("district view: the area's upkeep only, not its fee share", () => {
    expect(serviceCost(power, NORTH, data)).toBe(-3000);
    expect(serviceCost(power, NORTH, data)).not.toBe(600 - 3000);
    expect(serviceCost({ key: WATER, budget: 0 }, OUTSIDE_KEY, data)).toBe(0);
  });
});

describe("feeRowEstimate (a fee row shows its revenue, positive)", () => {
  it("city view: the fee's internal and export income", () => {
    expect(feeRowEstimate(fee({ incomeInternal: 900, incomeExports: 300 }), POWER, CITY_KEY, data)).toBe(1200);
    expect(feeRevenue(fee({ incomeInternal: 900 }))).toBe(900);
  });
  it("district view: the area's share of that fee, not the city's", () => {
    expect(feeRowEstimate(fee({ resource: 11 }), WATER, NORTH, data)).toBe(150);
    expect(feeRowEstimate(fee({ resource: 11 }), WATER, NORTH, data)).not.toBe(900);
    expect(feeRowEstimate(fee({ resource: 6 }), WATER, OUTSIDE_KEY, data)).toBe(0);
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
    const rows = services.map((s) => serviceNet(s, NORTH, data));
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

describe("visibleEstimates (the visible column adds up to the footer)", () => {
  const services = [
    { key: POWER, budget: -31191, fees: [fee({ incomeInternal: 14104 })] },
    { key: WATER, budget: -6766, fees: [fee({ resource: 6, incomeInternal: 6207 }), fee({ resource: 11, adjustable: false, incomeInternal: 500 })] },
  ];
  const sum = (column: (number | null)[]) => column.reduce<number>((t, v) => t + (v ?? 0), 0);
  it("the visible column adds up to the footer, city and district view", () => {
    expect(sum(visibleEstimates(services, CITY_KEY, data))).toBe(estimateTotal(services, CITY_KEY, data));
    expect(sum(visibleEstimates(services, NORTH, data))).toBe(estimateTotal(services, NORTH, data));
  });
  it("draws each service's cost, then each fee's revenue (a non-adjustable fee too), never the NET", () => {
    expect(visibleEstimates(services, CITY_KEY, data)).toEqual([-31191 - 14104, 14104, -6766 - 6207 - 500, 6207, 500]);
    expect(visibleEstimates(services, CITY_KEY, data)).not.toContain(-31191);
    expect(visibleEstimates(services, NORTH, data)).toEqual([-3000, 600, -1000, 250, 150]);
  });
});
