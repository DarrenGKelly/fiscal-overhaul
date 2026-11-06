// The vanilla UI sounds the mod's panels play on a click that does something. No cs2 imports, so vitest runs it in
// Node; bindings.ts playSound sends one to the game. The names are vanilla's UISound values (cs2/ui UISound, played
// by vanilla through trigger("audio", "playSound", name, volume)).

export const SOUNDS = {
  /** A filter row toggling its chart line on or off (vanilla checkboxes). */
  toggleLine: "select-toggle",
  /** A span button (1M … All) picking a new span. */
  pickSpan: "select-item",
  /** The district selector's ‹ arrow. */
  previousDistrict: "select-previous-item",
  /** The district selector's › arrow. */
  nextDistrict: "select-next-item",
  /** Opening or closing the district list (vanilla dropdowns). */
  openList: "select-dropdown",
  /** Picking a choice from the district list. */
  pickFromList: "select-item",
  /** Expanding or collapsing the density tax rows on the Taxation tab. */
  expandRows: "expand-panel",
} as const;

export type SoundName = (typeof SOUNDS)[keyof typeof SOUNDS];

/** The sound for a click on a choice: none when it is already the selected one (the click changes nothing). */
export function soundForPick<T>(current: T, picked: T, sound: SoundName): SoundName | null {
  return current === picked ? null : sound;
}
