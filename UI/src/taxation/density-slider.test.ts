import { describe, expect, it } from "vitest";
import { densitySliderValues, offsetFromSlider } from "./density-slider";

describe("densitySliderValues", () => {
  it("draws the zone rate plus the offset on a zone rate ± max slider", () => {
    expect(densitySliderValues(2, 5, 11, true)).toEqual({ rate: 13, min: 6, max: 16 });
    expect(densitySliderValues(-5, 5, 11, true)).toEqual({ rate: 6, min: 6, max: 16 });
    expect(densitySliderValues(0, 5, 11, true)).not.toEqual({ rate: 0, min: -5, max: 5 });
  });
  it("draws the bare offset when switched off or with no zone rate yet", () => {
    expect(densitySliderValues(2, 5, 11, false)).toEqual({ rate: 2, min: -5, max: 5 });
    expect(densitySliderValues(2, 5, null, true)).toEqual({ rate: 2, min: -5, max: 5 });
    expect(densitySliderValues(2, 5, undefined, true)).toEqual({ rate: 2, min: -5, max: 5 });
  });
});

describe("offsetFromSlider", () => {
  it("is the inverse of densitySliderValues", () => {
    for (const absolute of [true, false])
      for (let o = -5; o <= 5; o++) expect(offsetFromSlider(densitySliderValues(o, 5, 11, absolute).rate, 5, 11, absolute)).toBe(o);
  });
  it("backs the zone rate off, rounds and clamps", () => {
    expect(offsetFromSlider(13, 5, 11, true)).toBe(2);
    expect(offsetFromSlider(13.6, 5, 11, true)).toBe(3);
    expect(offsetFromSlider(30, 5, 11, true)).toBe(5);
    expect(offsetFromSlider(0, 5, 11, true)).toBe(-5);
    expect(offsetFromSlider(13, 5, 11, false)).toBe(5);
    expect(offsetFromSlider(13, 5, 11, true)).not.toBe(13);
  });
  it("never stores -0", () => {
    expect(Object.is(offsetFromSlider(10.6, 5, 11, true), 0)).toBe(true);
  });
});
