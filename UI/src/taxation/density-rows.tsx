import { useMapValue, useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { useCallback } from "react";
import { densityTaxes$, DensityTaxCell, setDensityTax } from "bindings";
import { localize, Translate } from "l10n";
import { ActiveFocusDiv, TaxSlider, taxationItemClasses } from "vanilla";
import { densityIcon } from "./density-icons";
import { cellsForArea, densityLabel, offsetSummary } from "./density";
import { densitySliderValues, offsetFromSlider } from "./density-slider";
import styles from "./density-rows.module.scss";

interface AreaTypeLike {
  index: number;
  locked: boolean;
}

/** The density cells of a zone (none for the city, a locked zone, or no data yet). The sections are drawn only while density taxes are on. */
export function useDensityCells(areaType: AreaTypeLike): { cells: DensityTaxCell[]; max: number } {
  const data = useValue(densityTaxes$);
  return { cells: cellsForArea(data?.cells, areaType.index, areaType.locked, true), max: data?.maxOffset ?? 0 };
}

/** The Density section's header summary: "no offsets", "2 offsets set". */
export const DensitySummary = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { cells } = useDensityCells(areaType);
  return <>{offsetSummary(cells.filter((c) => c.offset !== 0).length, translate)}</>;
};

/**
 * The Density section's rows: one slider per density the zone has, setting an offset in percentage points on top of
 * whatever rate the building already pays. Each row is drawn exactly as vanilla TaxationItem draws an education or
 * resource row (its focus wrapper, its class names, its TaxSlider), so the icon, title, % column, slider and estimate
 * line up with those rows; only the values are ours (density-slider.ts).
 */
export const DensityRows = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { cells, max } = useDensityCells(areaType);
  const zoneRate = useMapValue(taxation.areaTaxRates$, areaType.index);
  return (
    <>
      {cells.map((cell) => (
        <DensityRow key={`${cell.zone}-${cell.density}`} cell={cell} max={max} zoneRate={zoneRate} translate={translate} />
      ))}
      <div className={styles.note}>{localize(translate, "DensityNote")}</div>
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
  max: number;
  zoneRate: number | undefined;
  translate: Translate;
}

const DensityRow = ({ cell, max, zoneRate, translate }: DensityRowProps) => {
  const onChange = useCallback(
    (v: number) => setDensityTax(cell.zone, cell.density, offsetFromSlider(v, max, zoneRate)),
    [cell.zone, cell.density, max, zoneRate],
  );
  const icon = densityIcon(cell.zone, cell.density);
  const slider = densitySliderValues(cell.offset, max, zoneRate);
  return (
    <RowBox className={itemClasses.row}>
      {icon ? <img className={itemClasses.icon} src={icon} /> : <div className={itemClasses.icon} />}
      <div className={itemClasses.title}>{densityLabel(cell.density, translate)}</div>
      <TaxSlider min={slider.min} max={slider.max} rate={slider.rate} income={cell.estimate} className={itemClasses.slider} onValueChanged={onChange} />
    </RowBox>
  );
};
