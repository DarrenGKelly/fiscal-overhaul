import { useState } from "react";
import { cycle, dividerBefore, DistrictOption } from "./district-select";
import styles from "./district-selector.module.scss";
import { Scrollable } from "vanilla";
import { playSound } from "bindings";
import { SOUNDS, soundForPick } from "sounds";

// The game's font has no arrow glyphs (a tofu box): vanilla's icon, mirrored for the left arrow.
const ARROW = "Media/Glyphs/ThickStrokeArrowRight.svg";

/**
 * `‹ [name] ›`: the arrows step through the options (wrapping), the name opens a list of every option. Drawn once, in
 * the Economy panel's header at the left of the tabs (economy/economy-tab-bar.tsx); `compact` sizes it for the header.
 */
export const DistrictSelector = ({
  options,
  selected,
  onSelect,
  compact = false,
}: {
  options: DistrictOption[];
  selected: string;
  onSelect: (key: string) => void;
  compact?: boolean;
}) => {
  const [open, setOpen] = useState(false);
  const current = options.find((o) => o.key === selected) ?? options[0];
  const choose = (key: string) => {
    setOpen(false);
    onSelect(key);
  };
  const step = (dir: 1 | -1) => {
    playSound(dir < 0 ? SOUNDS.previousDistrict : SOUNDS.nextDistrict);
    choose(cycle(options, selected, dir));
  };

  return (
    <div className={`${styles.districtBar} ${compact ? styles.compact : ""}`}>
      <div className={styles.districtSelector}>
        <div className={`${styles.spanButton} ${styles.districtArrowButton}`} onClick={() => step(-1)}>
          <img className={`${styles.districtArrow} ${styles.districtArrowLeft}`} src={ARROW} />
        </div>
        <div className={`${styles.spanButton} ${styles.districtName}`} onClick={() => {
            playSound(SOUNDS.openList);
            setOpen((o) => !o);
          }}>
          {current?.label ?? ""}
        </div>
        <div className={`${styles.spanButton} ${styles.districtArrowButton}`} onClick={() => step(1)}>
          <img className={styles.districtArrow} src={ARROW} />
        </div>
        {open && (
          <div className={styles.districtList}>
            <Scrollable className={styles.districtScroll} trackVisibility="scrollable">
              {options.map((o, i) => (
                <div
                  key={o.key}
                  className={`${styles.districtItem} ${dividerBefore(options, i) ? styles.districtDivider : ""} ${
                    o.key === current?.key ? styles.spanActive : ""
                  }`}
                  onClick={() => {
                    playSound(soundForPick(selected, o.key, SOUNDS.pickFromList) ?? SOUNDS.openList);
                    choose(o.key);
                  }}
                >
                  {o.label}
                </div>
              ))}
            </Scrollable>
          </div>
        )}
      </div>
    </div>
  );
};
