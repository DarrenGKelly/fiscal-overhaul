// The Services tab's per-district budgets (BETA): in a district view (the Economy panel header's picker) our own
// service detail (the area's slider, "Use city budget", the area's upkeep) in place of vanilla's,
// which has the city-wide slider, fees and Reset. Feature off (or Simulation changes off): the vanilla tab, untouched.
import { useEffect, useMemo, useState } from "react";
import { useMapValue, useValue } from "cs2/api";
import { service as serviceBindings } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { districts$, districtServiceBudgetEnabled$, districtServiceBudgets$, setDistrictServiceBudget } from "bindings";
import { districtOptions, effectiveKey } from "district/district-select";
import { useSelectedDistrict } from "district/selection";
import { localize, localizeVanilla } from "l10n";
import { BudgetItem, Button, Scrollable, Section, serviceDetailClasses, SliderItem, useLegacyInterfaceActive } from "vanilla";
import { clampBudget, isDistrictView, isOverridden, MAX_BUDGET, MIN_BUDGET, serviceKey, sliderValue, upkeepFor } from "./district-services";
import styles from "./service-page.module.scss";

// Vanilla's info-column identifiers (economy-panel ServiceItemType: BudgetSlider = 1, ServiceUpkeep = 5), so hovering
// our rows explains them as vanilla's do. Module constants: vanilla memoises its handlers on the identifier.
const BUDGET_SLIDER_ID = { type: 1 };
const UPKEEP_ID = { type: 5 };

// Vanilla BudgetSliderItem's two gradients (not exported): the legacy interface's, and the current one.
const GRADIENT_LEGACY = {
  stops: [
    { offset: 0, color: "var(--negativeColor)" },
    { offset: 0.5, color: "#bfac6f" },
    { offset: 1, color: "var(--positiveColor)" },
  ],
};
const GRADIENT = {
  stops: [
    { offset: 0, color: "var(--highlightWarningRed)" },
    { offset: 0.5, color: "#ffcb00" },
    { offset: 1, color: "var(--highlightGreen)" },
  ],
};

/**
 * The Services tab's view of the shared district choice (set by the header's picker, economy/economy-tab-bar.tsx):
 * on/off and the effective selection.
 */
function useServiceDistrict() {
  const enabled = useValue(districtServiceBudgetEnabled$);
  const districts = useValue(districts$);
  const options = useMemo(() => districtOptions(districts ?? []), [districts]);
  const [key] = useSelectedDistrict();
  return { enabled, selected: effectiveKey(options, key) };
}

/** ServiceDetail extension: in a district view, the district's detail; otherwise (and feature off) vanilla's. */
export const withServiceDetailDistricts = (ServiceDetail: any) => (props: any) => {
  const { enabled, selected } = useServiceDistrict();
  if (!enabled || !isDistrictView(selected) || !props.service) return <ServiceDetail {...props} />;
  return <DistrictServiceDetail service={props.service} district={selected} className={props.className} />;
};

/**
 * One service in one area, built from vanilla's pieces and classes: the title, "Use city budget" where vanilla has
 * Reset, the area's budget slider (when vanilla's is adjustable), and the area's upkeep. No fees: they are city-wide.
 */
const DistrictServiceDetail = ({ service, district, className }: { service: any; district: string; className?: string }) => {
  const details = useMapValue(serviceBindings.serviceDetails$, service);
  const rows = useValue(districtServiceBudgets$);
  const legacy = useLegacyInterfaceActive ? useLegacyInterfaceActive() : false;
  const { translate } = useLocalization();
  const classes = serviceDetailClasses ?? {};

  const key = serviceKey(details?.entity ?? service);
  const city = details?.budgetPercentage ?? 100;
  const overridden = isOverridden(rows, key, district);
  const value = sliderValue(rows, key, district, city);

  // The value being dragged, shown until the binding comes back with it (a round trip through C#).
  const [pending, setPending] = useState<number | null>(null);
  useEffect(() => setPending(null), [key, district]);
  useEffect(() => {
    if (pending !== null && pending === value) setPending(null);
  }, [pending, value]);

  if (!details) return <Section className={className} />;
  const locked = details.locked;
  const change = (v: number) => {
    const budget = clampBudget(v);
    setPending(budget);
    setDistrictServiceBudget(key, district, budget);
  };
  const useCity = () => {
    setPending(null);
    setDistrictServiceBudget(key, district, 0);
  };

  return (
    <Section
      className={className}
      header={
        <div className={classes.header}>
          <div className={classes.title}>{translate(`Services.NAME[${details.name}]`, details.name) ?? details.name}</div>
          {details.budgetAdjustable && (
            <Button className={classes.resetButton} disabled={!overridden || locked} onSelect={useCity}>
              {localize(translate, "UseCityBudget")}
            </Button>
          )}
        </div>
      }
    >
      <Scrollable className={classes.scrollable}>
        <div className={classes.content}>
          {details.budgetAdjustable && (
            <div className={classes.adjustment}>
              <SliderItem
                identifier={BUDGET_SLIDER_ID}
                label={localizeVanilla(translate, "serviceBudget")}
                value={pending ?? value}
                min={MIN_BUDGET}
                max={MAX_BUDGET}
                gradient={legacy ? GRADIENT_LEGACY : GRADIENT}
                locked={locked}
                onValueChange={change}
              />
              <div className={styles.hint}>
                {overridden
                  ? localize(translate, "DistrictBudgetOwn", { PERCENT: city })
                  : localize(translate, "DistrictBudgetFollowsCity", { PERCENT: city })}
              </div>
            </div>
          )}
          <div className={classes.space} />
          <div className={classes.budget}>
            <div className={classes.budgetTitle}>{localizeVanilla(translate, "serviceExpenses")}</div>
            {/* Vanilla shows upkeep as a negative amount (ServiceBudgetUISystem writes -upkeep). */}
            <BudgetItem identifier={UPKEEP_ID} label={localizeVanilla(translate, "serviceUpkeep")} value={-upkeepFor(rows, key, district)} />
          </div>
        </div>
      </Scrollable>
    </Section>
  );
};
