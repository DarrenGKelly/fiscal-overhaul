// The Services tab as a table like the Taxation tab's (with District service budgets on): Type | Budget / Fee |
// Estimate, one row per service with its budget slider and NET, a sub-row per adjustable fee, and an estimated total.
// Always the Services tab (a user decision). The city view sets vanilla's budget and fees; with District service
// budgets (BETA) on, a district view (the header's picker) sets the area's budget and shows the city's fees
// read-only. Off: the table, city-only.
import { useEffect, useMemo, useState } from "react";
import { useMapValue, useValue } from "cs2/api";
import { service as serviceBindings } from "cs2/bindings";
import { FOCUS_DISABLED } from "cs2/input";
import { useLocalization } from "cs2/l10n";
import {
  districts$,
  districtServiceBudgetEnabled$,
  districtServiceBudgets$,
  districtServiceFees$,
  serviceFeeDefaults$,
  setDistrictServiceBudget,
} from "bindings";
import { FrameLayout, SliderResetFrame } from "common/slider-reset-frame";
import { markerFraction, resetVisibility } from "common/slider-reset";
import { districtOptions, effectiveKey } from "district/district-select";
import { useSelectedDistrict } from "district/selection";
import { localize, localizeVanilla, Translate } from "l10n";
import {
  ActiveFocusDiv,
  economyGradientSliderClasses,
  GradientSlider,
  intTransformer,
  NumberField,
  Scrollable,
  Section,
  taxationGroupClasses,
  taxationItemClasses,
  taxationPageClasses,
  taxSliderClasses,
  useLegacyInterfaceActive,
} from "vanilla";
import { clampBudget, isDistrictView, MAX_BUDGET, MIN_BUDGET, serviceKey } from "./district-services";
import {
  budgetReset,
  budgetValue,
  estimateTotal,
  FEE_SLIDER_MAX,
  feeDefault,
  feeRowEstimate,
  feeFromSlider,
  FeeLike,
  feeReset,
  feeRows,
  feeToSlider,
  serviceEstimate,
  ServiceLike,
  SliderReset,
  tableDistrict,
} from "./service-table";
import styles from "./service-table.module.scss";

// Vanilla BudgetSliderItem's gradients (red at 50 %, green at 150 %), and ServiceFeeSliderItem's (the reverse):
// the legacy interface's, and the current one. Not exported by the game.
const BUDGET_GRADIENT_LEGACY = { stops: [{ offset: 0, color: "var(--negativeColor)" }, { offset: 0.5, color: "#bfac6f" }, { offset: 1, color: "var(--positiveColor)" }] };
const BUDGET_GRADIENT = { stops: [{ offset: 0, color: "var(--highlightWarningRed)" }, { offset: 0.5, color: "#ffcb00" }, { offset: 1, color: "var(--highlightGreen)" }] };
const FEE_GRADIENT_LEGACY = { stops: [{ offset: 0, color: "var(--positiveColor)" }, { offset: 0.5, color: "#bfac6f" }, { offset: 1, color: "var(--negativeColor)" }] };
const FEE_GRADIENT = { stops: [{ offset: 0, color: "var(--highlightGreen)" }, { offset: 0.5, color: "#ffcb00" }, { offset: 1, color: "var(--highlightWarningRed)" }] };

