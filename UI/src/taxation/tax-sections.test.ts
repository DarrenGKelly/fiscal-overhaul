import { describe, expect, it } from "vitest";
import EN_US from "../../../l10n/en-US.json";
import { english } from "../l10n";
import * as density from "./density";
import { groupClasses, SectionExpandState, sectionsForArea, TAX_SECTIONS, TaxSectionDef } from "./tax-sections";

const RESIDENTIAL = 1;
const COMMERCIAL = 2;
const INDUSTRIAL = 3;
const OFFICE = 4;

describe("TAX_SECTIONS", () => {
  it("puts Density first", () => {
    expect(TAX_SECTIONS[0].id).toBe("density");
    expect(TAX_SECTIONS.map((s) => s.id)).toEqual(["density", "vanilla"]);
  });

  it("has an English title for every section in every zone", () => {
    for (const s of TAX_SECTIONS)
      for (const area of [RESIDENTIAL, COMMERCIAL, INDUSTRIAL, OFFICE]) expect(english(s.title(area))).toBeTruthy();
  });
});

describe("sectionsForArea", () => {
  it("gives Density then Education for Residential", () => {
    expect(sectionsForArea(RESIDENTIAL, false, { density: 3, vanilla: 5 })).toEqual([
      { id: "density", title: "SectionDensity" },
      { id: "vanilla", title: "SectionEducation" },
    ]);
    expect(english("SectionDensity")).toBe("Density");
    expect(english("SectionEducation")).toBe("Education");
  });

  it("names the other zones' vanilla rows Resources, not Education", () => {
    for (const area of [COMMERCIAL, INDUSTRIAL, OFFICE]) {
      const titles = sectionsForArea(area, false, { density: 2, vanilla: 4 }).map((s) => s.title);
      expect(titles).toEqual(["SectionDensity", "SectionResources"]);
      expect(titles).not.toContain("SectionEducation");
    }
  });

  it("leaves out a section with no rows, keeping the order of the rest", () => {
    expect(sectionsForArea(OFFICE, false, { density: 0, vanilla: 4 }).map((s) => s.id)).toEqual(["vanilla"]);
    expect(sectionsForArea(OFFICE, false, { vanilla: 4 }).map((s) => s.id)).toEqual(["vanilla"]);
    expect(sectionsForArea(RESIDENTIAL, false, { density: 3, vanilla: 0 }).map((s) => s.id)).toEqual(["density"]);
  });

  it("gives no sections for a locked zone or one with no rows", () => {
    expect(sectionsForArea(OFFICE, true, { density: 2, vanilla: 4 })).toEqual([]);
    expect(sectionsForArea(RESIDENTIAL, false, {})).toEqual([]);
  });

  it("takes a new tax type as one more entry, after Density", () => {
    const withNew = [...TAX_SECTIONS, { id: "land", title: () => "SectionResources" } as unknown as TaxSectionDef];
    const counts = { density: 1, vanilla: 1, land: 2 } as Record<string, number>;
    expect(sectionsForArea(RESIDENTIAL, false, counts, withNew).map((s) => s.id)).toEqual(["density", "vanilla", "land"]);
    expect(sectionsForArea(RESIDENTIAL, false, counts).map((s) => s.id)).toEqual(["density", "vanilla"]);
  });
});

describe("SectionExpandState", () => {
  it("starts every section collapsed", () => {
    const state = new SectionExpandState();
    expect(state.isOpen("Residential", "density")).toBe(false);
    expect(state.isOpen("Commercial", "vanilla")).toBe(false);
  });

  it("remembers a toggled section, per zone and per section", () => {
    const state = new SectionExpandState();
    expect(state.toggle("Residential", "density")).toBe(true);
    expect(state.isOpen("Residential", "density")).toBe(true);
    expect(state.isOpen("Residential", "vanilla")).toBe(false);
    expect(state.isOpen("Commercial", "density")).toBe(false);
    expect(state.toggle("Residential", "density")).toBe(false);
    expect(state.isOpen("Residential", "density")).toBe(false);
  });
});

describe("groupClasses", () => {
  const vanilla = { toggleButton: "toggle-button_hTU", hint: "hint_yGN", content: "content_NUa" };

  it("hides vanilla's expander while the sections are on", () => {
    expect(groupClasses(vanilla, "inv", "gone", true)).toEqual({
      toggleButton: "toggle-button_hTU inv",
      hint: "hint_yGN inv",
      content: "content_NUa gone",
    });
  });

  it("gives vanilla's names unchanged while they are off", () => {
    expect(groupClasses(vanilla, "inv", "gone", false)).toEqual(vanilla);
  });

  it("restores the names after being on (computed from vanilla's, not the patched ones)", () => {
    groupClasses(vanilla, "inv", "gone", true);
    expect(groupClasses(vanilla, "inv", "gone", false).toggleButton).toBe("toggle-button_hTU");
  });

  it("leaves a missing name missing", () => {
    expect(groupClasses({}, "inv", "gone", true)).toEqual({ toggleButton: undefined, hint: undefined, content: undefined });
  });
});

describe("density rows have no payer count", () => {
  it("has no payers text or helper any more", () => {
    expect(Object.keys(EN_US)).not.toContain("FiscalOverhaul.UI.Payers");
    expect("payersSuffix" in density).toBe(false);
  });

  it("still labels a row by its density alone", () => {
    expect(density.densityLabel(1, null)).toBe("Medium density");
  });
});
