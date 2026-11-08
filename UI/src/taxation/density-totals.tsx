import { useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { createContext, useContext } from "react";
import { densityTaxes$, densityTaxesEnabled$, taxRates$ } from "bindings";
import { taxationPageClasses } from "vanilla";
import { CITY_KEY } from "district/district-select";
import { TaxDistrictView, TaxRowTarget, useDistrictEstimates, useRowSlider } from "./district-tax-view";
import { districtHeaderRange, districtRowEstimate, districtTotalEstimate, RowTarget, TYPE_ZONE_RATE } from "./district-tax";
import districtStyles from "./district-tax-view.module.scss";
import { EngineWarningLine } from "./engine-warning-line";
import { TaxRowSlider } from "./tax-row-slider";
import { estimatedTotalValue, totalExtra, zoneExtra, zoneTotalIncome } from "./density";

// The Taxation tab's totals with the density tax and the district (and Outside) income-tax overrides in them. Vanilla computes both totals in C#
// (TaxationUISystem's `areaTaxIncomes` and `taxIncome`) and draws them inside its own components, so the UI adds the
// extra to the drawn value: TaxationPage and TaxationGroup provide the extra through a context, and extended TaxSlider
// and NumberField components add it where vanilla draws a total. Outside those contexts both are vanilla as is. In a
// district view every estimate is the district's own (C# DistrictTaxEstimateUISystem: its buildings at its rates).

/** The extra for the zone group being drawn (provided by the TaxationGroup wrapper in index.tsx). */
export const ZoneDensityExtra = createContext<number | null>(null);

/** The extra for ESTIMATED TOTAL (provided by the TaxationPage wrapper below). */
const TotalDensityExtra = createContext<number | null>(null);

/** The zone's extra, for the TaxationGroup wrapper to provide. */
export function useZoneDensityExtra(areaType: { index: number; locked: boolean }): number {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  return zoneExtra(data?.cells, data?.districtParts, areaType.index, areaType.locked, enabled);
}

/** TaxationPage, with every zone's extra provided for ESTIMATED TOTAL. */
export const withTotalDensityExtra = (TaxationPage: any) => (props: any) => {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  const areas = useValue(taxation.areaTypes$);
  return (
    <TotalDensityExtra.Provider value={totalExtra(data?.cells, data?.districtParts, areas, enabled)}>
      <EngineWarningLine />
      <TaxationPage {...props} />
    </TotalDensityExtra.Provider>
  );
};

/**
 * One zone header or education/resource row (TaxRowTarget): its reset button and marker, and in a district view the
 * district's own value, setter and estimate, marked where the value differs from the city's. Entire City: vanilla's
 * value and estimate (the header's with the zone's density extra).
 */
const RowSlider = ({ TaxSlider, props, target }: { TaxSlider: any; props: any; target: RowTarget }) => {
  const extra = useContext(ZoneDensityExtra);
  const area = useContext(TaxDistrictView);
  const estimates = useDistrictEstimates();
  const row = useRowSlider(target, props.rate, props.min, props.max);
  const rates = useValue(taxRates$);
  if (area === CITY_KEY || !row.onChange) {
    const city = { ...props, income: zoneTotalIncome(props.income, props.primary, extra) };
    return <TaxRowSlider TaxSlider={TaxSlider} sliderProps={city} target={target} area={area} reset={row.reset} />;
  }
  // A header's label reads `range` over `rate` (vanilla TaxSlider): the district's span, not the city's.
  const range = target.type === TYPE_ZONE_RATE ? districtHeaderRange(props.range, rates?.districts, area, target.zone, props.rate, props.min, props.max) : props.range;
  const district = { ...props, rate: row.rate, range, income: districtRowEstimate(estimates, area, target), onValueChanged: row.onChange };
  return (
    <TaxRowSlider
      TaxSlider={TaxSlider}
      sliderProps={district}
      target={target}
      area={area}
      reset={row.reset}
      sliderClass={row.overridden ? districtStyles.overridden : null}
    />
  );
};

/** TaxSlider: a zone header or a re-hosted education/resource row gets RowSlider; any other slider passes through. */
export const withZoneDensityExtra = (TaxSlider: any) => (props: any) => {
  const extra = useContext(ZoneDensityExtra);
  const target = useContext(TaxRowTarget);
  // The group's target (a zone rate) is the header's only; a row's target is its own slider's.
  const applies = !!target && (target.type === TYPE_ZONE_RATE) === !!props.primary;
  if (!applies || !target) return <TaxSlider {...props} income={zoneTotalIncome(props.income, props.primary, extra)} />;
  return <RowSlider TaxSlider={TaxSlider} props={props} target={target} />;
};

/** NumberField: the ESTIMATED TOTAL field shows the total plus every zone's extra; the district's total in a district view. */
export const withEstimatedTotalDensityExtra = (NumberField: any) => (props: any) => {
  const extra = useContext(TotalDensityExtra);
  const area = useContext(TaxDistrictView);
  const estimates = useDistrictEstimates();
  const totalClass = taxationPageClasses?.totalValue;
  const isTotal = !!totalClass && props.className === totalClass;
  if (isTotal && area !== CITY_KEY) return <NumberField {...props} value={districtTotalEstimate(estimates, area)} />;
  return <NumberField {...props} value={estimatedTotalValue(props.value, props.className, totalClass, extra)} />;
};
