import { useMapValue, useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { FC, useCallback, useState } from "react";
import { densityTaxesEnabled$, playSound } from "bindings";
import { localize } from "l10n";
import { SOUNDS } from "sounds";
import { TaxationItem, taxationGroupClasses } from "vanilla";
import { arrowFor } from "./density";
import { DensityRows, DensitySummary, useDensityCells } from "./density-rows";
import { useZoneDensityExtra, ZoneDensityExtra } from "./density-totals";
import { groupClasses, sectionExpandState, sectionsForArea, TaxSection, TaxSectionId } from "./tax-sections";
import styles from "./tax-sections.module.scss";

// The Taxation tab with density taxes on: under each zone's main slider, collapsible sections (tax-sections.ts):
// Density, then vanilla's own rows (education levels or resources), whose vanilla expander is hidden. Off: the vanilla
// TaxationGroup exactly.

type AreaType = taxation.TaxAreaType;

/** Vanilla's own sub-rows of a zone, as vanilla draws them inside its group. */
const VanillaRows = ({ areaType }: { areaType: AreaType }) => {
  const resources = useMapValue(taxation.areaResources$, areaType.index) ?? [];
  return (
    <>
      {resources.map((r) => (
        <TaxationItem key={r.resource} areaType={areaType} resource={r} />
      ))}
    </>
  );
};

/** What each section draws: its rows, and an optional summary at the right of its header. */
const TAX_SECTION_ROWS: Record<TaxSectionId, { Rows: FC<{ areaType: AreaType }>; Summary?: FC<{ areaType: AreaType }> }> = {
  density: { Rows: DensityRows, Summary: DensitySummary },
  vanilla: { Rows: VanillaRows },
};

// TaxationGroup's class names as the game ships them, before any wrapper has changed the live object.
const VANILLA_GROUP_CLASSES = { ...taxationGroupClasses };

/** Hides (on) or restores (off) vanilla TaxationGroup's own expander, which reads these names on every render. */
function hideVanillaExpander(hide: boolean) {
  if (taxationGroupClasses) Object.assign(taxationGroupClasses, groupClasses(VANILLA_GROUP_CLASSES, styles.invisible, styles.removed, hide));
}

/** The TaxationGroup extension (index.tsx): the zone total with its density extra, then the sections while density taxes are on. */
export const withTaxSections = (TaxationGroup: any) => (props: any) => {
  const enabled = useValue(densityTaxesEnabled$);
  const extra = useZoneDensityExtra(props.areaType);
  hideVanillaExpander(enabled);
  return (
    <>
      <ZoneDensityExtra.Provider value={extra}>
        <TaxationGroup {...props} />
      </ZoneDensityExtra.Provider>
      {enabled && <TaxSections areaType={props.areaType} />}
    </>
  );
};

const TaxSections = ({ areaType }: { areaType: AreaType }) => {
  const { cells } = useDensityCells(areaType);
  const resources = useMapValue(taxation.areaResources$, areaType.index);
  const sections = sectionsForArea(areaType.index, areaType.locked, { density: cells.length, vanilla: resources?.length ?? 0 });
  if (sections.length === 0) return null;
  return (
    <div className={styles.sections}>
      {sections.map((s) => (
        <Section key={s.id} areaType={areaType} section={s} />
      ))}
    </div>
  );
};

const Section = ({ areaType, section }: { areaType: AreaType; section: TaxSection }) => {
  const { translate } = useLocalization();
  const [open, setOpen] = useState(() => sectionExpandState.isOpen(areaType.id, section.id));
  const toggle = useCallback(() => {
    playSound(SOUNDS.expandRows);
    setOpen(sectionExpandState.toggle(areaType.id, section.id));
  }, [areaType.id, section.id]);
  const { Rows, Summary } = TAX_SECTION_ROWS[section.id];
  return (
    <div className={styles.section}>
      <div className={styles.header} onClick={toggle}>
        <img className={styles.arrow} src={arrowFor(open)} />
        <div className={styles.title}>{localize(translate, section.title)}</div>
        {Summary && (
          <div className={styles.summary}>
            <Summary areaType={areaType} />
          </div>
        )}
      </div>
      {open && <Rows areaType={areaType} />}
    </div>
  );
};
