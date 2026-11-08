import { describe, expect, it } from "vitest";
import { DENSITY_RATE_RANGE, densitySliderValues, rateFromSlider } from "./density-slider";

describe("densitySliderValues", () => {
  it("shows the density tax's own rate, never the zone rate plus it", () => {
    expect(densitySliderValues(3).rate).toBe(3);
    expect(densitySliderValues(0).rate).toBe(0);
    expect(densitySliderValues(3).rate).not.toBe(14); // a zone rate of 11 + 3
  });
  it("spans -5..+10, matching the C# density cap (TaxRateTable.DensityMin/DensityMax)", () => {
    expect(DENSITY_RATE_RANGE).toEqual({ min: -5, max: 10 });
    expect(DENSITY_RATE_RANGE).not.toEqual({ min: -5, max: 5 });
  });
  it("spans the one range constant", () => {
    expect(densitySliderValues(3)).toEqual({ rate: 3, min: DENSITY_RATE_RANGE.min, max: DENSITY_RATE_RANGE.max });
    expect(densitySliderValues(3, { min: -5, max: 10 })).toEqual({ rate: 3, min: -5, max: 10 });
    expect(densitySliderValues(3, { min: -5, max: 10 })).not.toEqual({ rate: 3, min: -5, max: 5 });
  });
});

describe("rateFromSlider", () => {
  it("is the inverse of densitySliderValues across the range", () => {
    for (let r = DENSITY_RATE_RANGE.min; r <= DENSITY_RATE_RANGE.max; r++) expect(rateFromSlider(densitySliderValues(r).rate)).toBe(r);
  });
  it("rounds and keeps the rate in the range", () => {
    expect(rateFromSlider(2.6)).toBe(3);
    expect(rateFromSlider(2.6)).not.toBe(2.6);
    expect(rateFromSlider(30)).toBe(DENSITY_RATE_RANGE.max);
    expect(rateFromSlider(-30)).toBe(DENSITY_RATE_RANGE.min);
    expect(rateFromSlider(8, { min: -5, max: 10 })).toBe(8);
  });
  it("never stores -0", () => {
    expect(Object.is(rateFromSlider(-0.4), 0)).toBe(true);
  });
});
