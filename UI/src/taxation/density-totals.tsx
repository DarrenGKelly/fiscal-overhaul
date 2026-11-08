import { useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { createContext, useContext } from "react";
import { densityTaxes$, densityTaxesEnabled$ } from "bindings";
import { taxationPageClasses } from "vanilla";
import { TaxDistrictView } from "./district-tax-view";
import { headerSliderDisabled } from "./district-tax";
import { EngineWarningLine } from "./engine-warning-line";
import { densityExtraForArea, densityExtraTotal, estimatedTotalValue, zoneTotalIncome } from "./density";

// The Taxation tab's totals with the density offsets' extra revenue in them. Vanilla computes both totals in C#
// (TaxationUISystem's `areaTaxIncomes` and `taxIncome`) and draws them inside its own components, so the UI adds the
// extra to the drawn value: TaxationPage and TaxationGroup provide the extra through a context, and extended TaxSlider
// and NumberField components add it where vanilla draws a total. Outside those contexts both are vanilla as is.

/** The extra for the zone group being drawn (provided by the TaxationGroup wrapper in index.tsx). */
export const ZoneDensityExtra = createContext<number | null>(null);

/** The extra for ESTIMATED TOTAL (provided by the TaxationPage wrapper below). */
const TotalDensityExtra = createContext<number | null>(null);

/** The zone's extra, for the TaxationGroup wrapper to provide. */
export function useZoneDensityExtra(areaType: { index: number; locked: boolean }): number {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  return densityExtraForArea(data?.cells, areaType.index, areaType.locked, enabled);
}

/** TaxationPage, with every zone's extra provided for ESTIMATED TOTAL. */
export const withTotalDensityExtra = (TaxationPage: any) => (props: any) => {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  const areas = useValue(taxation.areaTypes$);
  return (
    <TotalDensityExtra.Provider value={densityExtraTotal(data?.cells, areas, enabled)}>
      <EngineWarningLine />
      <TaxationPage {...props} />
    </TotalDensityExtra.Provider>
  );
};

/**
 * TaxSlider: a zone group's header slider shows the zone total plus the zone's extra. In a district view
 * (district-tax-view.tsx) it shows the city rate read-only: the district's own slider is in its District section.
 */
export const withZoneDensityExtra = (TaxSlider: any) => (props: any) => {
  const extra = useContext(ZoneDensityExtra);
  const district = useContext(TaxDistrictView);
  const disabled = headerSliderDisabled(props.disabled, props.primary, district);
  return <TaxSlider {...props} income={zoneTotalIncome(props.income, props.primary, extra)} disabled={disabled} />;
};

/** NumberField: the ESTIMATED TOTAL field shows the total plus every zone's extra. */
export const withEstimatedTotalDensityExtra = (NumberField: any) => (props: any) => {
  const extra = useContext(TotalDensityExtra);
  return <NumberField {...props} value={estimatedTotalValue(props.value, props.className, taxationPageClasses?.totalValue, extra)} />;
};
