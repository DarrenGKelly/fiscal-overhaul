import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import {
  clampAdjustment,
  districtAdjustment,
  headerSliderDisabled,
  showsTaxDistrictSelector,
  signedPoints,
  taxDistrictOptions,
  taxViewKey,
  vsCityLabel,
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

describe("districtAdjustment", () => {
  const rows = [
    { key: "12:1", zone: 0, value: 5 },
    { key: "12:1", zone: 3, value: -2 },
  ];

  it("reads the district's row for the zone", () => {
    expect(districtAdjustment(rows, "12:1", 0)).toBe(5);
    expect(districtAdjustment(rows, "12:1", 3)).toBe(-2);
  });

  it("is 0 for a zone, district or table with no row", () => {
    expect(districtAdjustment(rows, "12:1", 1)).toBe(0);
    expect(districtAdjustment(rows, "40:2", 0)).toBe(0);
    expect(districtAdjustment(null, "12:1", 0)).toBe(0);
  });
});

describe("labels and values", () => {
  it("signs the adjustment: +5% vs city, -3% vs city, 0% vs city", () => {
    expect(vsCityLabel(5, null)).toBe("+5% vs city");
    expect(vsCityLabel(-3, null)).toBe("-3% vs city");
    expect(vsCityLabel(0, null)).toBe("0% vs city");
    expect(signedPoints(10)).toBe("+10");
  });

  it("rounds and clamps a slider value to the cap", () => {
    expect(clampAdjustment(4.6, 10)).toBe(5);
    expect(clampAdjustment(14, 10)).toBe(10);
    expect(clampAdjustment(-14, 10)).toBe(-10);
  });
});

describe("headerSliderDisabled", () => {
  it("makes a zone's header slider read-only in a district view", () => {
    expect(headerSliderDisabled(false, true, "12:1")).toBe(true);
  });

  it("leaves it as vanilla says in the Entire City view, and other sliders alone", () => {
    expect(headerSliderDisabled(false, true, CITY_KEY)).toBe(false);
    expect(headerSliderDisabled(true, true, CITY_KEY)).toBe(true);
    expect(headerSliderDisabled(undefined, false, "12:1")).toBe(false);
  });
});
