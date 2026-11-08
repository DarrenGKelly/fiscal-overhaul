import { describe, expect, it } from "vitest";
import EN_US from "../../../l10n/en-US.json";
import { Translate } from "../l10n";
import * as density from "./density";
import {
  arrowFor,
  cellsForArea,
  densityExtraForArea,
  densityExtraTotal,
  densityLabel,
  estimatedTotalValue,
  zoneForAreaIndex,
  zoneTotalIncome,
} from "./density";

const french: Translate = (id, fallback) =>
  ({
    "FiscalOverhaul.UI.DensityLow": "Faible densité",
    "FiscalOverhaul.UI.DensityOther": "Densité {NUMBER}",
  })[id] ??
  fallback ??
  null;

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
    expect(densityLabel(0, null)).toBe("Low density");
    expect(densityLabel(1, null)).toBe("Medium density");
    expect(densityLabel(2, null)).toBe("High density");
    expect(densityLabel(7, null)).toBe("Density 7");
  });

  it("reads the label through translate, English where it has none", () => {
    expect(densityLabel(0, french)).toBe("Faible densité");
    expect(densityLabel(0, french)).not.toBe("Low density");
    expect(densityLabel(7, french)).toBe("Densité 7");
    expect(densityLabel(2, french)).toBe("High density");
  });
});

describe("the Density tax header", () => {
  it("has no count summary: no helper and no summary text (removed by request)", () => {
    expect("rateSummary" in density).toBe(false);
    expect("offsetSummary" in density).toBe(false);
    for (const key of ["NoOffsets", "OneOffsetSet", "OffsetsSet", "OneRateSet", "RatesSet", "OneOverride", "Overrides"])
      expect(Object.keys(EN_US)).not.toContain(`FiscalOverhaul.UI.${key}`);
  });

  it("still has its title", () => {
    expect(EN_US["FiscalOverhaul.UI.SectionDensity"]).toBe("Density");
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

  it("counts a cell at the city's 0 whose payers pay a district's density rate (booked as charged)", () => {
    const districtOnly = [{ zone: 0, density: 1, offset: 0, estimate: 1200 }];
    expect(densityExtraForArea(districtOnly, 1, false, true)).toBe(1200);
    expect(densityExtraForArea([{ zone: 0, density: 1, offset: 0, estimate: 0 }], 1, false, true)).toBe(0);
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

describe("the district part in the totals", () => {
  // Commercial (area 2 = zone 1): a district's income-tax override adds 600 a month to what vanilla estimates.
  const parts = [0, 600, 0, 0];

  it("a zone total with a district override is vanilla plus the district part", () => {
    expect(density.zoneTotalIncome(5000, true, density.zoneExtra([], parts, 2, false, true))).toBe(5600);
    expect(density.zoneTotalIncome(5000, true, density.zoneExtra([], parts, 2, false, true))).not.toBe(5000);
    // With density taxes off too: the district part is charged whatever the density switch.
    expect(density.zoneExtra(totalsCells, parts, 2, false, false)).toBe(600);
  });

  it("adds it on top of the density extra, and only to its own zone", () => {
    expect(density.zoneExtra(totalsCells, parts, 2, false, true)).toBe(800 + 600);
    expect(density.zoneExtra(totalsCells, parts, 1, false, true)).toBe(21720 - 5000);
    expect(density.districtPartForArea(parts, 2, true)).toBe(0); // locked
    expect(density.districtPartForArea(parts, 0, false)).toBe(0); // the city-wide area
    expect(density.districtPartForArea(undefined, 2, false)).toBe(0);
  });

  it("ESTIMATED TOTAL adds every unlocked zone's part, matching the zone totals", () => {
    const withOffice = [0, 600, 0, 300];
    // Office (area 4) is locked in `areas`: its part isn't added, as its zone total isn't drawn.
    expect(density.totalExtra(totalsCells, withOffice, areas, true)).toBe(21720 - 5000 + 800 + 600);
    expect(density.totalExtra([], withOffice, areas, true)).not.toBe(900);
  });
});
