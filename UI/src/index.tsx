import { useValue } from "cs2/api";
import { ModRegistrar } from "cs2/modding";
import { budgetChartEnabled$ } from "bindings";
import { BudgetPage } from "budget/budget-page";
import { withServiceDetailDistricts, withServicePageDistricts } from "services/service-page";
import { DensityRows } from "taxation/density-rows";
import { useZoneDensityExtra, withEstimatedTotalDensityExtra, withTotalDensityExtra, withZoneDensityExtra, ZoneDensityExtra } from "taxation/density-totals";
import { BUDGET_PAGE_PATH, MONEY_FIELD_PATH, SERVICE_DETAIL_PATH, SERVICE_PAGE_PATH, TAX_SLIDER_PATH, TAXATION_GROUP_PATH, TAXATION_PAGE_PATH } from "vanilla";

const register: ModRegistrar = (moduleRegistry) => {
  // Budget tab: the line-chart page in place of the whole vanilla page (bar chart + lists), or the vanilla page
  // itself while Options > Enable budget chart is off.
  moduleRegistry.extend(BUDGET_PAGE_PATH, "BudgetPage", (VanillaBudgetPage) => (props: any) => {
    const enabled = useValue(budgetChartEnabled$);
    return enabled ? <BudgetPage /> : <VanillaBudgetPage {...props} />;
  });

  // Taxation tab: keep each vanilla tax group and add density rows under it (none while density taxes are off).
  // The group's total (its header slider) includes the zone's density extra, provided to the extended TaxSlider.
  moduleRegistry.extend(TAXATION_GROUP_PATH, "TaxationGroup", (TaxationGroup) => (props: any) => (
    <>
      <ZoneDensityExtra.Provider value={useZoneDensityExtra(props.areaType)}>
        <TaxationGroup {...props} />
      </ZoneDensityExtra.Provider>
      <DensityRows areaType={props.areaType} />
    </>
  ));

  // Taxation tab totals: ESTIMATED TOTAL and each zone's total include the density offsets' extra revenue
  // (taxation/density-totals.tsx). Both pass through unchanged outside the Taxation page.
  moduleRegistry.extend(TAXATION_PAGE_PATH, "TaxationPage", withTotalDensityExtra);
  moduleRegistry.extend(TAX_SLIDER_PATH, "TaxSlider", withZoneDensityExtra);
  moduleRegistry.extend(MONEY_FIELD_PATH, "NumberField", withEstimatedTotalDensityExtra);

  // Services tab, per-district budgets (BETA): the district selector above the page, and in a district view the
  // district's own detail (slider, "Use city budget", upkeep) in place of vanilla's. Off: both are vanilla.
  moduleRegistry.extend(SERVICE_PAGE_PATH, "ServicePage", withServicePageDistricts);
  moduleRegistry.extend(SERVICE_DETAIL_PATH, "ServiceDetail", withServiceDetailDistricts);
};

export default register;
