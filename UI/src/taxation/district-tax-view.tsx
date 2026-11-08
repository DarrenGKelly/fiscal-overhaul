import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { createContext, useCallback, useContext, useMemo } from "react";
import { cityDemand$, districts$, setTaxRate, TAX_TYPE_DISTRICT, taxRates$ } from "bindings";
import { CITY_KEY } from "district/district-select";
import { DistrictSelector } from "district/district-selector";
import { useSelectedDistrict } from "district/selection";
import { localize } from "../l10n";
import { TaxSlider } from "vanilla";
import { zoneForAreaIndex } from "./density";
import { cityDemandFor, demandPercent, districtDemand } from "./district-demand";
import { clampAdjustment, districtAdjustment, showsTaxDistrictSelector, taxDistrictOptions, taxViewKey, vsCityLabel } from "./district-tax";
import styles from "./district-tax-view.module.scss";

// Per-district tax rates on the Taxation tab: the shared district selector above the page (district/selection.ts),
// and in a district view each zone's District section (tax-sections.ts) with one slider: the district's adjustment on
// the city rate. Entire City is today's layout. Shown only while the tax engine charges and the city has a district.

/** The district the Taxation tab shows: CITY_KEY for the Entire City view (and outside the Taxation page). */
export const TaxDistrictView = createContext<string>(CITY_KEY);

interface AreaTypeLike {
  index: number;
  locked: boolean;
}

/** TaxationPage, with the district selector above it and the view it picks provided to the zone groups. */
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
      {showsTaxDistrictSelector(engineOn, options) && <DistrictSelector options={options} selected={view} onSelect={setSelected} />}
      <TaxationPage {...props} />
    </TaxDistrictView.Provider>
  );
};

/** The selected district's adjustment for a zone group, and the cap; key = CITY_KEY in the city view. */
function useDistrictAdjustment(areaType: AreaTypeLike): { key: string; zone: number; value: number; cap: number } {
  const key = useContext(TaxDistrictView);
  const rates = useValue(taxRates$);
  const zone = zoneForAreaIndex(areaType.index);
  return { key, zone, value: districtAdjustment(rates?.districts, key, zone), cap: rates?.districtCap ?? 0 };
}

/** The District section's header summary: "+5% vs city". */
export const DistrictSummary = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { value } = useDistrictAdjustment(areaType);
  return <>{vsCityLabel(value, translate)}</>;
};

/** The District section's row: one slider, -cap..+cap points on the city rate, for the selected district and this zone. */
export const DistrictRows = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { key, zone, value, cap } = useDistrictAdjustment(areaType);
  const onChange = useCallback(
    (v: number) => setTaxRate(TAX_TYPE_DISTRICT, zone, key, clampAdjustment(v, cap)),
    [zone, key, cap],
  );
  if (key === CITY_KEY || zone < 0 || cap <= 0) return null;
  return (
    <>
      <div className={styles.row}>
        <div className={styles.rowTitle}>{vsCityLabel(value, translate)}</div>
        <TaxSlider min={-cap} max={cap} rate={value} income={0} className={styles.slider} onValueChanged={onChange} />
      </div>
      <DemandInfo zone={zone} value={value} />
    </>
  );
};

/** "Demand (info): 31% vs city 36%": the city demand shifted by the district's adjustment. Estimate only. */
const DemandInfo = ({ zone, value }: { zone: number; value: number }) => {
  const { translate } = useLocalization();
  const rates = useValue(taxRates$);
  const resLow = useValue(cityDemand$.resLow);
  const resMed = useValue(cityDemand$.resMed);
  const resHigh = useValue(cityDemand$.resHigh);
  const commercial = useValue(cityDemand$.commercial);
  const industrial = useValue(cityDemand$.industrial);
  const office = useValue(cityDemand$.office);
  const city = cityDemandFor(zone, { resLow, resMed, resHigh, commercial, industrial, office });
  const district = districtDemand(city, zone, value, rates?.taxEffect);
  const note = localize(translate, "DistrictDemandNote");
  return (
    <div className={styles.row} title={note}>
      <div className={styles.demandText}>
        {localize(translate, "DistrictDemand", { DISTRICT: demandPercent(district), CITY: demandPercent(city) })}
      </div>
      <div className={styles.demandNote}>{note}</div>
    </div>
  );
};