// Vanilla's classes, or this module's copies should a game patch move a module.
const page = {
  page: taxationPageClasses?.taxationPage ?? styles.page,
  main: taxationPageClasses?.mainColumn ?? styles.main,
  tableSection: taxationPageClasses?.tableSection ?? styles.tableSection,
  tableHeader: taxationPageClasses?.tableHeader ?? styles.tableHeader,
  headerCell: taxationPageClasses?.headerCell ?? styles.headerCell,
  scrollable: taxationPageClasses?.scrollable ?? styles.scrollable,
  tableLines: taxationPageClasses?.tableLines ?? styles.tableLines,
  name: taxationPageClasses?.name ?? styles.name,
  rate: taxationPageClasses?.rate ?? styles.rate,
  estimate: taxationPageClasses?.estimate ?? styles.estimate,
  totalSection: taxationPageClasses?.totalSection ?? styles.totalSection,
  totalContent: taxationPageClasses?.totalContent ?? styles.totalContent,
  totalLabel: taxationPageClasses?.totalLabel ?? styles.totalLabel,
  totalColumn: taxationPageClasses?.totalColumn ?? styles.totalColumn,
};
const group = {
  group: taxationGroupClasses?.taxationGroup ?? styles.group,
  header: taxationGroupClasses?.header ?? styles.header,
  icon: taxationGroupClasses?.icon ?? styles.groupIcon,
  iconDisabled: taxationGroupClasses?.iconDisabled ?? styles.iconDisabled,
  title: taxationGroupClasses?.title ?? styles.groupTitle,
  slider: taxationGroupClasses?.headerSlider ?? styles.rowSlider,
};
const item = {
  row: taxationItemClasses?.taxationItem ?? styles.item,
  icon: taxationItemClasses?.icon ?? styles.itemIcon,
  title: taxationItemClasses?.title ?? styles.itemTitle,
  slider: taxationItemClasses?.slider ?? styles.rowSlider,
};
const slider = {
  root: taxSliderClasses?.taxSlider ?? styles.taxSlider,
  primary: taxSliderClasses?.primary ?? "",
  column: taxSliderClasses?.sliderColumn ?? styles.sliderColumn,
  rate: taxSliderClasses?.rate ?? styles.rateText,
  track: taxSliderClasses?.slider ?? styles.track,
  estimateColumn: taxSliderClasses?.estimateColumn ?? styles.estimateColumn,
  estimateValue: taxSliderClasses?.estimateValue ?? styles.estimateValue,
};
const RowBox = ActiveFocusDiv ?? "div";
const cx = (...names: (string | false | null | undefined)[]) => names.filter(Boolean).join(" ");

/** ServicePage override: our table always replaces vanilla's page (service list, detail, info column). */
export const ServiceTablePage = () => <ServiceTable />;

/** The header picker's choice (economy/economy-tab-bar.tsx) as the Services tab sees it: "" city, a district, "outside". */
function useServiceDistrict(): string {
  const enabled = useValue(districtServiceBudgetEnabled$);
  const districts = useValue(districts$);
  const options = useMemo(() => districtOptions(districts ?? []), [districts]);
  const [key] = useSelectedDistrict();
  return tableDistrict(enabled, effectiveKey(options, key));
}

const ServiceTable = () => {
  const { translate } = useLocalization();
  const services = useValue(serviceBindings.services$) as ServiceLike[];
  const budgets = useValue(districtServiceBudgets$);
  const fees = useValue(districtServiceFees$);
  const district = useServiceDistrict();
  const data = { budgets, fees };
  const keyed = (services ?? []).map((s) => ({ key: serviceKey(s.entity), budget: s.budget }));
  const total = estimateTotal(keyed, district, data);
  const tr = translate as Translate;
  return (
    <div className={page.page}>
      <div className={page.main}>
        <Section
          className={page.tableSection}
          header={
            <div className={page.tableHeader}>
              <div className={cx(page.headerCell, page.name)}>{vanillaText(tr, "EconomyPanel.TAX_AREA_COLUMN_TITLE", "Type")}</div>
              <div className={cx(page.headerCell, page.rate)}>{localize(tr, "ServiceColumnBudgetFee")}</div>
              <div className={cx(page.headerCell, page.estimate)}>{vanillaText(tr, "EconomyPanel.TAX_INCOME_ESTIMATE_TITLE", "Estimate")}</div>
            </div>
          }
        >
          <Scrollable className={page.scrollable}>
            {(services ?? []).map((s) => (
              <ServiceGroup key={serviceKey(s.entity)} service={s} district={district} data={data} translate={tr} />
            ))}
          </Scrollable>
          <div className={page.tableLines}>
            <div className={page.name} />
            <div className={page.rate} />
            <div className={page.estimate} />
          </div>
        </Section>
        <Section focusKey={FOCUS_DISABLED} className={page.totalSection}>
          <div className={page.totalContent}>
            <div className={page.totalLabel}>{vanillaText(tr, "EconomyPanel.TAX_INCOME_TOTAL_TITLE", "Estimated total")}</div>
            <div className={page.totalColumn}>
              {/* Not the Taxation page's totalValue class: the extended NumberField adds the density total to that one. */}
              <NumberField value={total} importance="primary" className={styles.totalValue} />
            </div>
          </div>
        </Section>
      </div>
    </div>
  );
};

