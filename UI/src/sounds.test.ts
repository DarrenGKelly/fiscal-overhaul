import { describe, expect, it } from "vitest";
import { SOUNDS, soundForPick } from "./sounds";

// Vanilla's UISound values (UI/types/ui.d.ts, cs2/ui UISound) that the panels use.
const VANILLA_UI_SOUNDS = new Set([
  "select-item",
  "select-toggle",
  "select-dropdown",
  "select-previous-item",
  "select-next-item",
  "expand-panel",
]);

describe("SOUNDS", () => {
  it("every sound is a vanilla UISound name", () => {
    for (const sound of Object.values(SOUNDS)) expect(VANILLA_UI_SOUNDS.has(sound)).toBe(true);
  });

  it("a made-up name is not one", () => {
    expect(VANILLA_UI_SOUNDS.has("click")).toBe(false);
  });
});

describe("soundForPick", () => {
  it("plays when the click picks a different choice", () => {
    expect(soundForPick(3, 6, SOUNDS.pickSpan)).toBe("select-item");
    expect(soundForPick("", "12:1", SOUNDS.pickFromList)).toBe("select-item");
  });

  it("is silent when the choice is already selected", () => {
    expect(soundForPick(3, 3, SOUNDS.pickSpan)).toBeNull();
  });
});
