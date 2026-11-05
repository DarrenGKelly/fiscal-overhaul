import { describe, expect, it } from "vitest";
import { Translate } from "../l10n";
import { arrowFor, formatCount, cellsForArea, densityLabel, offsetSummary, payersSuffix, zoneForAreaIndex } from "./density";

const french: Translate = (id, fallback) =>
  ({
    "FiscalOverhaul.UI.DensityLow": "Faible densité",
    "FiscalOverhaul.UI.DensityOther": "Densité {NUMBER}",
    "FiscalOverhaul.UI.OffsetsSet": "{COUNT} ajustements",
    "FiscalOverhaul.UI.Payers": "{COUNT} contribuables",
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

describe("offsetSummary", () => {
  it("reads as before in English", () => {
    expect(offsetSummary(0, null)).toBe("no offsets");
    expect(offsetSummary(1, null)).toBe("1 offset set");
    expect(offsetSummary(3, null)).toBe("3 offsets set");
  });

  it("fills the count into the translation", () => {
    expect(offsetSummary(3, french)).toBe("3 ajustements");
    expect(offsetSummary(0, french)).toBe("no offsets");
  });
});

describe("payersSuffix", () => {
  it("shows the count with separators, and nothing for no payers", () => {
    expect(payersSuffix(1234, null)).toBe(" · 1,234 payers");
    expect(payersSuffix(0, null)).toBe("");
  });

  it("uses the translation", () => {
    expect(payersSuffix(1234, french)).toBe(" · 1,234 contribuables");
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
