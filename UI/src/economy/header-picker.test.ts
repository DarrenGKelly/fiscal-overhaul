import { describe, expect, it } from "vitest";
import { CITY_KEY, OUTSIDE_KEY } from "../district/district-select";
import { orderEconomyTabs } from "./tab-order";
import { DistrictFeatures, economyTabBarChildren, headerPicker, isDistrictTab, reservesPickerSlot, TAB, withTabGroupDivider } from "./header-picker";

const FEATURES = ["City Budget", "Loan", "Taxation", "Service Budgets", "Production Panel"];
const tab = (id: number) => ({ props: { tab: id, feature: FEATURES[id] } });
const DIVIDER = { props: { divider: true } };
const ids = (xs: any[]) => xs.map((x) => (x.props.divider ? "|" : x.props.tab));

const ALL_ON: DistrictFeatures = { budgetOn: true, servicesOn: true, taxEngineOn: true };
const ALL_OFF: DistrictFeatures = { budgetOn: false, servicesOn: false, taxEngineOn: false };
const DISTRICTS = [
  { key: "12:1", name: "Downtown" },
  { key: "40:2", name: "Harbour" },
];

describe("withTabGroupDivider", () => {
  it("puts the divider between Services and Production, by tab id", () => {
    const ordered = orderEconomyTabs([0, 1, 2, 3, 4].map(tab));
    expect(ids(withTabGroupDivider(ordered, DIVIDER))).toEqual([0, 2, 3, "|", 4, 1]);
  });

  it("still groups a Loan-less set", () => {
    const ordered = orderEconomyTabs([0, 2, 3, 4].map(tab));
    expect(ids(withTabGroupDivider(ordered, DIVIDER))).toEqual([0, 2, 3, "|", 4]);
  });

  it("follows the ids, not the positions: Loan right after Services gets the divider", () => {
    expect(ids(withTabGroupDivider([0, 2, 3, 1].map(tab), DIVIDER))).toEqual([0, 2, 3, "|", 1]);
  });

  it("adds nothing without both groups", () => {
    const districtOnly = [0, 2, 3].map(tab);
    expect(withTabGroupDivider(districtOnly, DIVIDER)).toBe(districtOnly);
    const cityOnly = [4, 1].map(tab);
    expect(withTabGroupDivider(cityOnly, DIVIDER)).toBe(cityOnly);
  });

  it("tells district tabs from city-only tabs", () => {
    expect([TAB.Budget, TAB.Taxation, TAB.Services].every(isDistrictTab)).toBe(true);
    expect([TAB.Production, TAB.Loan].some(isDistrictTab)).toBe(false);
  });
});

describe("headerPicker visibility per tab", () => {
  it("shows on Budget, Taxation and Services", () => {
    for (const t of [TAB.Budget, TAB.Taxation, TAB.Services]) expect(headerPicker(t, ALL_ON, DISTRICTS, CITY_KEY)).not.toBeNull();
  });

  it("hides on Production and Loans", () => {
    for (const t of [TAB.Production, TAB.Loan]) expect(headerPicker(t, ALL_ON, DISTRICTS, CITY_KEY)).toBeNull();
  });

  it("hides on a tab whose district feature is off", () => {
    expect(headerPicker(TAB.Budget, { ...ALL_ON, budgetOn: false }, DISTRICTS, CITY_KEY)).toBeNull();
    expect(headerPicker(TAB.Services, { ...ALL_ON, servicesOn: false }, DISTRICTS, CITY_KEY)).toBeNull();
    expect(headerPicker(TAB.Taxation, { ...ALL_ON, taxEngineOn: false }, DISTRICTS, CITY_KEY)).toBeNull();
  });

  it("keeps each tab's own rule for a city with no district: Budget shows, Taxation hides", () => {
    expect(headerPicker(TAB.Budget, ALL_ON, [], CITY_KEY)).not.toBeNull();
    expect(headerPicker(TAB.Taxation, ALL_ON, [], CITY_KEY)).toBeNull();
  });
});

describe("headerPicker choices per tab", () => {
  it("offers Outside Districts on Budget, Services and Taxation", () => {
    for (const t of [TAB.Budget, TAB.Services, TAB.Taxation])
      expect(headerPicker(t, ALL_ON, DISTRICTS, CITY_KEY)!.options.map((o) => o.key)).toContain(OUTSIDE_KEY);
  });

  it("shows Outside as Outside on every district tab, but not on Taxation with the engine off", () => {
    expect(headerPicker(TAB.Taxation, ALL_ON, DISTRICTS, OUTSIDE_KEY)!.selected).toBe(OUTSIDE_KEY);
    expect(headerPicker(TAB.Budget, ALL_ON, DISTRICTS, OUTSIDE_KEY)!.selected).toBe(OUTSIDE_KEY);
    expect(headerPicker(TAB.Taxation, { ...ALL_ON, taxEngineOn: false }, DISTRICTS, OUTSIDE_KEY)).toBeNull();
  });

  it("shows a district on every district tab", () => {
    for (const t of [TAB.Budget, TAB.Taxation, TAB.Services]) expect(headerPicker(t, ALL_ON, DISTRICTS, "40:2")!.selected).toBe("40:2");
  });

  it("labels the city option with the city's name", () => {
    expect(headerPicker(TAB.Budget, ALL_ON, DISTRICTS, CITY_KEY, null, "Odiham")!.options[0]).toEqual({ key: CITY_KEY, label: "Odiham" });
    expect(headerPicker(TAB.Taxation, ALL_ON, DISTRICTS, CITY_KEY, null, "Odiham")!.options[0].label).toBe("Odiham");
  });
});

describe("reservesPickerSlot", () => {
  it("reserves the place while any district feature is on", () => {
    expect(reservesPickerSlot(ALL_ON)).toBe(true);
    expect(reservesPickerSlot({ ...ALL_OFF, servicesOn: true })).toBe(true);
  });

  it("reserves nothing with every feature off", () => {
    expect(reservesPickerSlot(ALL_OFF)).toBe(false);
  });
});

describe("economyTabBarChildren", () => {
  const PICKER = { props: { picker: true } };
  const vanilla = [0, 1, 2, 3, 4].map(tab);

  it("puts the picker first, then every vanilla tab element once, unchanged, in our order with the divider", () => {
    const out = economyTabBarChildren(vanilla, PICKER, DIVIDER);
    expect(out[0]).toBe(PICKER);
    expect(ids(out.slice(1))).toEqual([0, 2, 3, "|", 4, 1]);
    // The same element objects vanilla passed, none dropped, copied or duplicated.
    for (const t of vanilla) expect(out.filter((x) => x === t)).toHaveLength(1);
    expect(out).toHaveLength(vanilla.length + 2);
  });

  it("has no picker element when the slot isn't reserved", () => {
    const out = economyTabBarChildren(vanilla, null, DIVIDER);
    expect(out).not.toContain(PICKER);
    expect(ids(out)).toEqual([0, 2, 3, "|", 4, 1]);
  });
});