import { ModRegistrar } from "cs2/modding";
import { BudgetPage } from "budget/budget-page";
import { DensityRows } from "taxation/density-rows";
import { BUDGET_PAGE_PATH, TAXATION_GROUP_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Budget tab: replace the whole page (bar chart + lists) with the line-chart page.
  moduleRegistry.override(BUDGET_PAGE_PATH, "BudgetPage", BudgetPage);

  // Taxation tab: keep each vanilla tax group and add density rows under it.
  moduleRegistry.extend(TAXATION_GROUP_PATH, "TaxationGroup", (TaxationGroup) => (props: any) => (
    <>
      <TaxationGroup {...props} />
      <DensityRows areaType={props.areaType} />
    </>
  ));
};

export default register;
