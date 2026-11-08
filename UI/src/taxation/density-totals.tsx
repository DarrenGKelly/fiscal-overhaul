import { useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { createContext, useContext } from "react";
import { densityTaxes$, densityTaxesEnabled$ } from "bindings";
import { taxationPageClasses } from "vanilla";
import { CITY_KEY } from "district/district-select";
import { TaxDistrictView, TaxRowTarget, useDistrictSlider } from "./district-tax-view";
import { TYPE_ZONE_RATE } from "./district-tax";
import districtStyles from "./district-tax-view.module.scss";
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
 * (district-tax-view.tsx) the header and each re-hosted education/resource row (TaxRowTarget) show and set the
 * district's own value, marked where it differs from the city's, with no estimate (the estimates are the city's).
 */
export const withZoneDensityExtra = (TaxSlider: any) => (props: any) => {
  const extra = useContext(ZoneDensityExtra);
  const area = useContext(TaxDistrictView);
  const target = useContext(TaxRowTarget);
  // The group's target (a zone rate) is the header's only; a row's target is its own slider's.
  const applies = !!target && (target.type === TYPE_ZONE_RATE) === !!props.primary;
  const district = useDistrictSlider(applies ? target : null, props.rate, props.min, props.max);
  if (area === CITY_KEY || !district.onChange) {
    return <TaxSlider {...props} income={zoneTotalIncome(props.income, props.primary, extra)} />;
  }
  const className = [props.className, districtStyles.noEstimate, district.overridden ? districtStyles.overridden : null].filter(Boolean).join(" ");
  return <TaxSlider {...props} rate={district.rate} income={0} className={className} onValueChanged={district.onChange} />;
};

/** NumberField: the ESTIMATED TOTAL field shows the total plus every zone's extra; hidden in a district view. */
export const withEstimatedTotalDensityExtra = (NumberField: any) => (props: any) => {
  const extra = useContext(TotalDensityExtra);
  const area = useContext(TaxDistrictView);
  const totalClass = taxationPageClasses?.totalValue;
  const isTotal = !!totalClass && props.className === totalClass;
  const className = isTotal && area !== CITY_KEY ? `${props.className} ${districtStyles.hiddenTotal}` : props.className;
  return <NumberField {...props} className={className} value={estimatedTotalValue(props.value, props.className, totalClass, extra)} />;
};