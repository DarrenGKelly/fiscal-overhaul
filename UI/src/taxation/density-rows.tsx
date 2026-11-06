import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { useCallback, useState } from "react";
import { densityTaxes$, densityTaxesEnabled$, DensityTaxCell, setDensityTax } from "bindings";
import { localize, Translate } from "l10n";
import { TaxSlider } from "vanilla";
import { arrowFor, cellsForArea, densityLabel, offsetSummary, payersSuffix } from "./density";
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
  const { translate } = useLocalization();
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
        <img className={styles.arrow} src={arrowFor(open)} />
        <div className={styles.title}>{localize(translate, "ByDensity")}</div>
        <div className={styles.summary}>{offsetSummary(active, translate)}</div>
      </div>
      {open && (
        <>
          {cells.map((cell) => (
            <DensityRow key={`${cell.zone}-${cell.density}`} cell={cell} max={data.maxOffset} translate={translate} />
          ))}
          <div className={styles.note}>{localize(translate, "DensityNote")}</div>
        </>
      )}
    </div>
  );
};

const DensityRow = ({ cell, max, translate }: { cell: DensityTaxCell; max: number; translate: Translate }) => {
  // The C# trigger takes an int (and clamps it to ±maxOffset), so round whatever the slider reports.
  const onChange = useCallback((v: number) => setDensityTax(cell.zone, cell.density, Math.round(v)), [cell.zone, cell.density]);
  return (
    <div className={styles.row}>
      <div className={styles.rowTitle}>
        {densityLabel(cell.density, translate)}
        <span className={styles.count}>{payersSuffix(cell.payers, translate)}</span>
      </div>
      <TaxSlider min={-max} max={max} rate={cell.offset} income={cell.estimate} className={styles.slider} onValueChanged={onChange} />
    </div>
  );
};
