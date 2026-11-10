import { describe, expect, it } from "vitest";
import { toggled } from "./budget-rows";
import { groupState, toggledGroup } from "./group-toggle";

const GROUP = "expense:Upkeep";
const KIDS = ["expense:Upkeep:a", "expense:Upkeep:b"];
const ALL = [GROUP, ...KIDS];
const sorted = (s: ReadonlySet<string>) => [...s].sort();

describe("toggledGroup", () => {
  it("off -> one click turns the group and every child on", () => {
    const next = toggledGroup(new Set(["income:total"]), GROUP, KIDS);
    expect(groupState(next, GROUP, KIDS)).toBe("on");
    expect(sorted(next)).toEqual([...ALL, "income:total"].sort());
  });

  it("off -> a click does not turn on only the group's own line", () => {
    const next = toggledGroup(new Set(), GROUP, KIDS);
    expect(groupState(next, GROUP, KIDS)).not.toBe("partial");
    for (const k of KIDS) expect(next.has(k)).toBe(true);
  });

  it("all on -> one click turns the group and every child off, other rows kept", () => {
    const next = toggledGroup(new Set([...ALL, "income:total"]), GROUP, KIDS);
    expect(groupState(next, GROUP, KIDS)).toBe("off");
    expect(sorted(next)).toEqual(["income:total"]);
  });

  it("all on -> a click does not leave any child on", () => {
    const next = toggledGroup(new Set(ALL), GROUP, KIDS);
    for (const k of ALL) expect(next.has(k)).toBe(false);
  });

  it("partial -> one click turns everything on", () => {
    const start = new Set([KIDS[0]]);
    expect(groupState(start, GROUP, KIDS)).toBe("partial");
    expect(sorted(toggledGroup(start, GROUP, KIDS))).toEqual([...ALL].sort());
  });

  it("partial (group line on, a child off) -> a click does not turn things off", () => {
    const start = new Set([GROUP, KIDS[0]]);
    const next = toggledGroup(start, GROUP, KIDS);
    expect(groupState(next, GROUP, KIDS)).not.toBe("off");
    expect(next.size).toBe(ALL.length);
  });

  it("a row without children is a plain toggle", () => {
    expect(sorted(toggledGroup(new Set(), "income:total"))).toEqual(["income:total"]);
    expect(sorted(toggledGroup(new Set(["income:total"]), "income:total"))).toEqual([]);
  });

  it("leaves the old set untouched", () => {
    const start = new Set<string>();
    toggledGroup(start, GROUP, KIDS);
    expect(start.size).toBe(0);
  });
});

describe("groupState", () => {
  it("a child toggled off from all on makes the group partial", () => {
    expect(groupState(toggled(new Set(ALL), KIDS[1]), GROUP, KIDS)).toBe("partial");
  });

  it("a child toggled on from off makes the group partial, not on", () => {
    const next = toggled(new Set(), KIDS[0]);
    expect(groupState(next, GROUP, KIDS)).toBe("partial");
    expect(groupState(next, GROUP, KIDS)).not.toBe("on");
  });

  it("the last child toggled back on from partial makes the group on again", () => {
    const next = toggled(new Set([GROUP, KIDS[0]]), KIDS[1]);
    expect(groupState(next, GROUP, KIDS)).toBe("on");
  });

  it("a child toggle never touches the other keys", () => {
    expect(sorted(toggled(new Set(ALL), KIDS[0]))).toEqual([GROUP, KIDS[1]].sort());
  });

  it("without children only the row's own key counts", () => {
    expect(groupState(new Set(["x"]), "x")).toBe("on");
    expect(groupState(new Set(["y"]), "x")).toBe("off");
  });
});
