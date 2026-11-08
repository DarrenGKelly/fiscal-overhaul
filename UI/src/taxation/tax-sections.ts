import { UiKey } from "../l10n";
// The collapsible sections under each zone's main tax slider on the Taxation tab, in the order they are drawn. Data
// only: tax-sections-view.tsx draws each section's rows (TAX_SECTION_ROWS, keyed by the same id). A new tax type is one
// more entry here, one more renderer there, and its title in l10n/en-US.json.

/** A section's id: also its key in the expand state and in tax-sections-view.tsx's renderers. */
export type TaxSectionId = "density" | "vanilla" | "district";

/** Which view of the tab a section belongs to: the Entire City view, or a district picked in the selector. */
export type TaxView = "city" | "district";

export interface TaxSectionDef {
  id: TaxSectionId;
  /** The header's text for a tax area (vanilla TaxAreaType index: Residential = 1, Commercial = 2, ...). */
  title: (areaIndex: number) => UiKey;
  /** The view it shows in. City sections set city-wide rates, so a district view leaves them out. */
  view: TaxView;
  /** Open until the player collapses it (default: collapsed until opened). */
  startsOpen?: boolean;
}

/** Every section type, top to bottom. Density is always first in the city view. */
export const TAX_SECTIONS: readonly TaxSectionDef[] = [
  { id: "density", title: () => "SectionDensity", view: "city" },
  // Vanilla's own sub-rows (TaxationItem): education levels for Residential, resources for the other zones.
  { id: "vanilla", title: (areaIndex) => (areaIndex === 1 ? "SectionEducation" : "SectionResources"), view: "city" },
  // The picked district's adjustment for the zone: one slider, -cap..+cap points on the city rate. Open by default:
  // it is all a district view shows.
  { id: "district", title: () => "SectionDistrict", view: "district", startsOpen: true },
];

export interface TaxSection {
  id: TaxSectionId;
  title: UiKey;
  startsOpen: boolean;
}

/**
 * The sections a zone group shows in a view: each of that view's types that has rows there (rowCounts[id] > 0), in
 * TAX_SECTIONS order. None for a locked zone (vanilla doesn't expand one either).
 */
export function sectionsForArea(
  areaIndex: number,
  locked: boolean,
  rowCounts: Partial<Record<TaxSectionId, number>>,
  sections: readonly TaxSectionDef[] = TAX_SECTIONS,
  view: TaxView = "city",
): TaxSection[] {
  if (locked) return [];
  return sections
    .filter((s) => s.view === view && (rowCounts[s.id] ?? 0) > 0)
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
