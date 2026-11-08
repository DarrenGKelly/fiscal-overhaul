import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { useCallback, useContext } from "react";
import { densityTaxes$, densityTaxesEnabled$, DensityTaxCell, setDensityTax, setTaxRate, taxRates$ } from "bindings";
import { CITY_KEY } from "district/district-select";
import { Translate } from "l10n";
import { ActiveFocusDiv, TaxSlider, taxationItemClasses } from "vanilla";
import { densityIcon } from "./density-icons";
import { cellsForArea, densityLabel } from "./density";
import { districtDensityRate, TYPE_DENSITY } from "./district-tax";
import { TaxDistrictView } from "./district-tax-view";
import districtStyles from "./district-tax-view.module.scss";
import { densitySliderValues, rateFromSlider } from "./density-slider";
import styles from "./density-rows.module.scss";

interface AreaTypeLike {
  index: number;
  locked: boolean;
}

/**
 * The density cells of a zone: none for the city-wide area, a locked zone, no data yet, or while density taxes are off
 * (a district view draws its sections whatever the switch, but the Density section only while it is on).
 */
export function useDensityCells(areaType: AreaTypeLike): { cells: DensityTaxCell[] } {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  return { cells: cellsForArea(data?.cells, areaType.index, areaType.locked, enabled) };
}

/**
 * The Density section's rows: one slider per density the zone has, setting an offset in percentage points on top of
 * whatever rate the building already pays. Each row is drawn exactly as vanilla TaxationItem draws an education or
 * resource row (its focus wrapper, its class names, its TaxSlider), so the icon, title, % column, slider and estimate
 * line up with those rows; only the values are ours: the density tax's own rate, which is charged on top of income
 * tax (density-slider.ts). In a district view the row shows and sets the district's rate (else the city's), marked
 * where it differs from the city's, with no estimate.
 */
export const DensityRows = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { cells } = useDensityCells(areaType);
  return (
    <>
      {cells.map((cell) => (
        <DensityRow key={`${cell.zone}-${cell.density}`} cell={cell} translate={translate} />
      ))}
    </>
  );
};

// Vanilla's taxation-item classes, or this module's copies of them should a game patch move the module.
const itemClasses = {
  row: taxationItemClasses?.taxationItem ?? styles.row,
  icon: taxationItemClasses?.icon ?? styles.icon,
  title: taxationItemClasses?.title ?? styles.title,
  slider: taxationItemClasses?.slider ?? styles.slider,
};

const RowBox = ActiveFocusDiv ?? "div";

interface DensityRowProps {
  cell: DensityTaxCell;
  translate: Translate;
}

const DensityRow = ({ cell, translate }: DensityRowProps) => {
  const area = useContext(TaxDistrictView);
  const rates = useValue(taxRates$);
  const district = area !== CITY_KEY;
  const onChange = useCallback(
    (v: number) =>
      district
        ? setTaxRate(TYPE_DENSITY, cell.zone, cell.density, area, rateFromSlider(v))
        : setDensityTax(cell.zone, cell.density, rateFromSlider(v)),
    [cell.zone, cell.density, district, area],
  );
  const icon = densityIcon(cell.zone, cell.density);
  const rate = district ? districtDensityRate(rates?.districts, area, cell.zone, cell.density, cell.offset) : cell.offset;
  const slider = densitySliderValues(rate);
  const sliderClass = district
    ? [itemClasses.slider, districtStyles.noEstimate, rate !== cell.offset ? districtStyles.overridden : null].filter(Boolean).join(" ")
    : itemClasses.slider;
  return (
    <RowBox className={itemClasses.row}>
      {icon ? <img className={itemClasses.icon} src={icon} /> : <div className={itemClasses.icon} />}
      <div className={itemClasses.title}>{densityLabel(cell.density, translate)}</div>
      <TaxSlider min={slider.min} max={slider.max} rate={slider.rate} income={district ? 0 : cell.estimate} className={sliderClass} onValueChanged={onChange} />
    </RowBox>
  );
};
