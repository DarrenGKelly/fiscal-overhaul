using Colossal.IO.AssetDatabase;
using FiscalOverhaul.Logic;
using Game.Modding;
using Game.Settings;

namespace FiscalOverhaul
{
    /// <summary>
    /// Mod options: on an untested game version, a notice at the top (<see cref="kNoticeGroup"/>); then three groups: <see cref="kFeaturesGroup"/> (the core: only the UI and what it reads from the game),
    /// <see cref="kSimulationGroup"/> (the master switch, then every switch that changes the simulation, greyed out while
    /// the master is off) and <see cref="kResetGroup"/>. Also the density tax template (global, not per save), not shown.
    /// The switches take effect live: the options page calls <see cref="Game.Settings.Setting.ApplyAndSave"/> on every
    /// change, and <see cref="Mod"/> pushes the new values out from <c>onSettingsApplied</c>. The game lists each group's
    /// options in declaration order; the property names are the saved keys, so keep them.
    /// </summary>
    [FileLocation(nameof(FiscalOverhaul))]
    [SettingsUIGroupOrder(kNoticeGroup, kFeaturesGroup, kSimulationGroup, kResetGroup)]
    [SettingsUIShowGroupName(kFeaturesGroup, kSimulationGroup, kResetGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kNoticeGroup = "Notice";
        public const string kFeaturesGroup = "Features";
        public const string kSimulationGroup = "SimulationChanges";
        public const string kResetGroup = "Reset";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // ---- Notice: the untested-version warning, first on the page, shown only while it applies. ----

        /// <summary>
        /// A warning line (the game's warning icon) at the top of the page while the game version is untested and Run on
        /// untested game version is off (<see cref="HidesUntestedVersionNotice"/>): taxes stay vanilla until the mod is
        /// updated or the player ticks the option. Its text is its label. Read-only, so it isn't saved.
        /// </summary>
        [SettingsUISection(kSection, kNoticeGroup)]
        [SettingsUIMultilineText("Media/Misc/Warning.svg")]
        [SettingsUISearchHidden]
        [SettingsUIHideByCondition(typeof(Setting), nameof(HidesUntestedVersionNotice))]
        public string UntestedVersionNotice => string.Empty;

        /// <summary>The notice's hide condition (<see cref="TaxEngineGuardLogic.ShowsUntestedVersionNotice"/>). A method, so it isn't listed as an option.</summary>
        public bool HidesUntestedVersionNotice()
            => !TaxEngineGuardLogic.ShowsUntestedVersionNotice(GameVersionKnown(), RunOnUntestedGameVersion, EnableSimulationChanges);

        private static bool? s_GameVersionKnown;

        /// <summary>Whether the running game's version is one the tax engine was checked against; read once.</summary>
        private static bool GameVersionKnown()
        {
            if (s_GameVersionKnown == null)
                s_GameVersionKnown = TaxEngineGuardLogic.IsKnownVersion(TaxEngineGuard.GameVersion(), TaxEngineGuardLogic.KnownGameVersions);
            return s_GameVersionKnown.Value;
        }

        // ---- Features: the core. Only the UI and what it reads from the game; no simulation change. ----

        /// <summary>Off: the Budget tab is the vanilla page and no history is built.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableBudgetChart { get; set; } = FeatureGates.BudgetChartDefault;

        /// <summary>Off (or the budget chart off): no per-district budget samples are recorded; the recorded ones are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDistrictBudget { get; set; } = FeatureGates.DistrictBudgetDefault;

        /// <summary>
        /// Off: the Services tab is the vanilla page (service list, detail, info column) and the table's data isn't built;
        /// District service budgets then has no district view (its stored budgets still apply).
        /// </summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableServicesTable { get; set; } = FeatureGates.ServicesTableDefault;

        // ---- Simulation changes: the master first, then every switch under it. ----

        /// <summary>
        /// The master switch. Off: every feature that changes the vanilla simulation (demand, happiness, service
        /// efficiency and upkeep, taxes charged) runs vanilla, whatever its own switch says; stored values are kept, and
        /// the switches below it are greyed out (<see cref="SimulationSwitchesDisabled"/>).
        /// Today: the tax engine (vanilla TaxSystem charges tax again, live: no density or district rates, vanilla
        /// happiness and home choice) and the per-district service budgets (every building follows the city slider).
        /// Any new such feature gates on it too and sits in this group (<see cref="FeatureGates.SimulationChangesDefault"/>).
        /// </summary>
        [SettingsUISection(kSection, kSimulationGroup)]
        public bool EnableSimulationChanges { get; set; } = FeatureGates.SimulationChangesDefault;

        /// <summary>Off: buildings pay the vanilla rates and the Taxation tab has no density rows. The offsets are kept.</summary>
        [SettingsUISection(kSection, kSimulationGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(SimulationSwitchesDisabled))]
        public bool EnableDensityTaxes { get; set; } = FeatureGates.DensityTaxesDefault;

        /// <summary>Off (or simulation changes off): every service building runs at the city slider; the per-district overrides (in the save) are kept.</summary>
        [SettingsUISection(kSection, kSimulationGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(SimulationSwitchesDisabled))]
        public bool EnableDistrictServiceBudget { get; set; } = FeatureGates.DistrictServiceBudgetDefault;

        /// <summary>
        /// Off (default): on a game version the tax engine wasn't checked against, taxes stay vanilla, and the Taxation tab
        /// and the top of this page (<see cref="UntestedVersionNotice"/>) say so. On: the engine runs anyway. Never overrides a failed structure check, nor the master switch.
        /// </summary>
        [SettingsUISection(kSection, kSimulationGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(SimulationSwitchesDisabled))]
        public bool RunOnUntestedGameVersion { get; set; } = FeatureGates.RunOnUntestedGameVersionDefault;

        /// <summary>
        /// The Options page's condition for greying out the switches under the master
        /// (<see cref="FeatureGates.DisablesSimulationSwitches"/>). A method, not a property, so it isn't listed as an option.
        /// </summary>
        public bool SimulationSwitchesDisabled() => FeatureGates.DisablesSimulationSwitches(EnableSimulationChanges);

        // ---- Reset: clears stored values, whatever the switches say. ----

        /// <summary>Clears the loaded city's per-district service budgets, so every district follows the city slider.</summary>
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kResetGroup)]
        public bool ResetDistrictServiceBudgets
        {
            set => Unity.Entities.World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<DistrictServiceBudgetSystem>()?.ResetAll();
        }

