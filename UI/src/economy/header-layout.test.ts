// The header picker's sizes, read from its stylesheets (a regression: a 26rem name capped at 190rem clipped
// "Chester Square"). The row [picker | 5 tabs] must still fit the bar: ~360rem are left beside the tabs at 16:9
// (economy-tab-bar.module.scss).
import { readFileSync } from "fs";
import { join } from "path";
import { describe, expect, it } from "vitest";

const bar = readFileSync(join(__dirname, "economy-tab-bar.module.scss"), "utf8");
const selector = readFileSync(join(__dirname, "../district/district-selector.module.scss"), "utf8");

const rem = (source: string, name: string) => Number(new RegExp(`\\$${name}:\\s*([\\d.]+)rem`).exec(source)?.[1] ?? NaN);
const rule = (source: string, sel: string) => {
  const start = source.indexOf(`${sel} {`);
  return start < 0 ? "" : source.slice(start, source.indexOf("}", start));
};
const nameRule = rule(selector, ".compact .district-selector .district-name");

describe("header picker sizes", () => {
  it("the slot and its gap before Budget fit the room beside the tabs", () => {
    expect(rem(bar, "picker-width") + rem(bar, "picker-gap")).toBeLessThanOrEqual(360);
    expect(rem(bar, "picker-gap")).toBeGreaterThan(0);
  });
  it("the slot is wider than it was (310rem), for longer names", () => expect(rem(bar, "picker-width")).toBeGreaterThan(310));
  it("the name fills what the arrows leave, uncapped", () => {
    expect(nameRule).toMatch(/flex:\s*1 1 0/);
    expect(nameRule).toMatch(/max-width:\s*none/);
    expect(nameRule).not.toMatch(/max-width:\s*\d/);
  });
  it("the name is bold and between the tabs' labels and the old 26rem", () => {
    const size = Number(/font-size:\s*([\d.]+)rem/.exec(nameRule)?.[1]);
    expect(nameRule).toMatch(/font-weight:\s*bold/);
    expect(size).toBeLessThan(26);
    expect(size).toBeGreaterThanOrEqual(20);
  });
});
