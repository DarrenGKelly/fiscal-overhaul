// The Economy panel's tab bar: our tab order, the one district picker at its left and a gap between the district tabs
// [Budget | Taxation | Services] and the city-only tabs [Production | Loans], with a frame around the picker and the
// tabs it drives. The logic is in header-picker.ts and tab-group-frame.ts.
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { toolbarBottom } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { districtBudgetEnabled$, districts$, districtServiceBudgetEnabled$, servicesTableEnabled$, taxRates$ } from "bindings";
import { DistrictSelector } from "district/district-selector";
import { useSelectedDistrict } from "district/selection";
import { keepMeasuring } from "common/slider-reset";
import { economyTabBarChildren, headerPicker, reservesPickerSlot, servicesDistrictsOn } from "./header-picker";
import { FrameBox, Rect, sameBox, tabGroupFrame, tabHeight } from "./tab-group-frame";
import styles from "./economy-tab-bar.module.scss";

export { isEconomyTabSet } from "./tab-order";

/**
 * Vanilla TabBar with the Economy panel's tabs: the picker at the top left (its room kept, empty, on Production and
 * Loans and on a tab whose district feature is off, so the tabs don't move), then the tabs in our order with the group
 * divider.
 */
export const EconomyTabBar = ({ TabBar, props }: { TabBar: any; props: any }) => {
  const children: any[] = props.children;
  const selectedTab: number = children[0]?.props?.selectedTab;
  const { translate } = useLocalization();
  const budgetOn = useValue(districtBudgetEnabled$);
  const servicesOn = servicesDistrictsOn(useValue(districtServiceBudgetEnabled$), useValue(servicesTableEnabled$));
  const rates = useValue(taxRates$);
  const districts = useValue(districts$);
  const cityName = useValue(toolbarBottom.cityName$);
  const [selected, setSelected] = useSelectedDistrict();
  const features = { budgetOn, servicesOn, taxEngineOn: rates?.engineOn ?? false };
  const picker = useMemo(
    () => headerPicker(selectedTab, features, districts ?? [], selected, translate, cityName),
    [selectedTab, budgetOn, servicesOn, features.taxEngineOn, districts, selected, translate, cityName]
  );

  // The tab bar itself keeps vanilla's props and styles: any style of ours on it (justify-content, padding, position)
  // blew every tab up to ~2.5e8 px in Gameface. The picker and the divider are zero-width markers in the row, their
  // room made by the next tab's margin (economy-tab-bar.module.scss). The tab elements are vanilla's, untouched.
  const divider = (
    <div key="fo-tab-divider" className={styles.tabDivider}>
      <div className={styles.tabDividerLine} />
    </div>
  );
  const reserved = reservesPickerSlot(features);
  const slot = reserved ? (
    <PickerRoom key="fo-district-picker">
      {picker ? (height) => <DistrictSelector options={picker.options} selected={picker.selected} onSelect={setSelected} compact height={height} /> : null}
    </PickerRoom>
  ) : null;
  return <TabBar {...props} children={economyTabBarChildren(children, slot, divider)} />;
};
/** The frame's padding around the picker and the tabs, in rem (scaled by the measured px per rem). */
const FRAME_PAD_REM = 6;

const rectOf = (e: Element): Rect => {
  const r = e.getBoundingClientRect();
  return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
};

/**
 * The picker's zero-size room marker (economy-tab-bar.module.scss), and, while the picker shows, the frame around it
 * and the district tabs: the vanilla tab buttons after the marker, up to the group divider. Measured after each render
 * and for a few frames (no ResizeObserver in Gameface); the picker is drawn as tall as a tab.
 */
const PickerRoom = ({ children }: { children: ((height: number | null) => JSX.Element) | null }) => {
  const ref = useRef<HTMLDivElement>(null);
  const probe = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState<FrameBox | null>(null);
  const [height, setHeight] = useState<number | null>(null);
  const shows = !!children;
  const measure = (): FrameBox | null => {
    const room = ref.current;
    if (!room || !shows) return null;
    const tabs: Rect[] = [];
    for (let e = room.nextElementSibling; e && !e.classList.contains(styles.tabDivider); e = e.nextElementSibling) tabs.push(rectOf(e));
    const pxPerRem = (probe.current?.getBoundingClientRect().width ?? 100) / 100;
    const next = tabGroupFrame(rectOf(room), tabs, FRAME_PAD_REM * pxPerRem);
    const h = tabHeight(tabs);
    setBox((b) => (sameBox(b, next) ? b : next));
    setHeight((old) => (old !== null && h !== null && Math.abs(old - h) < 0.5 ? old : h));
    return next;
  };
  useLayoutEffect(() => {
    measure();
  });
  useEffect(() => {
    let id = 0;
    let frames = 0;
    let stable = 0;
    let last: FrameBox | null = null;
    const tick = () => {
      const next = measure();
      stable = sameBox(last, next) && next ? stable + 1 : 0;
      last = next;
      frames++;
      if (keepMeasuring(frames, stable, !!next)) id = requestAnimationFrame(tick);
    };
    id = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(id);
  });
  return (
    <div ref={ref} className={styles.pickerRoom}>
      <div ref={probe} className={styles.remProbe} />
      {shows && box && (
        <div className={styles.tabGroupFrame} style={{ left: `${box.left}px`, top: `${box.top}px`, width: `${box.width}px`, height: `${box.height}px` }} />
      )}
      <div className={styles.pickerSlot}>{children ? children(height) : null}</div>
    </div>
  );
};
