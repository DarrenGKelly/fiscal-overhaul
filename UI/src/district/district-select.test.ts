import { describe, expect, it } from "vitest";
import {
  CITY_KEY,
  cityLabel,
  cycle,
  districtLabel,
  districtOptions,
  dividerBefore,
  effectiveKey,
  OUTSIDE_KEY,
  showsDistrictSelector,
} from "./district-select";

describe("showsDistrictSelector", () => {
  it("shows for a city with no districts while recording is on (only Entire City)", () => {
    const options = districtOptions([]);
    expect(options.map((o) => o.key)).toEqual([CITY_KEY]);
    expect(showsDistrictSelector(true, options)).toBe(true);
  });

  it("shows with districts while recording is on", () => {
    expect(showsDistrictSelector(true, districtOptions(DISTRICTS))).toBe(true);
  });

  it("hides while recording is off, districts or not", () => {
    expect(showsDistrictSelector(false, districtOptions([]))).toBe(false);
    expect(showsDistrictSelector(false, districtOptions(DISTRICTS))).toBe(false);
  });
});

describe("cycle with only Entire City", () => {
  it("stays on Entire City both ways", () => {
    const options = districtOptions([]);
    expect(cycle(options, CITY_KEY, 1)).toBe(CITY_KEY);
    expect(cycle(options, CITY_KEY, -1)).toBe(CITY_KEY);
  });

  it("falls back to Entire City from a district that is gone", () => {
    expect(cycle(districtOptions([]), "12:1", 1)).toBe(CITY_KEY);
  });
});

const DISTRICTS = [
  { key: "12:1", name: "Downtown" },
  { key: "7:1", name: "Harbour" },
];

describe("cycle wraps forwards and back", () => {
  const options = districtOptions(DISTRICTS);

  it("steps through Entire City, the districts and Outside, wrapping at both ends", () => {
    expect(cycle(options, CITY_KEY, 1)).toBe("12:1");
    expect(cycle(options, "12:1", 1)).toBe("7:1");
    expect(cycle(options, "7:1", 1)).toBe(OUTSIDE_KEY);
    expect(cycle(options, OUTSIDE_KEY, 1)).toBe(CITY_KEY);
    expect(cycle(options, CITY_KEY, -1)).toBe(OUTSIDE_KEY);
    expect(cycle(options, "12:1", -1)).toBe(CITY_KEY);
  });

  it("an unknown key goes to Entire City, not a neighbour", () => {
    expect(cycle(options, "99:3", 1)).toBe(CITY_KEY);
    expect(cycle(options, "99:3", -1)).toBe(CITY_KEY);
  });

  it("a key no longer offered falls back to Entire City", () => {
    expect(effectiveKey(options, "7:1")).toBe("7:1");
    expect(effectiveKey(options, "99:3")).toBe(CITY_KEY);
    expect(effectiveKey(districtOptions([]), OUTSIDE_KEY)).toBe(CITY_KEY);
  });
});

describe("districtOptions order", () => {
  it("Entire City first, the districts in binding order, Outside last", () => {
    expect(districtOptions(DISTRICTS)).toEqual([
      { key: CITY_KEY, label: "Entire City" },
      { key: "12:1", label: "Downtown" },
      { key: "7:1", label: "Harbour" },
      { key: OUTSIDE_KEY, label: "Outside Districts" },
    ]);
  });

  it("with no districts, only Entire City (no Outside: the selector hides)", () => {
    expect(districtOptions([])).toEqual([{ key: CITY_KEY, label: "Entire City" }]);
  });

  it("puts the city first, labelled with the city's name", () => {
    const options = districtOptions(DISTRICTS, null, "Odiham");
    expect(options[0]).toEqual({ key: CITY_KEY, label: "Odiham" });
    expect(options.map((o) => o.label)).not.toContain("Entire City");
  });
});

describe("cityLabel", () => {
  it("is the city's name", () => {
    expect(cityLabel("Odiham")).toBe("Odiham");
    expect(cityLabel("  Odiham ")).toBe("Odiham");
  });

  it("falls back to Entire City while the name is blank", () => {
    expect(cityLabel("")).toBe("Entire City");
    expect(cityLabel("   ")).toBe("Entire City");
    expect(cityLabel(null)).toBe("Entire City");
  });

  it("falls back to the translated Entire City", () => {
    const translate = (id: string) => (id === "FiscalOverhaul.UI.EntireCity" ? "Toute la ville" : null);
    expect(cityLabel("", translate)).toBe("Toute la ville");
    expect(cityLabel("Odiham", translate)).toBe("Odiham");
  });
});

describe("dividerBefore", () => {
  it("draws a line after Entire City and before Outside Districts only", () => {
    const options = districtOptions(DISTRICTS); // Entire City, Downtown, Harbour, Outside
    expect(options.map((_, i) => dividerBefore(options, i))).toEqual([false, true, false, true]);
  });

  it("no line above the first option, between two districts, or out of range", () => {
    const options = districtOptions(DISTRICTS);
    expect(dividerBefore(options, 0)).toBe(false);
    expect(dividerBefore(options, 2)).toBe(false);
    expect(dividerBefore(options, 9)).toBe(false);
    expect(dividerBefore(districtOptions([]), 0)).toBe(false);
  });
});

describe("districtLabel", () => {
  it("keeps a player's name, spaces and dots included", () => {
    expect(districtLabel("Old Town", 2, null)).toBe("Old Town");
    expect(districtLabel("St. Mary's", 2, null)).toBe("St. Mary's");
  });

  it("a blank name becomes District <n>", () => {
    expect(districtLabel("", 3, null)).toBe("District 3");
    expect(districtLabel("  ", 3, null)).toBe("District 3");
  });

  it("a localization id is translated, or becomes District <n> when it has no translation", () => {
    const translate = (id: string) => (id === "Assets.DISTRICT_NAME[4]" ? "Riverside" : null);
    expect(districtLabel("Assets.DISTRICT_NAME[4]", 1, translate)).toBe("Riverside");
    expect(districtLabel("Assets.DISTRICT_NAME[9]", 5, translate)).toBe("District 5");
    expect(districtLabel("Assets.DISTRICT_NAME[9]", 5, null)).toBe("District 5");
  });
});