function vanillaText(translate: Translate, id: string, fallback: string): string {
  return translate(id, fallback) || fallback;
}

interface GroupProps {
  service: ServiceLike;
  district: string;
  data: { budgets: any; fees: any };
  translate: Translate;
}

/** One service: its header row (icon, name, budget slider, NET) and a sub-row per adjustable fee. */
const ServiceGroup = ({ service, district, data, translate }: GroupProps) => {
  const details = useMapValue(serviceBindings.serviceDetails$, service.entity);
  const defaults = useValue(serviceFeeDefaults$);
  const legacy = useLegacyInterfaceActive ? useLegacyInterfaceActive() : false;
  const key = serviceKey(service.entity);
  const districtView = isDistrictView(district);
  const locked = service.locked;
  const city = details?.budgetPercentage ?? 100;
  const reset = budgetReset(data.budgets, key, district, city);
  const value = budgetValue(data.budgets, key, district, city);
  const estimate = serviceEstimate({ key, budget: service.budget }, district, data);
  const setBudget = (v: number) => {
    const budget = clampBudget(v);
    if (districtView) setDistrictServiceBudget(key, district, budget);
    else serviceBindings.setServiceBudget(service.entity, budget);
  };
  const resetBudget = () => {
    if (reset.kind === "city") setDistrictServiceBudget(key, district, 0);
    else serviceBindings.setServiceBudget(service.entity, reset.target ?? 100);
  };
  const name = translate(`Services.NAME[${service.name}]`, service.name) ?? service.name;
  return (
    <div className={group.group}>
      <RowBox className={group.header}>
        <img className={cx(group.icon, locked && group.iconDisabled)} src={service.icon} />
        <div className={cx(group.title, locked && "disabled")}>{name}</div>
        <ServiceSlider
          className={group.slider}
          primary
          slider={
            details?.budgetAdjustable
              ? { value, min: MIN_BUDGET, max: MAX_BUDGET, gradient: legacy ? BUDGET_GRADIENT_LEGACY : BUDGET_GRADIENT, onChange: setBudget }
              : null
          }
          estimate={estimate}
          disabled={locked}
          reset={reset}
          onReset={resetBudget}
          translate={translate}
        />
      </RowBox>
      {feeRows<FeeLike>(details?.fees, districtView).map((fee) => (
        <FeeRow
          key={fee.resource}
          fee={fee}
          service={key}
          district={district}
          defaultFee={feeDefault(defaults, fee.resource)}
          fees={data.fees}
          locked={locked}
          legacy={legacy}
          translate={translate}
        />
      ))}
    </div>
  );
};

interface FeeRowProps {
  fee: FeeLike;
  service: string;
  district: string;
  defaultFee: number | null;
  fees: any;
  locked: boolean;
  legacy: boolean;
  translate: Translate;
}

/** A fee sub-row, drawn as a Taxation education/resource row. City-wide: read-only (disabled) in a district view. */
const FeeRow = ({ fee, service, district, defaultFee, fees, locked, legacy, translate }: FeeRowProps) => {
  const reset = feeReset(fee, defaultFee, district);
  const readOnly = isDistrictView(district);
  const label = translate(`EconomyPanel.SERVICES_RESOURCE_FEE_LABEL[${fee.name}]`, fee.name) ?? fee.name;
  return (
    <RowBox className={item.row}>
      <div className={item.icon} />
      <div className={cx(item.title, (locked || readOnly) && "disabled")}>{label}</div>
      <ServiceSlider
        className={item.slider}
        slider={{
          value: feeToSlider(fee.fee, fee.min, fee.max),
          min: 0,
          max: FEE_SLIDER_MAX,
          gradient: legacy ? FEE_GRADIENT_LEGACY : FEE_GRADIENT,
          onChange: (v: number) => serviceBindings.setServiceFee(fee.resource, feeFromSlider(v, fee.min, fee.max)),
        }}
        estimate={feeRowEstimate()}
        disabled={locked || readOnly}
        reset={reset}
        onReset={() => defaultFee !== null && serviceBindings.setServiceFee(fee.resource, defaultFee)}
        translate={translate}
      />
    </RowBox>
  );
};

