import { useRef, useState } from "react";
import { cycle, dividerBefore, DistrictOption, listAnchor } from "./district-select";
import styles from "./district-selector.module.scss";
import { Portal, Scrollable, usePortalContainer } from "vanilla";
import { playSound } from "bindings";
import { SOUNDS, soundForPick } from "sounds";

// Vanilla's portal hooks, or stand-ins where the game no longer has them (the list then draws in place).
const usePortal: () => HTMLElement | null = usePortalContainer ?? (() => null);
const inPortal = (at: { left: number; top: number } | null, list: JSX.Element) => (Portal && at ? <Portal>{list}</Portal> : list);

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
  // The list draws through vanilla's Portal, above the panel: inside the header it would sit under the panel content
  // (the header's stacking context comes before the content's). Placed under the name when it opens.
  const container = usePortal();
  const nameRef = useRef<HTMLDivElement>(null);
  const [at, setAt] = useState<{ left: number; top: number } | null>(null);
  const toggle = () => {
    const name = nameRef.current;
    if (!open && name) setAt(listAnchor(name.getBoundingClientRect(), container?.getBoundingClientRect() ?? null));
    setOpen((o) => !o);
  };
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
        <div ref={nameRef} className={`${styles.spanButton} ${styles.districtName}`} onClick={() => {
            playSound(SOUNDS.openList);
            toggle();
          }}>
          {current?.label ?? ""}
        </div>
        <div className={`${styles.spanButton} ${styles.districtArrowButton}`} onClick={() => step(1)}>
          <img className={styles.districtArrow} src={ARROW} />
        </div>
        {open && inPortal(
          at,
          <div className={styles.districtList} style={at && Portal ? { left: `${at.left}px`, top: `${at.top}px` } : undefined}>
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
