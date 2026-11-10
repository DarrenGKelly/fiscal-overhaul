import { describe, expect, it } from "vitest";
import { FAST_MEASURE_FRAMES, hasDrawableSize, LoadTimer, nextMeasure } from "./load-timing";

const none = { w: 0, h: 0, rem: 1 };
const laidOut = { w: 1200, h: 600, rem: 0.67 };

describe("hasDrawableSize", () => {
  it("is true once both sides are laid out", () => {
    expect(hasDrawableSize(laidOut)).toBe(true);
  });

  it("is false for a just-mounted chart Gameface hasn't laid out yet", () => {
    expect(hasDrawableSize(none)).toBe(false);
    expect(hasDrawableSize({ w: 1200, h: 0, rem: 1 })).toBe(false);
  });
});

describe("nextMeasure", () => {
  // Regression: the chart retried a 0-wide first measure only on its 500 ms poll, so it drew up to a second late.
  it("measures the very next frame after mount, before any size is known", () => {
    expect(nextMeasure(1, none)).toBe("frame");
  });

  it("keeps measuring every frame while the panel settles, even with a size", () => {
    expect(nextMeasure(FAST_MEASURE_FRAMES - 1, laidOut)).toBe("frame");
  });

  it("falls back to the slow poll after the fast frames once it has a size", () => {
    expect(nextMeasure(FAST_MEASURE_FRAMES, laidOut)).toBe("poll");
  });

  it("keeps measuring every frame past the fast frames while it still has no size, up to a cap", () => {
    expect(nextMeasure(FAST_MEASURE_FRAMES + 10, none)).toBe("frame");
    expect(nextMeasure(4 * FAST_MEASURE_FRAMES, none)).toBe("poll");
  });
});

describe("LoadTimer", () => {
  it("records each milestone once, relative to the start", () => {
    const timer = new LoadTimer(1000);
    expect(timer.mark("data", 1003)).toBe(true);
    expect(timer.mark("data", 1500)).toBe(false);
    expect(timer.elapsed("data")).toBe(3);
  });

  it("has no time for a milestone that didn't happen", () => {
    const timer = new LoadTimer(1000);
    expect(timer.has("draw")).toBe(false);
    expect(timer.elapsed("draw")).toBeUndefined();
  });

  it("writes the milestones in order, rounded, with the extra text", () => {
    const timer = new LoadTimer(0);
    timer.mark("data", 2.4);
    timer.mark("size", 17.6);
    timer.mark("draw", 19);
    expect(timer.line("3 draws")).toBe("data +2 ms, size +18 ms, draw +19 ms; 3 draws");
    expect(timer.line()).toBe("data +2 ms, size +18 ms, draw +19 ms");
  });

  it("never reports a negative time", () => {
    const timer = new LoadTimer(50);
    timer.mark("data", 40);
    expect(timer.elapsed("data")).toBe(0);
  });
});