interface SliderSpec {
  value: number;
  min: number;
  max: number;
  gradient: any;
  onChange: (v: number) => void;
}

interface ServiceSliderProps {
  className: string;
  primary?: boolean;
  /** Null: no slider (a service whose budget isn't adjustable), only its estimate. */
  slider: SliderSpec | null;
  /** Null: a blank estimate column (a fee row: its income is in the service's NET). */
  estimate: number | null;
  disabled: boolean;
  reset: SliderReset;
  onReset: () => void;
  translate: Translate;
}

/** TaxSlider's DOM: root > [slider column > [rate, track], estimate column]: the reset frame finds the track and the gap. */
function locateSlider(frame: HTMLElement): FrameLayout {
  const root = frame.children[0] as HTMLElement | undefined;
  const column = root?.children[0] as HTMLElement | undefined;
  const estimate = root?.children[1] as HTMLElement | undefined;
  const track = (column?.children[1] as HTMLElement | undefined) ?? null;
  if (!track || !estimate) return { track, slot: null };
  const f = frame.getBoundingClientRect();
  const t = track.getBoundingClientRect();
  const e = estimate.getBoundingClientRect();
  const width = e.left - t.right;
  return { track, slot: width > 0 ? { right: f.right - e.left, width } : null };
}

/**
 * A slider row in TaxSlider's own DOM and classes (so it lines up with the Taxation tab's columns), with the
 * service sliders' gradients and a whole-% label; its reset button and target marker over it.
 */
const ServiceSlider = ({ className, primary, slider: spec, estimate, disabled, reset, onReset, translate }: ServiceSliderProps) => {
  // The value being dragged, shown until the binding comes back with it (a round trip through C#).
  const [pending, setPending] = useState<number | null>(null);
  const shown = spec ? Math.round(spec.value) : null;
  useEffect(() => {
    if (pending !== null && pending === shown) setPending(null);
  }, [pending, shown]);
  useEffect(() => setPending(null), [spec?.min, spec?.max]);

  const value = pending ?? shown;
  const visibility = resetVisibility(reset.differs && !disabled && !!spec);
  const tooltip = localize(translate, reset.kind === "city" ? "ResetServiceToCity" : "ResetServiceToDefault");
  const row = (
    <div className={cx(slider.root, primary && slider.primary, styles.slider)}>
      <div className={slider.column}>
        <div className={cx(slider.rate, disabled && "disabled")}>{value === null ? "" : `${value}%`}</div>
        {spec && GradientSlider ? (
          <GradientSlider
            value={value}
            start={spec.min}
            end={spec.max}
            gamepadStep={primary ? 2 : 1}
            valueTransformer={intTransformer}
            theme={economyGradientSliderClasses}
            gradient={spec.gradient}
            className={cx(slider.track, disabled && "disabled")}
            disabled={disabled}
            onChange={(v: number) => {
              const next = Math.round(v);
              setPending(next);
              spec.onChange(next);
            }}
          />
        ) : (
          <div className={slider.track} />
        )}
      </div>
      <div className={slider.estimateColumn}>
        {estimate !== null && <NumberField value={estimate} importance={primary ? "secondary" : undefined} className={slider.estimateValue} />}
      </div>
    </div>
  );
  return (
    <SliderResetFrame
      className={className}
      locate={locateSlider}
      fraction={spec && reset.target !== null ? markerFraction(reset.target, spec.min, spec.max) : null}
      reset={{ kind: reset.kind, visibility, tooltip, onReset }}
    >
      {row}
    </SliderResetFrame>
  );
};
