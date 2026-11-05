import { useValue } from "cs2/api";
import { ModRegistrar } from "cs2/modding";
import { budgetChartEnabled$ } from "bindings";
import { BudgetPage } from "budget/budget-page";
import { DensityRows } from "taxation/density-rows";
import { BUDGET_PAGE_PATH, TAXATION_GROUP_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Budget tab: the line-chart page in place of the whole vanilla page (bar chart + lists), or the vanilla page
  // itself while Options > Enable budget chart is off.
  moduleRegistry.extend(BUDGET_PAGE_PATH, "BudgetPage", (VanillaBudgetPage) => (props: any) => {
    const enabled = useValue(budgetChartEnabled$);
    return enabled ? <BudgetPage /> : <VanillaBudgetPage {...props} />;
  });

  // Taxation tab: keep each vanilla tax group and add density rows under it (none while density taxes are off).
  moduleRegistry.extend(TAXATION_GROUP_PATH, "TaxationGroup", (TaxationGroup) => (props: any) => (
    <>
      <TaxationGroup {...props} />
      <DensityRows areaType={props.areaType} />
    </>
  ));
};

export default register;
