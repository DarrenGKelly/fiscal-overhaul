// Mapping between the game's tax area indices and the mod's density tax table (Mod/Logic/DensityTaxTable.cs).

/** Vanilla TaxAreaType: City = 0, Residential = 1, Commercial = 2, Industrial = 3, Office = 4. */
export function zoneForAreaIndex(areaIndex: number): number {
  return areaIndex >= 1 && areaIndex <= 4 ? areaIndex - 1 : -1;
}

/**
 * The density cells a tax group shows rows for: its zone's cells, or none for the city-wide group, a locked zone,
 * no data yet, or density taxes switched off in Options.
 */
export function cellsForArea<T extends { zone: number }>(cells: T[] | null | undefined, areaIndex: number, locked: boolean, enabled: boolean): T[] {
  const zone = zoneForAreaIndex(areaIndex);
  if (!enabled || locked || zone < 0 || !cells) return [];
  return cells.filter((c) => c.zone === zone);
}

const DENSITY_LABELS = ["Low density", "Medium density", "High density"];

export function densityLabel(density: number): string {
  return DENSITY_LABELS[density] ?? `Density ${density}`;
}
