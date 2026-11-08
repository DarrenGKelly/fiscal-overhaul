// The zoning toolbar's icons for the density rows. Zone index and density follow Mod/Logic/DensityTaxTable.cs
// (zone: Residential 0, Commercial 1, Industrial 2, Office 3; density: Low 0, Medium 1, High 2). Drawn as <img src> of a
// game path, like vanilla's tax rows (inline SVG doesn't work in Gameface).

const ICON_DIR = "Media/Game/Icons/";

const ICONS: Record<string, string> = {
  "0-0": "ZoneResidentialLow",
  "0-1": "ZoneResidentialMedium",
  "0-2": "ZoneResidentialHigh",
  "1-0": "ZoneCommercialLow",
  "1-2": "ZoneCommercialHigh",
  "2-0": "ZoneIndustrial",
  "3-0": "ZoneOfficeLow",
  "3-2": "ZoneOfficeHigh",
};

/** The icon path for a zone + density cell, or null for a cell the game has no zoning icon for (draw no <img>). */
export function densityIcon(zone: number, density: number): string | null {
  const name = ICONS[`${zone}-${density}`];
  return name ? `${ICON_DIR}${name}.svg` : null;
}
