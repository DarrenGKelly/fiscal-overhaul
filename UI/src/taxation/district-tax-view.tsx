import { useMapValue, useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { createContext, useContext, useEffect, useMemo } from "react";
import { districts$, districtTaxEstimates$, setTaxEstimateArea, setTaxRate, taxRates$ } from "bindings";
import { CITY_KEY } from "district/district-select";
import { useSelectedDistrict } from "district/selection";
import { RowReset, rowReset, RowTarget, rowRate, ShownRate, taxDistrictOptions, taxViewKey } from "./district-tax";

// Per-district tax rates on the Taxation tab: the district picked in the Economy panel header (district/selection.ts).
// A picked district shows the same layout as Entire City (zone header slider, Education/Resources, Density) with
// the district's own values and estimates: each slider shows the district's value (else the city's), is marked where
// it differs from the city's, and sets the district's value (setTaxRate); each row's reset button clears its override
// (tax-row-slider.tsx), as does dragging a value back to the city's. Shown only while the tax engine charges and the
// city has a district. District keys change on every load: the view always takes them from the districts binding.

/** The district the Taxation tab shows: CITY_KEY for the Entire City view (and outside the Taxation page). */
export const TaxDistrictView = createContext<string>(CITY_KEY);

/**
 * The row a TaxSlider below belongs to: provided by the zone group (its header: a zone rate) and around each
 * re-hosted vanilla TaxationItem (an education level or resource). Null: a slider the district view leaves alone.
 */
export const TaxRowTarget = createContext<RowTarget | null>(null);

/**
 * The district the Taxation tab shows and its choices: the shared selection (district/selection.ts) checked against
 * the districts binding (the one list, DistrictListUISystem's) and the engine's state.
 */
function useTaxView() {
  const rates = useValue(taxRates$);
  const districts = useValue(districts$);
  const [selected] = useSelectedDistrict();
  const options = useMemo(() => taxDistrictOptions(districts ?? []), [districts]);
  return { rates, view: taxViewKey(rates?.engineOn ?? false, options, selected) };
}

/** TaxationPage, with the header's view provided to the zone groups; C# told which district's estimates to compute. */
export const withTaxDistrictSelector = (TaxationPage: any) => (props: any) => {
  const { view } = useTaxView();
  useEffect(() => {
    setTaxEstimateArea(view);
    return () => setTaxEstimateArea(CITY_KEY);
  }, [view]);
  return (
    <TaxDistrictView.Provider value={view}>
      <TaxationPage {...props} />
    </TaxDistrictView.Provider>
  );
};

/** The shown district's estimates (DistrictTaxEstimateUISystem), for district-tax.ts districtRowEstimate. */
export function useDistrictEstimates() {
  return useValue(districtTaxEstimates$);
}

export interface RowSlider extends ShownRate {
  /** The district view's setter, or null in the Entire City view (the slider keeps vanilla's). */
  onChange: ((value: number) => void) | null;
  /** The row's reset button and marker (district-tax.ts rowReset). */
  reset: RowReset;
}

/**
 * What a slider for `target` shows in the current view: the city's value (vanilla's `cityRate`) in Entire City, the
 * district's in a district view, with its setter; and its reset.
 */
export function useRowSlider(target: RowTarget, cityRate: number, min: number, max: number): RowSlider {
  const area = useContext(TaxDistrictView);
  const rates = useValue(taxRates$);
  // The zone's city rate (vanilla's areaTaxRates: base + zone), for a level/resource row's offset.
  const cityZoneRate = useMapValue(taxation.areaTaxRates$, target.zone + 1) ?? 0;
  const reset = rowReset(target, area, rates?.districts, cityRate, cityZoneRate, min, max, rates?.defaults);
  if (area === CITY_KEY) return { rate: cityRate, overridden: false, onChange: null, reset };
  const shown = rowRate(target, area, rates?.districts, cityRate, cityZoneRate, min, max);
  return { ...shown, reset, onChange: (v: number) => setTaxRate(target.type, target.zone, target.key, area, Math.round(v)) };
}
