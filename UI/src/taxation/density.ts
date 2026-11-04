// Mapping between the game's tax area indices and the mod's density tax table (Mod/Logic/DensityTaxTable.cs).

/** Vanilla TaxAreaType: City = 0, Residential = 1, Commercial = 2, Industrial = 3, Office = 4. */
export function zoneForAreaIndex(areaIndex: number): number {
  return areaIndex >= 1 && areaIndex <= 4 ? areaIndex - 1 : -1;
}

const DENSITY_LABELS = ["Low density", "Medium density", "High density"];

export function densityLabel(density: number): string {
  return DENSITY_LABELS[density] ?? `Density ${density}`;
}
