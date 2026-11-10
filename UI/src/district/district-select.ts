// Pure logic for the district selector shared by the Budget, Services and Taxation tabs. No game imports, so vitest runs it in Node.
import { localize, Translate } from "../l10n";

/** One district as the districts binding sends it (bindings.ts DistrictChoice). */
export interface DistrictLike {
  key: string;
  name: string;
}

/** A selector choice: its key (sent with setDistrict) and the text shown. */
export interface DistrictOption {
  key: string;
  label: string;
}

export const CITY_KEY = "";
export const OUTSIDE_KEY = "outside";

/**
 * A district's name as shown. NameSystem.GetRenderedLabelName gives a localization id (no spaces, dotted or
 * bracketed, e.g. "Assets.DISTRICT_NAME[3]") for a district the player hasn't named, or "": an id is translated, and
 * anything still blank or id-looking becomes "District <n>" (n = its 1-based place in the list).
 */
export function districtLabel(name: string, position: number, translate: Translate | null): string {
  let text = (name ?? "").trim();
  if (looksLikeId(text) && translate) text = (translate(text, null) ?? "").trim();
  if (text === "" || looksLikeId(text)) return localize(translate, "DistrictNumber", { NUMBER: position });
  return text;
}

function looksLikeId(text: string): boolean {
  return text !== "" && !/\s/.test(text) && /[.[\]]/.test(text) && /^[A-Za-z0-9_.:[\]-]+$/.test(text);
}

/** The city option's label: the city's name (vanilla toolbarBottom.cityName), or "Entire City" while it is blank. */
export function cityLabel(cityName: string | null | undefined, translate: Translate | null = null): string {
  const name = (cityName ?? "").trim();
  return name !== "" ? name : localize(translate, "EntireCity");
}

/**
 * Every selector choice in cycle order: the city (labelled with its name, cityLabel), each district (the binding's
 * order, by name), Outside Districts. No districts: only the city (its arrows and list staying on the city).
 */
export function districtOptions(districts: DistrictLike[], translate: Translate | null = null, cityName = ""): DistrictOption[] {
  const city: DistrictOption = { key: CITY_KEY, label: cityLabel(cityName, translate) };
  if (districts.length === 0) return [city];
  return [
    city,
    ...districts.map((d, i) => ({ key: d.key, label: districtLabel(d.name, i + 1, translate) })),
    { key: OUTSIDE_KEY, label: localize(translate, "OutsideDistricts") },
  ];
}

/**
 * Whether a tab shows the district selector: whenever district recording is on (the districtBudgetEnabled
 * binding), whatever the districts are, so a city without districts still shows Entire City and the view can be found.
 * Recording off is vanilla behaviour: no selector.
 */
export function showsDistrictSelector(recordingOn: boolean, options: { key: string }[]): boolean {
  return recordingOn && options.length > 0;
}

/**
 * Whether the list draws a divider line above option `index`: where the group changes, so Entire City, the districts
 * and Outside Districts read as three blocks. Never above the first option.
 */
export function dividerBefore(options: { key: string }[], index: number): boolean {
  if (index <= 0 || index >= options.length) return false;
  return optionGroup(options[index].key) !== optionGroup(options[index - 1].key);
}

function optionGroup(key: string): "city" | "district" | "outside" {
  return key === CITY_KEY ? "city" : key === OUTSIDE_KEY ? "outside" : "district";
}

/** The key one step forwards (1) or back (-1) from `currentKey`, wrapping both ways; an unknown key gives Entire City. */
export function cycle(options: { key: string }[], currentKey: string, dir: 1 | -1): string {
  const i = options.findIndex((o) => o.key === currentKey);
  if (i < 0 || options.length === 0) return CITY_KEY;
  return options[(i + dir + options.length) % options.length].key;
}

/** The selected key if it is still a choice, else Entire City (a district deleted, or recording switched off). */
export function effectiveKey(options: { key: string }[], key: string): string {
  return options.some((o) => o.key === key) ? key : CITY_KEY;
}

/** A box on screen: what getBoundingClientRect gives. */
export interface BoxLike {
  left: number;
  top: number;
  width: number;
  height: number;
}

/**
 * Where the picker's list goes inside the portal container (vanilla Portal): its top centre under the anchor (the
 * picker's name), in the container's coordinates. The list draws in the portal, not in the panel header, because the
 * header's stacking context sits under the panel content, which would cover the list.
 */
export function listAnchor(anchor: BoxLike, container: BoxLike | null): { left: number; top: number } {
  const ox = container?.left ?? 0;
  const oy = container?.top ?? 0;
  return { left: anchor.left + anchor.width / 2 - ox, top: anchor.top + anchor.height - oy };
}

/** What `Node.contains` needs: an element the picker owns. */
export interface ContainsLike {
  contains(node: unknown): boolean;
}

/**
 * Whether a mouse press closes the open list: anywhere but the picker's own name box and list (another tab, the
 * panel, the map). A press on the name toggles it through its own click; one on an entry picks it.
 */
export function closesOnPress(target: unknown, owned: (ContainsLike | null | undefined)[]): boolean {
  if (target == null) return true;
  return !owned.some((el) => el != null && el.contains(target));
}
