import { useMapValue, useValue } from "cs2/api";
import { taxation } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { FC, useCallback, useContext, useState } from "react";
import { densityTaxesEnabled$, playSound } from "bindings";
import { CITY_KEY } from "district/district-select";
import { localize } from "l10n";
import { SOUNDS } from "sounds";
import { TaxationItem, taxationGroupClasses } from "vanilla";
import { arrowFor, zoneForAreaIndex } from "./density";
import { DensityRows, useDensityCells } from "./density-rows";
import { useZoneDensityExtra, ZoneDensityExtra } from "./density-totals";
import { TaxDistrictView, TaxRowTarget } from "./district-tax-view";
import { TYPE_RESOURCE_RATE, TYPE_ZONE_RATE } from "./district-tax";
import { groupClasses, sectionExpandState, sectionsForArea, TaxSection, TaxSectionId } from "./tax-sections";
import styles from "./tax-sections.module.scss";

// The Taxation tab with density taxes on: under each zone's main slider, collapsible sections (tax-sections.ts):
// Density tax, then vanilla's own rows (education levels or resources), whose vanilla expander is hidden. A district
// picked in the selector (district-tax-view.tsx) shows the same sections (Density tax only while density taxes are on)
// with the district's own values. Density taxes off, in the Entire City view: the vanilla TaxationGroup exactly.

type AreaType = taxation.TaxAreaType;

/**
 * Vanilla's own sub-rows of a zone, as vanilla draws them inside its group. Each says which rate it is
 * (TaxRowTarget: the education level or resource), so its slider shows and sets the district's value in a district view.
 */
const VanillaRows = ({ areaType }: { areaType: AreaType }) => {
  const resources = useMapValue(taxation.areaResources$, areaType.index) ?? [];
  const zone = zoneForAreaIndex(areaType.index);
  return (
    <>
      {resources.map((r) => (
        <TaxRowTarget.Provider key={r.resource} value={{ type: TYPE_RESOURCE_RATE, zone, key: r.resource }}>
          <TaxationItem areaType={areaType} resource={r} />
        </TaxRowTarget.Provider>
      ))}
    </>
  );
};

/** What each section draws under its header (the header itself is the arrow and the title, no summary). */
const TAX_SECTION_ROWS: Record<TaxSectionId, { Rows: FC<{ areaType: AreaType }> }> = {
  density: { Rows: DensityRows },
  vanilla: { Rows: VanillaRows },

};

// TaxationGroup's class names as the game ships them, before any wrapper has changed the live object.
const VANILLA_GROUP_CLASSES = { ...taxationGroupClasses };

/** Hides (on) or restores (off) vanilla TaxationGroup's own expander, which reads these names on every render. */
function hideVanillaExpander(hide: boolean) {
  if (taxationGroupClasses) Object.assign(taxationGroupClasses, groupClasses(VANILLA_GROUP_CLASSES, styles.invisible, styles.removed, hide));
}

/**
 * The TaxationGroup extension (index.tsx): the zone total with its density extra (its header slider is the zone rate:
 * TaxRowTarget), then the sections while density taxes are on or a district is picked.
 */
export const withTaxSections = (TaxationGroup: any) => (props: any) => {
  const enabled = useValue(densityTaxesEnabled$);
  const district = useContext(TaxDistrictView) !== CITY_KEY;
  const extra = useZoneDensityExtra(props.areaType);
  const sectionsOn = enabled || district;
  hideVanillaExpander(sectionsOn);
  const zone = zoneForAreaIndex(props.areaType?.index ?? 0);
  return (
    <>
      <ZoneDensityExtra.Provider value={extra}>
        <TaxRowTarget.Provider value={zone >= 0 ? { type: TYPE_ZONE_RATE, zone, key: 0 } : null}>
          <TaxationGroup {...props} />
        </TaxRowTarget.Provider>
      </ZoneDensityExtra.Provider>
      {sectionsOn && <TaxSections areaType={props.areaType} />}
    </>
  );
};

const TaxSections = ({ areaType }: { areaType: AreaType }) => {
  const { cells } = useDensityCells(areaType);
  const resources = useMapValue(taxation.areaResources$, areaType.index);
  const counts = { density: cells.length, vanilla: resources?.length ?? 0 };
  const sections = sectionsForArea(areaType.index, areaType.locked, counts);
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
  const [open, setOpen] = useState(() => sectionExpandState.isOpen(areaType.id, section.id, section.startsOpen));
  const toggle = useCallback(() => {
    playSound(SOUNDS.expandRows);
    setOpen(sectionExpandState.toggle(areaType.id, section.id, section.startsOpen));
  }, [areaType.id, section.id, section.startsOpen]);
  const { Rows } = TAX_SECTION_ROWS[section.id];
  return (
    <div className={styles.section}>
      <div className={styles.header} onClick={toggle}>
        <img className={styles.arrow} src={arrowFor(open)} />
        <div className={styles.title}>{localize(translate, section.title)}</div>
      </div>
      {open && <Rows areaType={areaType} />}
    </div>
  );
};
