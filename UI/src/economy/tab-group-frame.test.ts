import { describe, expect, it } from "vitest";
import { sameBox, tabGroupFrame, tabHeight } from "./tab-group-frame";

// The room marker at x 100, y 50 (the row's middle); three 200 px tabs from x 430, 30 px tall, 10 px apart.
const room = { left: 100, top: 50, right: 100, bottom: 50 };
const tab = (left: number) => ({ left, top: 35, right: left + 200, bottom: 65 });
const tabs = [tab(430), tab(640), tab(850)];

describe("tabGroupFrame", () => {
  it("spans the picker and the district tabs, padded on every side", () => {
    expect(tabGroupFrame(room, tabs, 6)).toEqual({ left: -6, top: 35 - 50 - 6, width: 1050 - 100 + 12, height: 30 + 12 });
  });
  it("ends at the last district tab, not at a tab beyond the divider", () => {
    expect(tabGroupFrame(room, tabs.slice(0, 2), 6)?.width).toBe(840 - 100 + 12);
    expect(tabGroupFrame(room, tabs.slice(0, 2), 6)?.width).not.toBe(1050 - 100 + 12);
  });
  it("is null with no tabs or before layout", () => {
    expect(tabGroupFrame(room, [], 6)).toBeNull();
    expect(tabGroupFrame(room, [{ left: 0, top: 0, right: 0, bottom: 0 }], 6)).toBeNull();
  });
});

describe("tabHeight", () => {
  it("is the tabs' height", () => expect(tabHeight(tabs)).toBe(30));
  it("is null before layout", () => {
    expect(tabHeight([])).toBeNull();
    expect(tabHeight([{ left: 0, top: 10, right: 0, bottom: 10 }])).toBeNull();
  });
});

describe("sameBox", () => {
  const box = { left: 0, top: 0, width: 10, height: 10 };
  it("matches within half a pixel", () => expect(sameBox(box, { ...box, width: 10.3 })).toBe(true));
  it("differs beyond it, or against null", () => {
    expect(sameBox(box, { ...box, width: 11 })).toBe(false);
    expect(sameBox(box, null)).toBe(false);
    expect(sameBox(null, null)).toBe(true);
  });
});
