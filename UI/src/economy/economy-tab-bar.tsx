// The Economy panel's tab bar: our tab order, the one district picker at its left and a gap between the district tabs
// [Budget | Taxation | Services] and the city-only tabs [Production | Loans]. The logic is in header-picker.ts.
import { useMemo } from "react";
import { useValue } from "cs2/api";
import { toolbarBottom } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { districtBudgetEnabled$, districts$, districtServiceBudgetEnabled$, taxRates$ } from "bindings";
import { DistrictSelector } from "district/district-selector";
import { useSelectedDistrict } from "district/selection";
import { headerPicker, reservesPickerSlot, withTabGroupDivider } from "./header-picker";
import { orderEconomyTabs } from "./tab-order";
import styles from "./economy-tab-bar.module.scss";

export { isEconomyTabSet } from "./tab-order";

/**
 * Vanilla TabBar with the Economy panel's tabs: the picker's slot first (kept, empty, on Production and Loans and on a
 * tab whose district feature is off, so the tabs don't move), then the tabs in our order with the group divider.
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

  const tabs = withTabGroupDivider(orderEconomyTabs(children), <div key="fo-tab-divider" className={styles.tabDivider} />);
  const slot = reservesPickerSlot(features) ? (
    <div key="fo-district-picker" className={styles.pickerSlot}>
      {picker && <DistrictSelector options={picker.options} selected={picker.selected} onSelect={setSelected} compact />}
    </div>
  ) : null;
  return <TabBar {...props} className={`${props.className ?? ""} ${slot ? styles.withPicker : ""}`} children={slot ? [slot, ...tabs] : tabs} />;
};
