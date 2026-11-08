import { readdirSync, readFileSync, statSync } from "fs";
import { join } from "path";
import { describe, expect, it } from "vitest";
import EN_US from "../../l10n/en-US.json";
import { english, fill, localize, localizeVanilla, Translate, UI_PREFIX } from "./l10n";

/** A stand-in for the game's translate: the dictionary's text, else the fallback. */
const fakeTranslate =
  (dict: Record<string, string>): Translate =>
  (id, fallback) =>
    dict[id] ?? fallback ?? null;

describe("localize", () => {
  it("returns the game's translation when it has the key", () => {
    const t = fakeTranslate({ "FiscalOverhaul.UI.SectionDensity": "Dichte" });
    expect(localize(t, "SectionDensity")).toBe("Dichte");
    expect(localize(t, "SectionDensity")).not.toBe("Density");
  });

  it("falls back to the English text when the game has no translation", () => {
    expect(localize(fakeTranslate({}), "SectionDensity")).toBe("Density");
    // The game's translate returns null when it has neither the key nor a fallback; an empty string isn't a translation.
    expect(localize(() => null, "SectionDensity")).toBe("Density");
    expect(localize(() => "", "SectionDensity")).toBe("Density");
  });

  it("gives English without a translate (tests, no game)", () => {
    expect(localize(null, "ChartEmpty")).toBe("Tick a row on the left to plot it.");
  });

  it("asks the game for the prefixed key, with the English as its fallback", () => {
    const asked: [string, string | null | undefined][] = [];
    localize((id, fallback) => (asked.push([id, fallback]), null), "SpanAll");
    expect(asked).toEqual([["FiscalOverhaul.UI.SpanAll", "All"]]);
  });

  it("fills placeholders in the translation and in the fallback", () => {
    expect(localize(fakeTranslate({ "FiscalOverhaul.UI.OffsetsSet": "{COUNT} Anpassungen" }), "OffsetsSet", { COUNT: 5 })).toBe("5 Anpassungen");
    expect(localize(null, "OffsetsSet", { COUNT: 5 })).toBe("5 offsets set");
  });
});

describe("fill", () => {
  it("replaces each named placeholder, every time it appears", () => {
    expect(fill("-{MONTHS} mo {WEEKS} wk", { MONTHS: 1, WEEKS: 2 })).toBe("-1 mo 2 wk");
    expect(fill("{A}{A}", { A: "x" })).toBe("xx");
  });

  it("leaves a placeholder with no value as it is", () => {
    expect(fill("{COUNT} payers", {})).toBe("{COUNT} payers");
    expect(fill("{COUNT} payers")).toBe("{COUNT} payers");
  });
});

describe("localizeVanilla", () => {
  it("reuses the game's own key", () => {
    expect(localizeVanilla(fakeTranslate({ "StatisticsPanel.TIME_SCALE_NOW": "Agora" }), "now")).toBe("Agora");
  });

  it("falls back to English when the game lacks it", () => {
    expect(localizeVanilla(fakeTranslate({}), "revenues")).toBe("Revenues");
    expect(localizeVanilla(null, "now")).toBe("Now");
  });
});

describe("en-US.json", () => {
  const keys = Object.keys(EN_US);

  it("holds only the mod's own keys: UI strings and Options", () => {
    const bad = keys.filter((k) => !k.startsWith(UI_PREFIX) && !k.startsWith("FiscalOverhaul.Options."));
    expect(bad).toEqual([]);
  });

  it("has English text for every UI key", () => {
    expect(english("SectionDensity")).toBe("Density");
    for (const k of keys) expect((EN_US as Record<string, string>)[k].trim()).not.toBe("");
  });
});

describe("UI source", () => {
  // Done when no player-facing English literal is left in UI/src: JSX text must come from localize().
  const files: string[] = [];
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith(".tsx")) files.push(path);
    }
  };
  walk(__dirname);

  /** Text between a tag's ">" and the next "<" that contains a word (code between them has braces, "=", ";" or an operator). */
  const jsxText = (src: string) => [...src.matchAll(/>([^<>{}=;()&|]*[A-Za-z]{2,}[^<>{}=;()&|]*)</g)].map((m) => m[1].trim());

  it("finds literal text in JSX, and not localized text", () => {
    expect(jsxText(`<div className={styles.title}>By density</div>`)).toEqual(["Density"]);
    expect(jsxText(`<div className={styles.title}>{localize(translate, "SectionDensity")}</div>`)).toEqual([]);
  });

  it("has no hard-coded English text in JSX", () => {
    const offenders = files.flatMap((f) => jsxText(readFileSync(f, "utf8")).map((t) => `${f}: ${t}`));
    expect(files.length).toBeGreaterThan(0);
    expect(offenders).toEqual([]);
  });
});
