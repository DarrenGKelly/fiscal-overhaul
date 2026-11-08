import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import {
  districtItemRate,
  districtOverride,
  districtRowEstimate,
  districtTotalEstimate,
  rowRate,
  rowReset,
  showsTaxDistrictSelector,
  taxDistrictOptions,
  taxViewKey,
  TYPE_DENSITY,
  TYPE_RESOURCE_RATE,
  TYPE_ZONE_RATE,
  VANILLA_TAX_DEFAULTS,
} from "./district-tax";

const DISTRICTS = [
  { key: "12:1", name: "Harbour" },
  { key: "40:2", name: "Old Town" },
];

describe("taxDistrictOptions", () => {
  it("gives Entire City then each district, without Outside Districts", () => {
    expect(taxDistrictOptions(DISTRICTS).map((o) => o.key)).toEqual([CITY_KEY, "12:1", "40:2"]);
    expect(taxDistrictOptions(DISTRICTS).map((o) => o.key)).not.toContain(OUTSIDE_KEY);
    expect(taxDistrictOptions(DISTRICTS)[0].label).toBe("Entire City");
  });

  it("gives only Entire City for a city without districts", () => {
    expect(taxDistrictOptions([]).map((o) => o.key)).toEqual([CITY_KEY]);
  });
});

describe("showsTaxDistrictSelector", () => {
  it("shows while the engine charges and a district exists", () => {
    expect(showsTaxDistrictSelector(true, taxDistrictOptions(DISTRICTS))).toBe(true);
  });

  it("hides with the engine off (vanilla tab) or no district", () => {
    expect(showsTaxDistrictSelector(false, taxDistrictOptions(DISTRICTS))).toBe(false);
    expect(showsTaxDistrictSelector(true, taxDistrictOptions([]))).toBe(false);
  });
});

describe("taxViewKey", () => {
  const options = taxDistrictOptions(DISTRICTS);

  it("is the picked district while the selector shows", () => {
    expect(taxViewKey(true, options, "40:2")).toBe("40:2");
  });

  it("is Entire City with the engine off, for Outside Districts, or for a deleted district", () => {
    expect(taxViewKey(false, options, "40:2")).toBe(CITY_KEY);
    expect(taxViewKey(true, options, OUTSIDE_KEY)).toBe(CITY_KEY);
    expect(taxViewKey(true, options, "99:1")).toBe(CITY_KEY);
    expect(taxViewKey(true, taxDistrictOptions([]), "40:2")).toBe(CITY_KEY);
  });
});

// District A ("12:1") sets its Residential zone rate to 13, Highly Educated (level 4) to an offset of +10, and High
// density tax to 5. District B ("40:2") sets nothing. The city: Residential zone 10, level 4 at 12, level 0 at 8,
// High density tax 2.
const ROWS = [
  { area: "12:1", type: TYPE_RESOURCE_RATE, zone: 0, key: 4, value: 10 },
  { area: "12:1", type: TYPE_DENSITY, zone: 0, key: 2, value: 5 },
];
const WITH_ZONE = [...ROWS, { area: "12:1", type: TYPE_ZONE_RATE, zone: 0, key: 0, value: 13 }];

