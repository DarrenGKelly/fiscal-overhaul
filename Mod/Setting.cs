using Colossal.IO.AssetDatabase;
using FiscalOverhaul.Logic;
using Game.Modding;
using Game.Settings;

namespace FiscalOverhaul
{
    /// <summary>
    /// Mod options: a switch per feature, and the density tax offsets (global, not per save) — set from the
    /// Taxation tab, shown in Options only as a reset button. The switches take effect live: the options page calls
    /// <see cref="Game.Settings.Setting.ApplyAndSave"/> on every change, and <see cref="Mod"/> pushes the new values
    /// out from <c>onSettingsApplied</c>.
    /// </summary>
    [FileLocation(nameof(FiscalOverhaul))]
    [SettingsUIGroupOrder(kFeaturesGroup, kTaxGroup)]
    [SettingsUIShowGroupName(kFeaturesGroup, kTaxGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kFeaturesGroup = "Features";
        public const string kTaxGroup = "DensityTax";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        /// <summary>
        /// The master switch. Off: every feature that changes the vanilla simulation (demand, happiness, service
        /// efficiency and upkeep, taxes charged) runs vanilla, whatever its own switch says; stored values are kept.
        /// Today: the tax engine (vanilla TaxSystem charges tax again, live: no density or district adjustments) and the
        /// per-district service budgets (every building follows the city slider).
        /// Any new such feature gates on it too (<see cref="FeatureGates.SimulationChangesDefault"/>).
        /// </summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableSimulationChanges { get; set; } = FeatureGates.SimulationChangesDefault;

        /// <summary>Off: the Budget tab is the vanilla page and no history is built.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableBudgetChart { get; set; } = FeatureGates.BudgetChartDefault;

        /// <summary>Off: buildings pay the vanilla rates and the Taxation tab has no density rows. The offsets are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDensityTaxes { get; set; } = FeatureGates.DensityTaxesDefault;

        /// <summary>Off (or the budget chart off): no per-district budget samples are recorded; the recorded ones are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDistrictBudget { get; set; } = FeatureGates.DistrictBudgetDefault;

        /// <summary>Off (or simulation changes off): every service building runs at the city slider; the per-district overrides (in the save) are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDistrictServiceBudget { get; set; } = FeatureGates.DistrictServiceBudgetDefault;

        /// <summary>Clears the loaded city's per-district service budgets, so every district follows the city slider.</summary>
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool ResetDistrictServiceBudgets
        {
            set => Unity.Entities.World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<DistrictServiceBudgetSystem>()?.ResetAll();
        }

        /// <summary>
        /// Off (default): on a game version the tax engine wasn't checked against, taxes stay vanilla and the Taxation tab
        /// warns. On: the engine runs anyway. Never overrides a failed structure check.
        /// </summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool RunOnUntestedGameVersion { get; set; } = FeatureGates.RunOnUntestedGameVersionDefault;

        /// <summary>
        /// Serialized <see cref="DensityTaxTable"/>: the template a city without stored tax rates is seeded from once
        /// (a new city, or a save from before rates were kept per save; <see cref="TaxRatesSystem"/>). The Taxation tab
        /// edits the loaded city's own rates, not this. Not shown directly.
        /// </summary>
        [SettingsUIHidden]
        public string DensityTaxOffsets { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kTaxGroup)]
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
            EnableSimulationChanges = FeatureGates.SimulationChangesDefault;
            EnableDistrictServiceBudget = FeatureGates.DistrictServiceBudgetDefault;
            RunOnUntestedGameVersion = FeatureGates.RunOnUntestedGameVersionDefault;
            DensityTaxOffsets = string.Empty;
        }
    }
}
