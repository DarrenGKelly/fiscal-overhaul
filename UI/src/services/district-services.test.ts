import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import { clampBudget, DistrictServiceBudget, isDistrictView, isOverridden, serviceKey, sliderValue, upkeepFor } from "./district-services";

const POLICE = "12:1";
const FIRE = "13:1";
const NORTH = "40:2";
const SOUTH = "41:1";

const rows: DistrictServiceBudget[] = [
  { service: POLICE, district: NORTH, budget: 120, upkeep: 3600 },
  { service: POLICE, district: SOUTH, budget: 0, upkeep: 2000 },
  { service: FIRE, district: OUTSIDE_KEY, budget: 70, upkeep: 0 },
];

describe("sliderValue", () => {
  it("an override shows its own %", () => {
    expect(sliderValue(rows, POLICE, NORTH, 100)).toBe(120);
    expect(sliderValue(rows, FIRE, OUTSIDE_KEY, 100)).toBe(70);
  });

  it("no override shows the city %", () => {
    expect(sliderValue(rows, POLICE, SOUTH, 90)).toBe(90); // a row with budget 0: follows the city
    expect(sliderValue(rows, FIRE, NORTH, 110)).toBe(110); // no row at all
    expect(sliderValue(null, POLICE, NORTH, 100)).toBe(100); // no data yet
  });

  it("an override is per service and per area", () => {
    expect(sliderValue(rows, FIRE, NORTH, 100)).not.toBe(120);
    expect(sliderValue(rows, POLICE, OUTSIDE_KEY, 100)).toBe(100);
  });
});

describe("isOverridden", () => {
  it("is true only for an area with its own %", () => {
    expect(isOverridden(rows, POLICE, NORTH)).toBe(true);
    expect(isOverridden(rows, FIRE, OUTSIDE_KEY)).toBe(true);
  });

  it("is false for a row following the city, a missing row or no data", () => {
    expect(isOverridden(rows, POLICE, SOUTH)).toBe(false);
    expect(isOverridden(rows, FIRE, SOUTH)).toBe(false);
    expect(isOverridden(undefined, POLICE, NORTH)).toBe(false);
  });
});

describe("upkeepFor", () => {
  it("is the area's own upkeep", () => {
    expect(upkeepFor(rows, POLICE, NORTH)).toBe(3600);
    expect(upkeepFor(rows, POLICE, SOUTH)).toBe(2000);
  });

  it("is 0 for an area without the service's buildings", () => {
    expect(upkeepFor(rows, FIRE, NORTH)).toBe(0);
    expect(upkeepFor(null, POLICE, NORTH)).toBe(0);
  });
});

describe("isDistrictView", () => {
  it("is true for a district and for Outside Districts", () => {
    expect(isDistrictView(NORTH)).toBe(true);
    expect(isDistrictView(OUTSIDE_KEY)).toBe(true);
  });

  it("is false for Entire City", () => {
    expect(isDistrictView(CITY_KEY)).toBe(false);
  });
});

describe("serviceKey", () => {
  it("is index:version, as the C# side's DistrictKey.Format", () => {
    expect(serviceKey({ index: 12, version: 1 })).toBe(POLICE);
  });

  it("is empty with no entity", () => {
    expect(serviceKey(null)).toBe("");
  });
});

describe("clampBudget", () => {
  it("keeps a whole % in 50-150", () => {
    expect(clampBudget(120)).toBe(120);
    expect(clampBudget(119.6)).toBe(120);
  });

  it("clamps out-of-range values and never sends 0 (which would clear)", () => {
    expect(clampBudget(10)).toBe(50);
    expect(clampBudget(400)).toBe(150);
    expect(clampBudget(NaN)).toBe(100);
  });
});
