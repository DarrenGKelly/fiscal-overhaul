import { describe, expect, it } from "vitest";
import { densityLabel, zoneForAreaIndex } from "./density";

describe("zoneForAreaIndex", () => {
  it("maps the four zoned tax areas to the C# TaxZone order", () => {
    expect(zoneForAreaIndex(1)).toBe(0); // Residential
    expect(zoneForAreaIndex(2)).toBe(1); // Commercial
    expect(zoneForAreaIndex(3)).toBe(2); // Industrial
    expect(zoneForAreaIndex(4)).toBe(3); // Office
  });

  it("has no density rows for the city-wide area or unknown ones", () => {
    expect(zoneForAreaIndex(0)).toBe(-1);
    expect(zoneForAreaIndex(5)).toBe(-1);
  });
});

describe("densityLabel", () => {
  it("names the three densities", () => {
    expect(densityLabel(0)).toBe("Low density");
    expect(densityLabel(2)).toBe("High density");
    expect(densityLabel(7)).toBe("Density 7");
  });
});
