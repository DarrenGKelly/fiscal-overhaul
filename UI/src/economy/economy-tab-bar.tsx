// The Economy panel's tab bar: our tab order, the one district picker at its left and a gap between the district tabs
// [Budget | Taxation | Services] and the city-only tabs [Production | Loans]. The logic is in header-picker.ts.
import { useMemo } from "react";
import { useValue } from "cs2/api";
import { toolbarBottom } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { districtBudgetEnabled$, districts$, districtServiceBudgetEnabled$, taxRates$ } from "bindings";
import { DistrictSelector } from "district/district-selector";
import { useSelectedDistrict } from "district/selection";
import { economyTabBarChildren, headerPicker, reservesPickerSlot } from "./header-picker";
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
  const servicesOn = useValue(districtServiceBudgetEnabled$);
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
    <div key="fo-district-picker" className={styles.pickerRoom}>
      <div className={styles.pickerSlot}>
        {picker && <DistrictSelector options={picker.options} selected={picker.selected} onSelect={setSelected} compact />}
      </div>
    </div>
  ) : null;
  return <TabBar {...props} children={economyTabBarChildren(children, slot, divider)} />;
};