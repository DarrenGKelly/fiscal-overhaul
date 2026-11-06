import { useState } from "react";
import { cycle, dividerBefore, DistrictOption } from "./district-select";
import styles from "./budget-page.module.scss";

// The game's font has no arrow glyphs (a tofu box): vanilla's icon, mirrored for the left arrow.
const ARROW = "Media/Glyphs/ThickStrokeArrowRight.svg";

/**
 * `‹ [name] ›` centred above the Budget tab: the arrows step through the options (wrapping), the name opens a list of
 * every option. The parent hides it when there are no districts.
 */
export const DistrictSelector = ({
  options,
  selected,
  onSelect,
}: {
  options: DistrictOption[];
  selected: string;
  onSelect: (key: string) => void;
}) => {
  const [open, setOpen] = useState(false);
  const current = options.find((o) => o.key === selected) ?? options[0];
  const choose = (key: string) => {
    setOpen(false);
    onSelect(key);
  };

  return (
    <div className={styles.districtBar}>
      <div className={styles.districtSelector}>
        <div className={styles.spanButton} onClick={() => choose(cycle(options, selected, -1))}>
          <img className={`${styles.districtArrow} ${styles.districtArrowLeft}`} src={ARROW} />
        </div>
        <div className={`${styles.spanButton} ${styles.districtName}`} onClick={() => setOpen((o) => !o)}>
          {current?.label ?? ""}
        </div>
        <div className={styles.spanButton} onClick={() => choose(cycle(options, selected, 1))}>
          <img className={styles.districtArrow} src={ARROW} />
        </div>
        {open && (
          <div className={styles.districtList}>
            {options.map((o, i) => (
              <div
                key={o.key}
                className={`${styles.districtItem} ${dividerBefore(options, i) ? styles.districtDivider : ""} ${
                  o.key === current?.key ? styles.spanActive : ""
                }`}
                onClick={() => choose(o.key)}
              >
                {o.label}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
