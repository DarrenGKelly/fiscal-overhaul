import { UiKey } from "../l10n";
// The collapsible sections under each zone's main tax slider on the Taxation tab, in the order they are drawn. Data
// only: tax-sections-view.tsx draws each section's rows (TAX_SECTION_ROWS, keyed by the same id). A new tax type is one
// more entry here, one more renderer there, and its title in l10n/en-US.json.

/** A section's id: also its key in the expand state and in tax-sections-view.tsx's renderers. */
export type TaxSectionId = "density" | "vanilla";

export interface TaxSectionDef {
  id: TaxSectionId;
  /** The header's text for a tax area (vanilla TaxAreaType index: Residential = 1, Commercial = 2, ...). */
  title: (areaIndex: number) => UiKey;
}

/** Every section type, top to bottom. Density is always first. */
export const TAX_SECTIONS: readonly TaxSectionDef[] = [
  { id: "density", title: () => "SectionDensity" },
  // Vanilla's own sub-rows (TaxationItem): education levels for Residential, resources for the other zones.
  { id: "vanilla", title: (areaIndex) => (areaIndex === 1 ? "SectionEducation" : "SectionResources") },
];

export interface TaxSection {
  id: TaxSectionId;
  title: UiKey;
}

/**
 * The sections a zone group shows: each type that has rows there (rowCounts[id] > 0), in TAX_SECTIONS order. None for
 * a locked zone (vanilla doesn't expand one either).
 */
export function sectionsForArea(
  areaIndex: number,
  locked: boolean,
  rowCounts: Partial<Record<TaxSectionId, number>>,
  sections: readonly TaxSectionDef[] = TAX_SECTIONS,
): TaxSection[] {
  if (locked) return [];
  return sections.filter((s) => (rowCounts[s.id] ?? 0) > 0).map((s) => ({ id: s.id, title: s.title(areaIndex) }));
}

// Expand state. Every section starts collapsed; a section the player opens stays open while the UI lives (switching
// tab or closing the Economy panel unmounts the groups, so React state alone would forget it).

export class SectionExpandState {
  private readonly open = new Set<string>();

  private static key(areaId: string, section: TaxSectionId): string {
    return `${areaId}/${section}`;
  }

  isOpen(areaId: string, section: TaxSectionId): boolean {
    return this.open.has(SectionExpandState.key(areaId, section));
  }

  /** Flips the section and returns its new state. */
  toggle(areaId: string, section: TaxSectionId): boolean {
    const key = SectionExpandState.key(areaId, section);
    if (this.open.delete(key)) return false;
    this.open.add(key);
    return true;
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
