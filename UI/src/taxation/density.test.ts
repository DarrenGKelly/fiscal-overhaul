import { describe, expect, it } from "vitest";
import {
  arrowFor,
  formatCount,
  cellsForArea,
  densityExtraForArea,
  densityExtraTotal,
  densityLabel,
  estimatedTotalValue,
  zoneForAreaIndex,
  zoneTotalIncome,
} from "./density";

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

// Residential (area 1 = zone 0): Medium +3 earns 21720, High −2 loses 5000; Low has no offset (its estimate is 0).
// Commercial (area 2 = zone 1): Low +1 earns 800.
const totalsCells = [
  { zone: 0, density: 0, offset: 0, estimate: 0 },
  { zone: 0, density: 1, offset: 3, estimate: 21720 },
  { zone: 0, density: 2, offset: -2, estimate: -5000 },
  { zone: 1, density: 0, offset: 1, estimate: 800 },
];
const areas = [
  { index: 0, locked: false }, // City
  { index: 1, locked: false },
  { index: 2, locked: false },
  { index: 3, locked: false },
  { index: 4, locked: true }, // Office not unlocked yet
];

describe("densityExtraForArea", () => {
  it("is the sum of the zone's offset estimates, negatives lowering it", () => {
    expect(densityExtraForArea(totalsCells, 1, false, true)).toBe(21720 - 5000);
    expect(densityExtraForArea(totalsCells, 2, false, true)).toBe(800);
  });

  it("is 0 while density taxes are off", () => {
    expect(densityExtraForArea(totalsCells, 1, false, false)).toBe(0);
    expect(densityExtraForArea(totalsCells, 2, false, false)).toBe(0);
  });

  it("is 0 with every offset 0, even if a stale estimate is left", () => {
    const zeroed = [{ zone: 0, density: 1, offset: 0, estimate: 21720 }];
    expect(densityExtraForArea(zeroed, 1, false, true)).toBe(0);
  });

  it("is 0 for the city-wide area, a locked zone, a zone without offsets, or no data", () => {
    expect(densityExtraForArea(totalsCells, 0, false, true)).toBe(0);
    expect(densityExtraForArea(totalsCells, 1, true, true)).toBe(0);
    expect(densityExtraForArea(totalsCells, 3, false, true)).toBe(0);
    expect(densityExtraForArea(null, 1, false, true)).toBe(0);
  });
});

describe("densityExtraTotal", () => {
  it("is the sum of every zone's extra", () => {
    expect(densityExtraTotal(totalsCells, areas, true)).toBe(21720 - 5000 + 800);
  });

  it("can be negative when offsets below 0 outweigh the rest", () => {
    const cuts = [
      { zone: 0, density: 0, offset: -5, estimate: -9000 },
      { zone: 1, density: 0, offset: 1, estimate: 400 },
    ];
    expect(densityExtraTotal(cuts, areas, true)).toBe(-8600);
  });

  it("is 0 while density taxes are off, and with no areas or data yet", () => {
    expect(densityExtraTotal(totalsCells, areas, false)).toBe(0);
    expect(densityExtraTotal(totalsCells, [], true)).toBe(0);
    expect(densityExtraTotal(totalsCells, null, true)).toBe(0);
    expect(densityExtraTotal(undefined, areas, true)).toBe(0);
  });

  it("leaves out a locked zone", () => {
    const office = [{ zone: 3, density: 0, offset: 2, estimate: 300 }];
    expect(densityExtraTotal(office, areas, true)).toBe(0);
    expect(densityExtraTotal(office, [{ index: 4, locked: false }], true)).toBe(300);
  });
});

describe("zoneTotalIncome", () => {
  it("adds the zone's extra to the header (primary) slider's income", () => {
    expect(zoneTotalIncome(100000, true, 21720)).toBe(121720);
    expect(zoneTotalIncome(100000, true, -5000)).toBe(95000);
  });

  it("can turn the total negative, which vanilla's NumberField signs", () => {
    expect(zoneTotalIncome(1000, true, -3000)).toBe(-2000);
  });

  it("leaves item sliders and a zone without extra exactly vanilla", () => {
    expect(zoneTotalIncome(4000, false, 21720)).toBe(4000);
    expect(zoneTotalIncome(4000, undefined, 21720)).toBe(4000);
    expect(zoneTotalIncome(100000, true, 0)).toBe(100000);
    expect(zoneTotalIncome(100000, true, null)).toBe(100000);
  });
});

describe("estimatedTotalValue", () => {
  const total = "total-value_hzh";

  it("adds the extra to the ESTIMATED TOTAL field", () => {
    expect(estimatedTotalValue(500000, total, total, 17520)).toBe(517520);
    expect(estimatedTotalValue(500000, total, total, -8600)).toBe(491400);
  });

  it("leaves ESTIMATED TOTAL exactly vanilla with no extra (density taxes off or every offset 0)", () => {
    expect(estimatedTotalValue(500000, total, total, 0)).toBe(500000);
    expect(estimatedTotalValue(500000, total, total, null)).toBe(500000);
  });

  it("leaves every other money field alone", () => {
    expect(estimatedTotalValue(4000, "estimate-value_x", total, 17520)).toBe(4000);
    expect(estimatedTotalValue(4000, undefined, total, 17520)).toBe(4000);
  });

  it("changes nothing if the vanilla class can't be found (a renamed module after a patch)", () => {
    expect(estimatedTotalValue(4000, undefined, undefined, 17520)).toBe(4000);
  });
});
