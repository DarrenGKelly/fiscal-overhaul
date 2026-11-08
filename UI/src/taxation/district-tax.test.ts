import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import {
  densityOverrideCount,
  districtItemRate,
  districtOverride,
  hasDistrictOverrides,
  rowRate,
  showsTaxDistrictSelector,
  taxDistrictOptions,
  taxViewKey,
  TYPE_DENSITY,
  TYPE_RESOURCE_RATE,
  TYPE_ZONE_RATE,
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

describe("resetting a district", () => {
  it("has something to reset only while the district sets a value", () => {
    expect(hasDistrictOverrides(ROWS, "12:1")).toBe(true);
    expect(hasDistrictOverrides(ROWS, "40:2")).toBe(false);
    expect(hasDistrictOverrides(ROWS, CITY_KEY)).toBe(false);
  });

  it("follows the city once its overrides are gone", () => {
    const reset = ROWS.filter((r) => r.area !== "12:1");
    expect(rowRate({ type: TYPE_RESOURCE_RATE, zone: 0, key: 4 }, "12:1", reset, 12, 10, 0, 30)).toEqual({ rate: 12, overridden: false });
    expect(hasDistrictOverrides(reset, "12:1")).toBe(false);
  });
});

describe("densityOverrideCount", () => {
  const cells = [
    { zone: 0, density: 0, offset: 0 },
    { zone: 0, density: 1, offset: 0 },
    { zone: 0, density: 2, offset: 2 },
  ];

  it("counts the cells whose district rate differs from the city's", () => {
    expect(densityOverrideCount(ROWS, "12:1", cells)).toBe(1);
    expect(densityOverrideCount(ROWS, "40:2", cells)).toBe(0);
    // An override equal to the city's rate is not a difference.
    expect(densityOverrideCount([{ area: "40:2", type: TYPE_DENSITY, zone: 0, key: 2, value: 2 }], "40:2", cells)).toBe(0);
  });
});