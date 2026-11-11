// The Taxation tab's warning line: why taxes are the game's own. The C# side
// publishes TaxEngineWarning as an int (taxEngineWarning$); this maps it to the line's text. No cs2 imports, so vitest
// runs it without the game.
import { localize, Translate, UiKey } from "../l10n";

/** C# TaxEngineWarning values. */
export const TaxEngineWarning = {
  None: 0,
  // 1 and 2 (an untested game version, not forced / forced on) are no longer sent: the Options page's notice says it.
  Incompatible: 3,
} as const;

const KEYS: Record<number, UiKey> = {
  [TaxEngineWarning.Incompatible]: "TaxEngineIncompatible",
};

/** The warning's text key, or null for none (0, or a value this build doesn't know). */
export function engineWarningKey(warning: number | null | undefined): UiKey | null {
  return warning != null && warning in KEYS ? KEYS[warning] : null;
}

/** The warning line's text in the game's language, or null when there is no warning. */
export function engineWarningText(warning: number | null | undefined, translate: Translate | null): string | null {
  const key = engineWarningKey(warning);
  return key ? localize(translate, key) : null;
}
