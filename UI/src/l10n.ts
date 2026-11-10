// The mod's player-facing text: every string is a locale key, read through the game's translate() with the English
// text as fallback. The English dictionary is l10n/en-US.json at the repo root, the same file the C# side registers
// with the game (Mod/LocaleRegistry.cs), so the two can't drift. No cs2 imports: pure modules take a Translate and
// vitest runs without the game. How to add a language: knowledge/cs2-ui-modding.md § Localization.
import EN_US from "../../l10n/en-US.json";

/** The game's translate (cs2/l10n useLocalization().translate): the text for `id`, or `fallback` when it has none. */
export type Translate = (id: string, fallback?: string | null) => string | null;

/** Every key the UI reads starts with this; the rest of the key is a {@link UiKey}. */
export const UI_PREFIX = "FiscalOverhaul.UI.";

type StripPrefix<K> = K extends `FiscalOverhaul.UI.${infer N}` ? N : never;

/** A UI string's name: its key in l10n/en-US.json without {@link UI_PREFIX}. A typo is a compile error. */
export type UiKey = StripPrefix<keyof typeof EN_US>;

/** Placeholder values, e.g. { COUNT: 3 } for "{COUNT} offsets set". */
export type Args = Record<string, string | number>;

const ENGLISH: Record<string, string> = EN_US;

/** The English text for a UI string (the fallback when the game has no translation). */
export function english(key: UiKey): string {
  return ENGLISH[UI_PREFIX + key];
}

/** Replaces each {NAME} in `text` with args.NAME; a placeholder with no value is left as it is. */
export function fill(text: string, args?: Args): string {
  if (!args) return text;
  return text.replace(/\{([A-Z_]+)\}/g, (whole, name: string) => (name in args ? String(args[name]) : whole));
}

/**
 * A UI string in the game's language: the translation of FiscalOverhaul.UI.<key> when the game has one, else the
 * English text, with its placeholders filled. `translate` null (tests, or no game) gives English.
 */
export function localize(translate: Translate | null, key: UiKey, args?: Args): string {
  const fallback = english(key);
  const text = (translate ? translate(UI_PREFIX + key, fallback) : null) || fallback;
  return fill(text, args);
}

/**
 * Strings the game already translates: reused through its own keys, so they read as vanilla does in every language.
 * The English is only the fallback for a game that lacks the key.
 */
export const VANILLA = {
  revenues: ["EconomyPanel.INCOME_SECTION_TITLE", "Revenues"],
  expenses: ["EconomyPanel.EXPENSES_SECTION_TITLE", "Expenses"],
  monthlyBalance: ["EconomyPanel.MONTHLY_BALANCE", "Monthly balance"],
  now: ["StatisticsPanel.TIME_SCALE_NOW", "Now"],
  serviceBudget: ["EconomyPanel.SERVICES_TITLE_BUDGET", "Budget"],
  serviceExpenses: ["EconomyPanel.SERVICES_TITLE_EXPENSES", "Expenses"],
  serviceUpkeep: ["EconomyPanel.SERVICES_LABEL_UPKEEP", "Upkeep"],
} as const;

/** A vanilla string ({@link VANILLA}) in the game's language, or its English fallback. */
export function localizeVanilla(translate: Translate | null, name: keyof typeof VANILLA): string {
  const [id, fallback] = VANILLA[name];
  return (translate ? translate(id, fallback) : null) || fallback;
}
