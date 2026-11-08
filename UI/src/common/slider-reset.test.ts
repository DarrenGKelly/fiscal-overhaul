import { describe, expect, it } from "vitest";
import { markerFraction, markerLeft, relativeBox, resetClickable, resetVisibility } from "./slider-reset";

describe("resetVisibility", () => {
  it("shows the button while the row differs from its target", () => {
    expect(resetVisibility(true, "hide")).toBe("shown");
    expect(resetVisibility(true, "dim")).toBe("shown");
  });

  it("hides or dims it while the row is at its target", () => {
    expect(resetVisibility(false, "hide")).toBe("hidden");
    expect(resetVisibility(false, "dim")).toBe("dimmed");
    expect(resetVisibility(false, "dim")).not.toBe("shown");
  });

  it("is clickable only while shown", () => {
    expect(resetClickable("shown")).toBe(true);
    expect(resetClickable("dimmed")).toBe(false);
    expect(resetClickable("hidden")).toBe(false);
  });
});

describe("markerFraction", () => {
  it("places the target along the range", () => {
    expect(markerFraction(10, 0, 30)).toBeCloseTo(1 / 3);
    expect(markerFraction(0, -5, 10)).toBeCloseTo(1 / 3);
    expect(markerFraction(-5, -5, 10)).toBe(0);
    expect(markerFraction(10, -5, 10)).toBe(1);
  });

  it("gives no marker outside the range or for an empty one", () => {
    expect(markerFraction(31, 0, 30)).toBeNull();
    expect(markerFraction(-1, 0, 30)).toBeNull();
    expect(markerFraction(5, 5, 5)).toBeNull();
    expect(markerFraction(NaN, 0, 30)).toBeNull();
  });
});

describe("markerLeft", () => {
  const track = { left: 100, top: 4, width: 300, height: 20 };

  it("centres the marker on the fraction of the track", () => {
    expect(markerLeft(track, 0.5, 2)).toBe(249);
    expect(markerLeft(track, 0, 2)).toBe(99);
    expect(markerLeft(track, 0.5, 2)).not.toBe(250);
  });

  it("gives none without a fraction or a measured track", () => {
    expect(markerLeft(track, null, 2)).toBeNull();
    expect(markerLeft(null, 0.5, 2)).toBeNull();
    expect(markerLeft({ ...track, width: 0 }, 0.5, 2)).toBeNull();
  });

  it("measures the track relative to the frame", () => {
    expect(relativeBox({ left: 50, top: 10, width: 600, height: 48 }, { left: 150, top: 14, width: 300, height: 20 })).toEqual(track);
  });
});
