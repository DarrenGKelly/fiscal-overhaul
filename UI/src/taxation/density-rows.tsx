import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { useCallback } from "react";
import { densityTaxes$, DensityTaxCell, setDensityTax } from "bindings";
import { localize, Translate } from "l10n";
import { TaxSlider } from "vanilla";
import { cellsForArea, densityLabel, offsetSummary } from "./density";
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
 * whatever rate the building already pays.
 */
export const DensityRows = ({ areaType }: { areaType: AreaTypeLike }) => {
  const { translate } = useLocalization();
  const { cells, max } = useDensityCells(areaType);
  return (
    <>
      {cells.map((cell) => (
        <DensityRow key={`${cell.zone}-${cell.density}`} cell={cell} max={max} translate={translate} />
      ))}
      <div className={styles.note}>{localize(translate, "DensityNote")}</div>
    </>
  );
};

const DensityRow = ({ cell, max, translate }: { cell: DensityTaxCell; max: number; translate: Translate }) => {
  // The C# trigger takes an int (and clamps it to ±maxOffset), so round whatever the slider reports.
  const onChange = useCallback((v: number) => setDensityTax(cell.zone, cell.density, Math.round(v)), [cell.zone, cell.density]);
  return (
    <div className={styles.row}>
      <div className={styles.rowTitle}>{densityLabel(cell.density, translate)}</div>
      <TaxSlider min={-max} max={max} rate={cell.offset} income={cell.estimate} className={styles.slider} onValueChanged={onChange} />
    </div>
  );
};
