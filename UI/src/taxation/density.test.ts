import { describe, expect, it } from "vitest";
import { arrowFor, formatCount, cellsForArea, densityLabel, zoneForAreaIndex } from "./density";

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

describe("cellsForArea", () => {
  const cells = [
    { zone: 0, density: 0 },
    { zone: 0, density: 2 },
    { zone: 1, density: 0 },
  ];

  it("gives the area's zone cells while density taxes are enabled", () => {
    expect(cellsForArea(cells, 1, false, true)).toEqual([cells[0], cells[1]]);
    expect(cellsForArea(cells, 2, false, true)).toEqual([cells[2]]);
  });

  it("gives no cells while density taxes are disabled", () => {
    expect(cellsForArea(cells, 1, false, false)).toEqual([]);
    expect(cellsForArea(cells, 2, false, false)).toEqual([]);
  });

  it("gives no cells for a locked zone, the city-wide area or missing data", () => {
    expect(cellsForArea(cells, 1, true, true)).toEqual([]);
    expect(cellsForArea(cells, 0, false, true)).toEqual([]);
    expect(cellsForArea(null, 1, false, true)).toEqual([]);
  });
});

describe("arrowFor", () => {
  it("returns the vanilla glyph svg for both states", () => {
    expect(arrowFor(true)).toBe("Media/Glyphs/ThickStrokeArrowDown.svg");
    expect(arrowFor(false)).toBe("Media/Glyphs/ThickStrokeArrowRight.svg");
  });
  it("never returns a non-ASCII glyph", () => {
    for (const o of [true, false]) expect(/^[\x20-\x7e]+$/.test(arrowFor(o))).toBe(true);
  });
});

describe("formatCount", () => {
  it("groups thousands with commas", () => {
    expect(formatCount(1234567)).toBe("1,234,567");
    expect(formatCount(0)).toBe("0");
  });
  it("has no separator below 1000", () => {
    expect(formatCount(999)).toBe("999");
  });
});
