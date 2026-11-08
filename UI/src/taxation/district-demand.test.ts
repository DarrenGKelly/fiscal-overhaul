import { describe, expect, it } from "vitest";
import { cityDemandFor, demandPercent, districtDemand, districtRateDelta, taxEffectFor } from "./district-demand";
import { TYPE_RESOURCE_RATE, TYPE_ZONE_RATE } from "./district-tax";

const effect = { x: 1, y: 1, z: 1 };

describe("districtDemand", () => {
  it("adj 0 equals the city demand", () => {
    for (const zone of [0, 1, 2, 3]) expect(districtDemand(0.4, zone, 0, effect)).toBe(0.4);
  });
  it("a higher rate is never higher than the city, a lower rate never lower", () => {
    for (const zone of [0, 1, 2, 3]) {
      expect(districtDemand(0.5, zone, 5, effect)).toBeLessThan(0.5);
      expect(districtDemand(0.5, zone, -5, effect)).toBeGreaterThan(0.5);
    }
  });
  it("scales with the tax-effect weight", () => {
    expect(districtDemand(0.5, 1, 5, { x: 1, y: 2, z: 1 })).toBeLessThan(districtDemand(0.5, 1, 5, effect));
  });
  it("clamps to 0..1", () => {
    expect(districtDemand(0.05, 1, 10, { x: 1, y: 3, z: 1 })).toBe(0);
    expect(districtDemand(0.95, 1, -10, { x: 1, y: 3, z: 1 })).toBe(1);
  });
  it("no weights known means no shift", () => {
    expect(districtDemand(0.4, 0, 5, null)).toBe(0.4);
  });
});

describe("helpers", () => {
  it("picks the weight per zone", () => {
    expect(taxEffectFor(0, { x: 1, y: 2, z: 3 })).toBe(1);
    expect(taxEffectFor(1, { x: 1, y: 2, z: 3 })).toBe(2);
    expect(taxEffectFor(3, { x: 1, y: 2, z: 3 })).toBe(3);
    expect(taxEffectFor(9, { x: 1, y: 2, z: 3 })).toBe(0);
  });
  it("city demand for residential is the mean", () => {
    const d = { resLow: 0.3, resMed: 0.6, resHigh: 0, commercial: 0.2, industrial: 0.1, office: 0.7 };
    expect(cityDemandFor(0, d)).toBeCloseTo(0.3);
    expect(cityDemandFor(3, d)).toBe(0.7);
  });
  it("formats a percent", () => {
    expect(demandPercent(0.374)).toBe("37%");
  });
});

describe("districtRateDelta", () => {
  // District "12:1" sets its Commercial zone rate to 15; the city's is 10.
  const rows = [
    { area: "12:1", type: TYPE_ZONE_RATE, zone: 1, key: 0, value: 15 },
    { area: "12:1", type: TYPE_RESOURCE_RATE, zone: 0, key: 4, value: 10 },
  ];
  it("is the district's own zone rate minus the city's", () => {
    expect(districtRateDelta(rows, "12:1", 1, 10)).toBe(5);
    expect(districtDemand(0.5, 1, districtRateDelta(rows, "12:1", 1, 10), effect)).toBeLessThan(0.5);
  });
  it("is 0 where the district follows the city's zone rate", () => {
    expect(districtRateDelta(rows, "12:1", 0, 10)).toBe(0); // only an education override, not the zone rate
    expect(districtRateDelta(rows, "40:2", 1, 10)).toBe(0);
    expect(districtDemand(0.5, 1, districtRateDelta(rows, "40:2", 1, 10), effect)).toBe(0.5);
  });
});