// Pure logic for the Taxation tab's information-only district demand. No game imports, so vitest runs it in Node.
// The game simulates ONE city-wide demand per zone; a district has none. This estimates what the city demand would be
// if the whole city paid the district's rate, using the same tax-effect weights vanilla's demand systems use.

/** DemandParameterData.m_TaxEffect: x residential, y commercial, z industrial and office. */
export interface TaxEffect {
  x: number;
  y: number;
  z: number;
}

/** Demand (0..1) lost per tax point, per unit of weight. Provisional estimates, one constant each. */
export const RESIDENTIAL_DEMAND_PER_POINT = 0.01; // vanilla: factor = x * (10 - rate), added to a 0..100 demand
export const OTHER_DEMAND_PER_POINT = 0.05; // vanilla: -0.05 * (rate - 10) * weight

/** The tax-effect weight for a zone (0 Residential, 1 Commercial, 2 Industrial, 3 Office); 0 for an unknown zone. */
export function taxEffectFor(zone: number, effect: TaxEffect | null | undefined): number {
  if (!effect) return 0;
  switch (zone) {
    case 0:
      return effect.x;
    case 1:
      return effect.y;
    case 2:
    case 3:
      return effect.z;
    default:
      return 0;
  }
}

/**
 * The city demand (0..1) shifted by a district's adjustment: +N points on the rate lowers the tax demand factor by
 * N x weight x scale; clamped to 0..1. Info only. A negative adjustment raises it.
 */
export function districtDemand(cityDemand: number, zone: number, districtAdj: number, taxEffect: TaxEffect | null | undefined): number {
  const perPoint = zone === 0 ? RESIDENTIAL_DEMAND_PER_POINT : OTHER_DEMAND_PER_POINT;
  const shift = -districtAdj * taxEffectFor(zone, taxEffect) * perPoint;
  return Math.max(0, Math.min(1, cityDemand + shift));
}

/** A demand as a whole percent: "37%". */
export function demandPercent(demand: number): string {
  return `${Math.round(demand * 100)}%`;
}

/** The city demand for a zone from the vanilla cityInfo values (residential: the mean of low/medium/high). */
export function cityDemandFor(
  zone: number,
  d: { resLow: number; resMed: number; resHigh: number; commercial: number; industrial: number; office: number },
): number {
  switch (zone) {
    case 0:
      return (d.resLow + d.resMed + d.resHigh) / 3;
    case 1:
      return d.commercial;
    case 2:
      return d.industrial;
    case 3:
      return d.office;
    default:
      return 0;
  }
}
