import { useValue } from "cs2/api";
import { ModRegistrar } from "cs2/modding";
import { budgetChartEnabled$ } from "bindings";
import { BudgetPage } from "budget/budget-page";
import { withEstimatedTotalDensityExtra, withTotalDensityExtra, withZoneDensityExtra } from "taxation/density-totals";
import { withTaxSections } from "taxation/tax-sections";
import { BUDGET_PAGE_PATH, MONEY_FIELD_PATH, TAX_SLIDER_PATH, TAXATION_GROUP_PATH, TAXATION_PAGE_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Budget tab: the line-chart page in place of the whole vanilla page (bar chart + lists), or the vanilla page
  // itself while Options > Enable budget chart is off.
  moduleRegistry.extend(BUDGET_PAGE_PATH, "BudgetPage", (VanillaBudgetPage) => (props: any) => {
    const enabled = useValue(budgetChartEnabled$);
    return enabled ? <BudgetPage /> : <VanillaBudgetPage {...props} />;
  });

  // Taxation tab: keep each vanilla tax group; with density taxes on, its sub-rows become collapsible sections under it
  // (Density, then vanilla's own rows: taxation/tax-sections.tsx). Off: the vanilla group as is.
  // The group's total (its header slider) includes the zone's density extra, provided to the extended TaxSlider.
  moduleRegistry.extend(TAXATION_GROUP_PATH, "TaxationGroup", withTaxSections);

  // Taxation tab totals: ESTIMATED TOTAL and each zone's total include the density offsets' extra revenue
  // (taxation/density-totals.tsx). Both pass through unchanged outside the Taxation page.
  moduleRegistry.extend(TAXATION_PAGE_PATH, "TaxationPage", withTotalDensityExtra);
  moduleRegistry.extend(TAX_SLIDER_PATH, "TaxSlider", withZoneDensityExtra);
  moduleRegistry.extend(MONEY_FIELD_PATH, "NumberField", withEstimatedTotalDensityExtra);
};

export default register;
