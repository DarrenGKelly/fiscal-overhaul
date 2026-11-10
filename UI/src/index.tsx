import { useValue } from "cs2/api";
import { ModRegistrar } from "cs2/modding";
import { budgetChartEnabled$, servicesTableEnabled$ } from "bindings";
import { BudgetPage } from "budget/budget-page";
import { ServiceTablePage } from "services/service-table-view";
import { withEstimatedTotalDensityExtra, withTotalDensityExtra, withZoneDensityExtra } from "taxation/density-totals";
import { withTaxDistrictSelector } from "taxation/district-tax-view";
import { withTaxSections } from "taxation/tax-sections-view";
import { EconomyTabBar, isEconomyTabSet } from "economy/economy-tab-bar";
import { BUDGET_PAGE_PATH, MONEY_FIELD_PATH, SERVICE_PAGE_PATH, TAB_BAR_PATH, TAX_SLIDER_PATH, TAXATION_GROUP_PATH, TAXATION_PAGE_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Economy panel header: the one district picker at the left, then the tabs Budget, Taxation, Services | Production,
  // Loans (economy/economy-tab-bar.tsx). Any other tab bar passes through untouched.
  moduleRegistry.extend(TAB_BAR_PATH, "TabBar", (TabBar) => (props: any) =>
    isEconomyTabSet(props.children) ? <EconomyTabBar TabBar={TabBar} props={props} /> : <TabBar {...props} />
  );

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
  // The header's district picker chooses the view; a picked district's view shows its adjustment per zone, with
  // "Use city rates" above the page (taxation/district-tax-view.tsx).
  moduleRegistry.extend(TAXATION_PAGE_PATH, "TaxationPage", (TaxationPage: any) => withTaxDistrictSelector(withTotalDensityExtra(TaxationPage)));
  moduleRegistry.extend(TAX_SLIDER_PATH, "TaxSlider", withZoneDensityExtra);
  moduleRegistry.extend(MONEY_FIELD_PATH, "NumberField", withEstimatedTotalDensityExtra);

  // Services tab: a table like the Taxation tab's, one row per service with its budget slider and cost, a row per fee
  // with its revenue, an estimated total and vanilla's info column; with District service budgets (BETA) on, the
  // header's picker shows a district's budgets and estimates (services/service-table-view.tsx). Options > Enable
  // services table off: the vanilla page.
  moduleRegistry.extend(SERVICE_PAGE_PATH, "ServicePage", (VanillaServicePage) => (props: any) => {
    const enabled = useValue(servicesTableEnabled$);
    return enabled ? <ServiceTablePage /> : <VanillaServicePage {...props} />;
  });
};

export default register;
