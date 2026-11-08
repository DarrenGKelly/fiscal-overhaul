import { describe, expect, it } from "vitest";
import { densityIcon } from "./density-icons";

const SUPPORTED = [[0, 0], [0, 1], [0, 2], [1, 0], [1, 2], [2, 0], [3, 0], [3, 2]];

describe("densityIcon", () => {
  it("maps every supported density cell to a zone icon", () => {
    for (const [z, d] of SUPPORTED) expect(densityIcon(z, d)).toMatch(/^Media\/Game\/Icons\/Zone\w+\.svg$/);
    expect(densityIcon(0, 2)).toBe("Media/Game/Icons/ZoneResidentialHigh.svg");
    expect(densityIcon(3, 0)).toBe("Media/Game/Icons/ZoneOfficeLow.svg");
  });
  it("gives each cell its own icon", () => {
    expect(new Set(SUPPORTED.map(([z, d]) => densityIcon(z, d))).size).toBe(SUPPORTED.length);
  });
  it("gives none for an unknown cell", () => {
    expect(densityIcon(1, 1)).toBeNull();
    expect(densityIcon(9, 0)).toBeNull();
    expect(densityIcon(0, 7)).toBeNull();
    expect(densityIcon(-1, -1)).toBeNull();
  });
});
