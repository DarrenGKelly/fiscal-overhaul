import { UiKey } from "../l10n";
// The collapsible sections under each zone's main tax slider on the Taxation tab, in the order they are drawn. Data
// only: tax-sections-view.tsx draws each section's rows (TAX_SECTION_ROWS, keyed by the same id). A new tax type is one
// more entry here, one more renderer there, and its title in l10n/en-US.json.

/** A section's id: also its key in the expand state and in tax-sections-view.tsx's renderers. */
export type TaxSectionId = "density" | "vanilla";

/**
 * "main": the zone's income tax itself (vanilla's education/resource rows); "additional": a tax charged on top of it
 * (density, and any later tax type). The main section is drawn first, then the additional ones.
 */
export type TaxSectionKind = "main" | "additional";


export interface TaxSectionDef {
  id: TaxSectionId;
  kind: TaxSectionKind;
  /** The header's text for a tax area (vanilla TaxAreaType index: Residential = 1, Commercial = 2, ...). */
  title: (areaIndex: number) => UiKey;

  /** Open until the player collapses it (default: collapsed until opened). */
  startsOpen?: boolean;
}

/**
 * Every section type. Drawn main first, then the additional taxes in this order (orderedSections): a new tax type is
 * one more "additional" entry. A district view shows the same sections as Entire City, with the district's own values
 * in them (district-tax-view.tsx).
 */
export const TAX_SECTIONS: readonly TaxSectionDef[] = [
  // Vanilla's own sub-rows (TaxationItem): education levels for Residential, resources for the other zones.
  { id: "vanilla", kind: "main", title: (areaIndex) => (areaIndex === 1 ? "SectionEducation" : "SectionResources") },
  { id: "density", kind: "additional", title: () => "SectionDensity" },
];

/** The draw order: every main section (in list order), then every additional one (in list order). */
export function orderedSections(sections: readonly TaxSectionDef[]): TaxSectionDef[] {
  return [...sections.filter((s) => s.kind === "main"), ...sections.filter((s) => s.kind !== "main")];
}

export interface TaxSection {
  id: TaxSectionId;
  title: UiKey;
  startsOpen: boolean;
}

/**
 * The sections a zone group shows: each type that has rows there (rowCounts[id] > 0), in orderedSections order. None for
 * a locked zone (vanilla doesn't expand one either).
 */
export function sectionsForArea(
  areaIndex: number,
  locked: boolean,
  rowCounts: Partial<Record<TaxSectionId, number>>,
  sections: readonly TaxSectionDef[] = TAX_SECTIONS,
): TaxSection[] {
  if (locked) return [];
  return orderedSections(sections)
    .filter((s) => (rowCounts[s.id] ?? 0) > 0)
    .map((s) => ({ id: s.id, title: s.title(areaIndex), startsOpen: s.startsOpen ?? false }));
}

// Expand state. A section starts collapsed (or open: startsOpen); one the player flips stays flipped while the UI
// lives (switching tab or closing the Economy panel unmounts the groups, so React state alone would forget it).

export class SectionExpandState {
  /** The sections flipped away from how they start. */
  private readonly flipped = new Set<string>();

  private static key(areaId: string, section: TaxSectionId): string {
    return `${areaId}/${section}`;
  }

  isOpen(areaId: string, section: TaxSectionId, startsOpen = false): boolean {
    return this.flipped.has(SectionExpandState.key(areaId, section)) !== startsOpen;
  }

  /** Flips the section and returns its new state. */
  toggle(areaId: string, section: TaxSectionId, startsOpen = false): boolean {
    const key = SectionExpandState.key(areaId, section);
    if (!this.flipped.delete(key)) this.flipped.add(key);
    return this.isOpen(areaId, section, startsOpen);
  }
}

/** The one the Taxation tab uses. */
export const sectionExpandState = new SectionExpandState();

export interface GroupClasses {
  toggleButton?: string;
  hint?: string;
  content?: string;
}

/**
 * The vanilla TaxationGroup's class names with its own expander hidden while the sections replace it. The toggle
 * button and its gamepad "Expand Group" hint get `invisibleClass` (visibility: hidden, so the header slider stays where
 * vanilla puts it); the content (the vanilla rows, re-hosted in the "vanilla" section) gets `removedClass`
 * (display: none). Off: the vanilla names as they were.
 */
export function groupClasses(vanilla: GroupClasses, invisibleClass: string, removedClass: string, hide: boolean): GroupClasses {
  const add = (name: string | undefined, cls: string) => (hide && name ? `${name} ${cls}` : name);
  return {
    toggleButton: add(vanilla.toggleButton, invisibleClass),
    hint: add(vanilla.hint, invisibleClass),
    content: add(vanilla.content, removedClass),
  };
}
