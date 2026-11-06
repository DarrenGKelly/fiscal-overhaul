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

        /// <summary>Serialized <see cref="DensityTaxTable"/>; edited through the Taxation tab, not shown directly.</summary>
        [SettingsUIHidden]
        public string DensityTaxOffsets { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kTaxGroup)]
        public bool ResetDensityTaxes
        {
            set
            {
                DensityTaxOffsets = string.Empty;
                DensityTaxState.Load(DensityTaxOffsets);
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            EnableBudgetChart = FeatureGates.BudgetChartDefault;
            EnableDensityTaxes = FeatureGates.DensityTaxesDefault;
            DensityTaxOffsets = string.Empty;
        }
    }
}
