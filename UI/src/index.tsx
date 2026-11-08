import { useValue } from "cs2/api";
import { ModRegistrar } from "cs2/modding";
import { budgetChartEnabled$ } from "bindings";
import { BudgetPage } from "budget/budget-page";
import { withServiceDetailDistricts, withServicePageDistricts } from "services/service-page";
import { withEstimatedTotalDensityExtra, withTotalDensityExtra, withZoneDensityExtra } from "taxation/density-totals";
import { withTaxDistrictSelector } from "taxation/district-tax-view";
import { withTaxSections } from "taxation/tax-sections-view";
import { orderEconomyTabs } from "economy/tab-order";
import { BUDGET_PAGE_PATH, MONEY_FIELD_PATH, SERVICE_DETAIL_PATH, SERVICE_PAGE_PATH, TAB_BAR_PATH, TAX_SLIDER_PATH, TAXATION_GROUP_PATH, TAXATION_PAGE_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Economy panel tabs: Budget, Taxation, Services, Production, Loans. Any other tab bar passes through untouched.
  moduleRegistry.extend(TAB_BAR_PATH, "TabBar", (TabBar) => (props: any) => (
    <TabBar {...props} children={Array.isArray(props.children) ? orderEconomyTabs(props.children) : props.children} />
  ));

  // Budget tab: the line-chart page in place of the whole vanilla page (bar chart + lists), or the vanilla page
  // itself while Options > Enable budget chart is off.
  moduleRegistry.extend(BUDGET_PAGE_PATH, "BudgetPage", (VanillaBudgetPage) => (props: any) => {
    const enabled = useValue(budgetChartEnabled$);
    return enabled ? <BudgetPage /> : <VanillaBudgetPage {...props} />;
  });

  // Taxation tab: keep each vanilla tax group; with density taxes on, its sub-rows become collapsible sections under it
  // (Density, then vanilla's own rows: taxation/tax-sections-view.tsx). Off: the vanilla group as is.
  // The group's total (its header slider) includes the zone's density extra, provided to the extended TaxSlider.
  moduleRegistry.extend(TAXATION_GROUP_PATH, "TaxationGroup", withTaxSections);

  // Taxation tab totals: ESTIMATED TOTAL and each zone's total include the density offsets' extra revenue
  // (taxation/density-totals.tsx). Both pass through unchanged outside the Taxation page.
  // Above it, the district selector while the tax engine charges and the city has a district; a picked district's view
  // shows its adjustment per zone (taxation/district-tax-view.tsx).
  moduleRegistry.extend(TAXATION_PAGE_PATH, "TaxationPage", (TaxationPage: any) => withTaxDistrictSelector(withTotalDensityExtra(TaxationPage)));
  moduleRegistry.extend(TAX_SLIDER_PATH, "TaxSlider", withZoneDensityExtra);
  moduleRegistry.extend(MONEY_FIELD_PATH, "NumberField", withEstimatedTotalDensityExtra);

  // Services tab, per-district budgets (BETA): the district selector above the page, and in a district view the
  // district's own detail (slider, "Use city budget", upkeep) in place of vanilla's. Off: both are vanilla.
  moduleRegistry.extend(SERVICE_PAGE_PATH, "ServicePage", withServicePageDistricts);
  moduleRegistry.extend(SERVICE_DETAIL_PATH, "ServiceDetail", withServiceDetailDistricts);
};

export default register;
