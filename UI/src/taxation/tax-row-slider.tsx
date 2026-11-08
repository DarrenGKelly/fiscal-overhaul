import { useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { clearTaxRate, setDensityTax, taxRates$ } from "bindings";
import { SliderResetFrame, FrameLayout } from "common/slider-reset-frame";
import { markerFraction, resetVisibility } from "common/slider-reset";
import { localize } from "l10n";
import { RowReset, RowTarget, TYPE_DENSITY, TYPE_RESOURCE_RATE, TYPE_ZONE_RATE } from "./district-tax";
import styles from "./tax-row-slider.module.scss";

// A Taxation tab slider row with its reset button and target marker (common/slider-reset-frame.tsx): every zone
// header, education/resource row and density row. Vanilla TaxSlider is [slider column | estimate column]; the button
// sits in the slider column's right padding (widened for it), just left of the divider before the estimate.

/** Vanilla TaxSlider's DOM: root > [slider column > [rate, track], estimate column]. */
function locateTaxSlider(frame: HTMLElement): FrameLayout {
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

/** What a row's reset does: in the city view the row's own setter with the default, in a district view its override cleared. */
export function resetRow(target: RowTarget, area: string, kind: RowReset["kind"], value: number) {
  if (kind === "city") {
    clearTaxRate(target.type, target.zone, target.key, area);
    return;
  }
  switch (target.type) {
    case TYPE_ZONE_RATE:
      taxation.setAreaTaxRate(target.zone + 1, value);
      break;
    case TYPE_RESOURCE_RATE:
      taxation.setResourceTaxRate(target.key, target.zone + 1, value);
      break;
    case TYPE_DENSITY:
      setDensityTax(target.zone, target.key, value);
      break;
  }
}

/** The defaults the C# side sends (vanilla's), for rowReset. */
export function useTaxDefaults() {
  return useValue(taxRates$)?.defaults;
}

export interface TaxRowSliderProps {
  /** The TaxSlider to draw (the extended chain's next one, or vanilla's). */
  TaxSlider: any;
  /** Its props; className is the parent's layout class, moved to the frame. */
  sliderProps: any;
  target: RowTarget;
  area: string;
  reset: RowReset;
  /** Classes for the slider itself (e.g. the district view's overridden bar). */
  sliderClass?: string | null;
}

export const TaxRowSlider = ({ TaxSlider, sliderProps, target, area, reset, sliderClass }: TaxRowSliderProps) => {
  const { translate } = useLocalization();
  const { className, ...rest } = sliderProps;
  // A locked zone's slider is disabled: nothing to reset.
  const visibility = resetVisibility(reset.differs && !rest.disabled);
  const tooltip = localize(translate, reset.kind === "city" ? "ResetToCity" : "ResetToDefault");
  return (
    <SliderResetFrame
      className={className}
      locate={locateTaxSlider}
      fraction={markerFraction(reset.target, sliderProps.min, sliderProps.max)}
      reset={{ kind: reset.kind, visibility, tooltip, onReset: () => resetRow(target, area, reset.kind, reset.target) }}
    >
      <TaxSlider {...rest} className={[styles.slider, sliderClass].filter(Boolean).join(" ")} />
    </SliderResetFrame>
  );
};
