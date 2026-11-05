import { useValue } from "cs2/api";
import { useCallback, useState } from "react";
import { densityTaxes$, densityTaxesEnabled$, DensityTaxCell, setDensityTax } from "bindings";
import { TaxSlider } from "vanilla";
import { cellsForArea, densityLabel } from "./density";
import styles from "./density-rows.module.scss";

interface AreaTypeLike {
  index: number;
  id: string;
  locked: boolean;
}

/**
 * Rows added under each vanilla tax group: one slider per density the zone has, setting an offset in
 * percentage points on top of whatever rate the building already pays. Collapsed by default like the
 * vanilla education/product rows.
 */
export const DensityRows = ({ areaType }: { areaType: AreaTypeLike }) => {
  const data = useValue(densityTaxes$);
  const enabled = useValue(densityTaxesEnabled$);
  const [open, setOpen] = useState(false);
  const toggle = useCallback(() => setOpen((o) => !o), []);

  const cells = cellsForArea(data?.cells, areaType.index, areaType.locked, enabled);
  if (!data || cells.length === 0) return null;
  const active = cells.filter((c) => c.offset !== 0).length;

  return (
    <div className={styles.densityRows}>
      <div className={styles.header} onClick={toggle}>
        <div className={styles.arrow}>{open ? "▾" : "▸"}</div>
        <div className={styles.title}>By density</div>
        <div className={styles.summary}>{active > 0 ? `${active} offset${active > 1 ? "s" : ""} set` : "no offsets"}</div>
      </div>
      {open && (
        <>
          {cells.map((cell) => (
            <DensityRow key={`${cell.zone}-${cell.density}`} cell={cell} max={data.maxOffset} />
          ))}
          <div className={styles.note}>
            Offsets add to the rate each building already pays. They change tax revenue only; demand and happiness still react to the
            main rates above.
          </div>
        </>
      )}
    </div>
  );
};

const DensityRow = ({ cell, max }: { cell: DensityTaxCell; max: number }) => {
  // The C# trigger takes an int (and clamps it to ±maxOffset), so round whatever the slider reports.
  const onChange = useCallback((v: number) => setDensityTax(cell.zone, cell.density, Math.round(v)), [cell.zone, cell.density]);
  return (
    <div className={styles.row}>
      <div className={styles.rowTitle}>
        {densityLabel(cell.density)}
        <span className={styles.count}>{cell.payers > 0 ? ` · ${cell.payers.toLocaleString()} payers` : ""}</span>
      </div>
      <TaxSlider min={-max} max={max} rate={cell.offset} income={cell.estimate} className={styles.slider} onValueChanged={onChange} />
    </div>
  );
};
