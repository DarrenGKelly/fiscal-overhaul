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

        /// <summary>Off: the Budget tab is the vanilla page and no history is built.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableBudgetChart { get; set; } = FeatureGates.BudgetChartDefault;

        /// <summary>Off: buildings pay the vanilla rates and the Taxation tab has no density rows. The offsets are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDensityTaxes { get; set; } = FeatureGates.DensityTaxesDefault;

        /// <summary>Off (or the budget chart off): no per-district budget samples are recorded; the recorded ones are kept.</summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableDistrictBudget { get; set; } = FeatureGates.DistrictBudgetDefault;

        /// <summary>
        /// Master switch for every change the mod makes to the simulation (today: the tax engine, which charges the
        /// density and district adjustments). Off: vanilla TaxSystem charges tax again, live; the stored rates are kept.
        /// </summary>
        [SettingsUISection(kSection, kFeaturesGroup)]
        public bool EnableSimulationChanges { get; set; } = FeatureGates.SimulationChangesDefault;

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
            RunOnUntestedGameVersion = FeatureGates.RunOnUntestedGameVersionDefault;
            DensityTaxOffsets = string.Empty;
        }
    }
}
