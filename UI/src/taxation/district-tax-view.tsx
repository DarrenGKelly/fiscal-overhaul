import { useMapValue, useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { Button } from "cs2/ui";
import { createContext, useContext, useMemo } from "react";
import { cityDemand$, districts$, resetDistrictTaxRates, setTaxRate, taxRates$ } from "bindings";
import { CITY_KEY } from "district/district-select";
import { DistrictSelector } from "district/district-selector";
import { useSelectedDistrict } from "district/selection";
import { localize } from "l10n";
import { cityDemandFor, demandPercent, districtDemand, districtRateDelta } from "./district-demand";
import { hasDistrictOverrides, RowTarget, rowRate, ShownRate, showsTaxDistrictSelector, taxDistrictOptions, taxViewKey } from "./district-tax";
import styles from "./district-tax-view.module.scss";

// Per-district tax rates on the Taxation tab: the shared district selector above the page (district/selection.ts).
// A picked district shows the same layout as Entire City (zone header slider, Density tax, Education/Resources) with
// the district's own values: each slider shows the district's value (else the city's), is marked where it differs from
// the city's, and sets the district's value (setTaxRate). Dragging a value back to the city's, or "Use city rates",
// makes it follow the city again. Shown only while the tax engine charges and the city has a district. District keys
// change on every load: the view always takes them from the districts binding.

/** The district the Taxation tab shows: CITY_KEY for the Entire City view (and outside the Taxation page). */
export const TaxDistrictView = createContext<string>(CITY_KEY);

/**
 * The row a TaxSlider below belongs to: provided by the zone group (its header: a zone rate) and around each
 * re-hosted vanilla TaxationItem (an education level or resource). Null: a slider the district view leaves alone.
 */
export const TaxRowTarget = createContext<RowTarget | null>(null);

/** TaxationPage, with the district selector (and "Use city rates") above it and the view it picks provided to the zone groups. */
export const withTaxDistrictSelector = (TaxationPage: any) => (props: any) => {
  const { translate } = useLocalization();
  const rates = useValue(taxRates$);
  const districts = useValue(districts$);
  const [selected, setSelected] = useSelectedDistrict();
  const options = useMemo(() => taxDistrictOptions(districts ?? [], translate), [districts, translate]);
  const engineOn = rates?.engineOn ?? false;
  const view = taxViewKey(engineOn, options, selected);
  return (
    <TaxDistrictView.Provider value={view}>
      {showsTaxDistrictSelector(engineOn, options) && (
        <>
          <DistrictSelector options={options} selected={view} onSelect={setSelected} />
          {view !== CITY_KEY && (
            <div className={styles.resetBar}>
              <Button
                variant="flat"
                className={styles.resetButton}
                disabled={!hasDistrictOverrides(rates?.districts, view)}
                onSelect={() => resetDistrictTaxRates(view)}
              >
                {localize(translate, "UseCityRates")}
              </Button>
            </div>
          )}
        </>
      )}
      <TaxationPage {...props} />
    </TaxDistrictView.Provider>
  );
};

export interface DistrictSlider extends ShownRate {
  /** The district view's setter, or null in the Entire City view (the slider keeps vanilla's). */
  onChange: ((value: number) => void) | null;
}

/**
 * What a slider for `target` shows in the current view: the city's value (vanilla's `cityRate`) in Entire City, the
 * district's in a district view, with its setter.
 */
export function useDistrictSlider(target: RowTarget | null, cityRate: number, min: number, max: number): DistrictSlider {
  const area = useContext(TaxDistrictView);
  const rates = useValue(taxRates$);
  // The zone's city rate (vanilla's areaTaxRates: base + zone), for a level/resource row's offset.
  const areaIndex = target ? target.zone + 1 : 0;
  const cityZoneRate = useMapValue(taxation.areaTaxRates$, areaIndex) ?? 0;
  if (!target || area === CITY_KEY) return { rate: cityRate, overridden: false, onChange: null };
  const shown = rowRate(target, area, rates?.districts, cityRate, cityZoneRate, min, max);
  return { ...shown, onChange: (v: number) => setTaxRate(target.type, target.zone, target.key, area, Math.round(v)) };
}


/**
 * "Demand (info): 31% vs city 36%" under a zone's header in a district view: the city demand shifted by how far the
 * district's own zone rate is from the city's (district-demand.ts). An estimate only: the game simulates one city-wide
 * demand per zone.
 */
export const DemandInfo = ({ zone }: { zone: number }) => {
  const { translate } = useLocalization();
  const area = useContext(TaxDistrictView);
  const rates = useValue(taxRates$);
  const cityZoneRate = useMapValue(taxation.areaTaxRates$, zone + 1) ?? 0;
  const resLow = useValue(cityDemand$.resLow);
  const resMed = useValue(cityDemand$.resMed);
  const resHigh = useValue(cityDemand$.resHigh);
  const commercial = useValue(cityDemand$.commercial);
  const industrial = useValue(cityDemand$.industrial);
  const office = useValue(cityDemand$.office);
  if (area === CITY_KEY || zone < 0) return null;
  const city = cityDemandFor(zone, { resLow, resMed, resHigh, commercial, industrial, office });
  const district = districtDemand(city, zone, districtRateDelta(rates?.districts, area, zone, cityZoneRate), rates?.taxEffect);
  const note = localize(translate, "DistrictDemandNote");
  return (
    <div className={styles.demandRow} title={note}>
      <div className={styles.demandText}>
        {localize(translate, "DistrictDemand", { DISTRICT: demandPercent(district), CITY: demandPercent(city) })}
      </div>
      <div className={styles.demandNote}>{note}</div>
    </div>
  );
};