import { beforeEach, describe, expect, it } from "vitest";
import { CITY_KEY, districtOptions, effectiveKey } from "./district-select";
import { getSelectedDistrict, setSelectedDistrict, subscribeSelectedDistrict } from "./selection";

beforeEach(() => setSelectedDistrict(CITY_KEY));

describe("selected district", () => {
  it("starts as Entire City", () => {
    expect(getSelectedDistrict()).toBe(CITY_KEY);
  });

  it("a set reaches every subscriber and the getter", () => {
    const a: string[] = [];
    const b: string[] = [];
    const offA = subscribeSelectedDistrict((k) => a.push(k));
    const offB = subscribeSelectedDistrict((k) => b.push(k));
    setSelectedDistrict("12:1");
    expect(a).toEqual(["12:1"]);
    expect(b).toEqual(["12:1"]);
    expect(getSelectedDistrict()).toBe("12:1");
    offA();
    offB();
  });

  it("an unsubscribed listener hears nothing, and an unchanged set notifies no one", () => {
    const seen: string[] = [];
    const off = subscribeSelectedDistrict((k) => seen.push(k));
    setSelectedDistrict("outside");
    setSelectedDistrict("outside");
    expect(seen).toEqual(["outside"]);
    off();
    setSelectedDistrict("7:1");
    expect(seen).toEqual(["outside"]);
  });

  it("a key a tab doesn't offer shows Entire City there but stays shared", () => {
    setSelectedDistrict("99:1");
    const options = districtOptions([{ key: "12:1", name: "Downtown" }]);
    expect(effectiveKey(options, getSelectedDistrict())).toBe(CITY_KEY);
    expect(getSelectedDistrict()).toBe("99:1");
  });
});