describe("which value a district row shows", () => {
  it("shows the district's own value where it sets one, marked as an override", () => {
    expect(rowRate({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, "12:1", ROWS, 12, 10, 0, 30)).toEqual({ rate: 20, overridden: true });
    expect(rowRate({ type: TYPE_DENSITY, zone: 0, key: 2 }, "12:1", ROWS, 2, 10, -5, 10)).toEqual({ rate: 5, overridden: true });
    expect(rowRate({ type: TYPE_ZONE_RATE, zone: 0, key: 0 }, "12:1", WITH_ZONE, 10, 10, 0, 30)).toEqual({ rate: 13, overridden: true });
  });

  it("shows the city's value, unmarked, where it sets none", () => {
    expect(rowRate({ type: TYPE_RESOURCE_RATE, zone: 0, key: 0 }, "12:1", ROWS, 8, 10, 0, 30)).toEqual({ rate: 8, overridden: false });
    expect(rowRate({ type: TYPE_DENSITY, zone: 0, key: 2 }, "40:2", ROWS, 2, 10, -5, 10)).toEqual({ rate: 2, overridden: false });
    expect(rowRate({ type: TYPE_ZONE_RATE, zone: 0, key: 0 }, "12:1", ROWS, 10, 10, 0, 30)).toEqual({ rate: 10, overridden: false });
  });

  it("shows the city's values in the Entire City view, whatever the districts set", () => {
    expect(rowRate({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, CITY_KEY, ROWS, 12, 10, 0, 30)).toEqual({ rate: 12, overridden: false });
    expect(rowRate({ type: TYPE_DENSITY, zone: 0, key: 2 }, CITY_KEY, ROWS, 2, 10, -5, 10).rate).not.toBe(5);
  });

  it("moves every level with a district zone rate, like vanilla's zone slider (offsets kept)", () => {
    // Level 0: 13 + city offset -2 = 11; level 4: 13 + its own offset 10 = 23.
    expect(districtItemRate(WITH_ZONE, "12:1", 0, 0, 10, 8, 0, 30)).toBe(11);
    expect(districtItemRate(WITH_ZONE, "12:1", 0, 4, 10, 12, 0, 30)).toBe(23);
    expect(districtItemRate(WITH_ZONE, "12:1", 0, 4, 10, 12, 0, 20)).toBe(20); // vanilla's limit
    expect(districtItemRate(ROWS, "40:2", 0, 0, 10, 8, 0, 30)).toBe(8);
  });

  it("reads overrides by the district's current key only", () => {
    expect(districtOverride(ROWS, "12:1", TYPE_DENSITY, 0, 2)).toBe(5);
    expect(districtOverride(ROWS, "12:2", TYPE_DENSITY, 0, 2)).toBeUndefined(); // another load's key
    expect(districtOverride(ROWS, CITY_KEY, TYPE_DENSITY, 0, 2)).toBeUndefined();
    expect(districtOverride(null, "12:1", TYPE_DENSITY, 0, 2)).toBeUndefined();
  });
});

describe("a row's reset in the Entire City view", () => {
  const DEFAULTS = { zoneRate: 10, itemOffset: 0, density: 0 };

  it("goes back to vanilla's default: zone 10, a level/resource to its zone's rate, density 0", () => {
    expect(rowReset({ type: TYPE_ZONE_RATE, zone: 1, key: 0 }, CITY_KEY, ROWS, 14, 14, 0, 30, DEFAULTS)).toEqual({ target: 10, differs: true, kind: "default" });
    expect(rowReset({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, CITY_KEY, ROWS, 12, 11, 0, 30, DEFAULTS)).toEqual({ target: 11, differs: true, kind: "default" });
    expect(rowReset({ type: TYPE_DENSITY, zone: 0, key: 2 }, CITY_KEY, ROWS, 2, 10, -5, 10, DEFAULTS)).toEqual({ target: 0, differs: true, kind: "default" });
  });

  it("has nothing to reset at the default, and ignores the districts' overrides", () => {
    expect(rowReset({ type: TYPE_ZONE_RATE, zone: 1, key: 0 }, CITY_KEY, WITH_ZONE, 10, 10, 0, 30, DEFAULTS).differs).toBe(false);
    expect(rowReset({ type: TYPE_DENSITY, zone: 0, key: 2 }, CITY_KEY, ROWS, 0, 10, -5, 10, DEFAULTS).differs).toBe(false);
    expect(rowReset({ type: TYPE_ZONE_RATE, zone: 1, key: 0 }, CITY_KEY, ROWS, 14, 14, 0, 30).target).toBe(VANILLA_TAX_DEFAULTS.zoneRate);
  });

  it("keeps the target inside the row's limits", () => {
    expect(rowReset({ type: TYPE_ZONE_RATE, zone: 1, key: 0 }, CITY_KEY, ROWS, 14, 14, 12, 30, DEFAULTS).target).toBe(12);
  });
});

describe("a row's reset in a district view", () => {
  it("clears the district's own value: the target is the city's, shown only while an override is stored", () => {
    expect(rowReset({ type: TYPE_DENSITY, zone: 0, key: 2 }, "12:1", ROWS, 2, 10, -5, 10)).toEqual({ target: 2, differs: true, kind: "city" });
    expect(rowReset({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, "12:1", ROWS, 12, 10, 0, 30)).toEqual({ target: 12, differs: true, kind: "city" });
    expect(rowReset({ type: TYPE_RESOURCE_RATE, zone: 0, key: 0 }, "12:1", ROWS, 8, 10, 0, 30).differs).toBe(false);
    expect(rowReset({ type: TYPE_DENSITY, zone: 0, key: 2 }, "40:2", ROWS, 2, 10, -5, 10).differs).toBe(false);
  });

  it("under a district zone override, a level's target is the district zone rate plus the city's offset", () => {
    // Level 4 without its own offset: 13 + (12 - 10) = 15, not the city's 12.
    expect(rowReset({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, "12:1", WITH_ZONE, 12, 10, 0, 30).target).toBe(15);
    expect(rowReset({ type: TYPE_ZONE_RATE, zone: 0, key: 0 }, "12:1", WITH_ZONE, 10, 10, 0, 30)).toEqual({ target: 10, differs: true, kind: "city" });
  });
});

describe("a district view's estimates", () => {
  const DATA = {
    area: "12:1",
    items: [{ zone: 0, key: 4, estimate: 300 }],
    density: [{ zone: 0, density: 2, estimate: -40 }],
    zones: [260, 0, 0, 0],
    total: 260,
  };

  it("gives each row, zone and the total the district's own estimate", () => {
    expect(districtRowEstimate(DATA, "12:1", { type: TYPE_RESOURCE_RATE, zone: 0, key: 4 })).toBe(300);
    expect(districtRowEstimate(DATA, "12:1", { type: TYPE_DENSITY, zone: 0, key: 2 })).toBe(-40);
    expect(districtRowEstimate(DATA, "12:1", { type: TYPE_ZONE_RATE, zone: 0, key: 0 })).toBe(260);
    expect(districtTotalEstimate(DATA, "12:1")).toBe(260);
    expect(districtRowEstimate(DATA, "12:1", { type: TYPE_RESOURCE_RATE, zone: 0, key: 0 })).toBe(0);
  });

  it("gives 0 until the estimates are for the shown district, and none in the city view", () => {
    expect(districtRowEstimate(DATA, "40:2", { type: TYPE_RESOURCE_RATE, zone: 0, key: 4 })).toBe(0);
    expect(districtTotalEstimate(DATA, "40:2")).toBe(0);
    expect(districtTotalEstimate(DATA, CITY_KEY)).toBe(0);
    expect(districtTotalEstimate(null, "12:1")).toBe(0);
  });
});