        /// <summary>
        /// Serialized <see cref="DensityTaxTable"/>: the template a city without stored tax rates is seeded from once
        /// (a new city, or a save from before rates were kept per save; <see cref="TaxRatesSystem"/>). The Taxation tab
        /// edits the loaded city's own rates, not this. Not shown directly.
        /// </summary>
        [SettingsUIHidden]
        public string DensityTaxOffsets { get; set; }

        /// <summary>Sets the density tax template and the loaded city's density rates back to 0.</summary>
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kResetGroup)]
        public bool ResetDensityTaxes
        {
            set
            {
                // Clears the template and the loaded city's offsets.
                DensityTaxOffsets = string.Empty;
                DensityTaxState.ClearAll();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            EnableBudgetChart = FeatureGates.BudgetChartDefault;
            EnableDensityTaxes = FeatureGates.DensityTaxesDefault;
            EnableDistrictBudget = FeatureGates.DistrictBudgetDefault;
            EnableServicesTable = FeatureGates.ServicesTableDefault;
            EnableSimulationChanges = FeatureGates.SimulationChangesDefault;
            EnableDistrictServiceBudget = FeatureGates.DistrictServiceBudgetDefault;
            RunOnUntestedGameVersion = FeatureGates.RunOnUntestedGameVersionDefault;
            DensityTaxOffsets = string.Empty;
        }
    }
}
