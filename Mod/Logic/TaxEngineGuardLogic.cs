using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>What runs the tax charge.</summary>
    public enum TaxEngineMode
    {
        /// <summary>The "Simulation changes" switch is off: vanilla TaxSystem charges, as the player chose.</summary>
        Vanilla = 0,
        /// <summary>The mod's copy charges (vanilla TaxSystem's update is off).</summary>
        Engine = 1,
        /// <summary>The guard stopped the engine (untested game version, or the game's code isn't what the copy expects): vanilla charges.</summary>
        Blocked = 2,
    }

    /// <summary>Why the Taxation tab shows a warning, if it does. The UI reads it as an int.</summary>
    public enum TaxEngineWarning
    {
        None = 0,
        /// <summary>Game version not in the known list, not forced: vanilla taxes.</summary>
        UntestedVersion = 1,
        // 2 was "untested version, forced on": the player chose it in Options, so it shows no warning (removed by request).
        /// <summary>The structure checks failed (or another mod already replaced TaxSystem): vanilla taxes.</summary>
        Incompatible = 3,
    }

    /// <summary>
    /// The tax engine's game-version guard, as pure logic: the engine is a copy of vanilla TaxSystem's charge, so it runs
    /// only on a game version it was copied from (or when the player forces it), only while the game's code still has
    /// the shape the copy relies on, and only while the "Simulation changes" master switch is on.
    /// </summary>
    public static class TaxEngineGuardLogic
    {
        /// <summary>
        /// The game versions the engine's copy was taken from and checked against (Game.Version.current.shortVersion).
        /// Add a version only after comparing vanilla TaxSystem's charge (PayTaxJob, OnUpdate, OnGameLoaded) with the
        /// copy in TaxEngineSystem.
        /// </summary>
        public static readonly string[] KnownGameVersions = { "1.6.0f1", "1.6.3f1", "1.6.4f1" };

        /// <summary>
        /// Whether <paramref name="version"/> is a known one: its first word equals one exactly ("1.6.0f1" and
        /// "1.6.0f1 (419.d6c6)" match "1.6.0f1"; "1.6.0f10" and "1.6.1f1" don't). Null or blank is unknown.
        /// </summary>
        public static bool IsKnownVersion(string version, IEnumerable<string> known)
        {
            if (string.IsNullOrWhiteSpace(version) || known == null) return false;
            string word = version.Trim();
            int space = word.IndexOfAny(new[] { ' ', '\t' });
            if (space >= 0) word = word.Substring(0, space);
            foreach (string k in known)
                if (string.Equals(word, k, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Master switch off: Vanilla. Structure checks failed: Blocked, even when forced. Unknown version: Blocked unless
        /// <paramref name="forceUntested"/>. Otherwise Engine.
        /// </summary>
        public static TaxEngineMode Decide(bool versionKnown, bool structureOk, bool forceUntested, bool masterOn)
        {
            if (!masterOn) return TaxEngineMode.Vanilla;
            if (!structureOk) return TaxEngineMode.Blocked;
            if (!versionKnown && !forceUntested) return TaxEngineMode.Blocked;
            return TaxEngineMode.Engine;
        }

        public static TaxEngineMode Decide(string version, IEnumerable<string> knownVersions, bool structureOk, bool forceUntested, bool masterOn)
            => Decide(IsKnownVersion(version, knownVersions), structureOk, forceUntested, masterOn);

        /// <summary>
        /// The Taxation tab's warning: none while the master switch is off (vanilla by choice), on a known, compatible
        /// version, or on an untested one the player forced on in Options (their choice, so no warning).
        /// </summary>
        public static TaxEngineWarning WarningFor(bool versionKnown, bool structureOk, bool forceUntested, bool masterOn)
        {
            if (!masterOn) return TaxEngineWarning.None;
            if (!structureOk) return TaxEngineWarning.Incompatible;
            if (!versionKnown && !forceUntested) return TaxEngineWarning.UntestedVersion;
            return TaxEngineWarning.None;
        }

        /// <summary>
        /// Whether the mod's copy of CitizenHappinessSystem's update runs (vanilla's is then off): only while the tax
        /// engine charges (<paramref name="engineMode"/> is Engine, so the guard, the version and the master switch
        /// already agree), vanilla CitizenHappinessSystem still has the private fields the copy writes
        /// (<paramref name="happinessStructureOk"/>), and no one else (another mod) had already disabled vanilla's
        /// update (<paramref name="foreignDisabled"/>).
        /// </summary>
        public static bool RunsHappiness(TaxEngineMode engineMode, bool happinessStructureOk, bool foreignDisabled)
        {
            return engineMode == TaxEngineMode.Engine && happinessStructureOk && !foreignDisabled;
        }

        /// <summary>
        /// Whether the mod's copy of HouseholdFindPropertySystem's update runs (vanilla's is then off): only while the
        /// tax engine charges (<paramref name="engineMode"/> is Engine) and no one else (another mod) had already
        /// disabled vanilla's update (<paramref name="foreignDisabled"/>). The copy touches no private vanilla state, so
        /// there is no structure check of its own; the game-version guard covers the copy.
        /// </summary>
        public static bool RunsHomeChoice(TaxEngineMode engineMode, bool foreignDisabled)
        {
            return engineMode == TaxEngineMode.Engine && !foreignDisabled;
        }
    }
}
