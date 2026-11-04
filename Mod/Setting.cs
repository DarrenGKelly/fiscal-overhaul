using Colossal;
using Colossal.IO.AssetDatabase;
using FiscalOverhaul.Logic;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace FiscalOverhaul
{
    /// <summary>
    /// Mod options. Density tax offsets are stored here (global, not per save) — set from the Taxation tab,
    /// shown read-only-ish in Options with a reset button.
    /// </summary>
    [FileLocation(nameof(FiscalOverhaul))]
    [SettingsUIGroupOrder(kTaxGroup)]
    [SettingsUIShowGroupName(kTaxGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kTaxGroup = "DensityTax";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

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
            DensityTaxOffsets = string.Empty;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Fiscal Overhaul" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kTaxGroup), "Density taxes" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetDensityTaxes)), "Reset density tax offsets" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetDensityTaxes)), "Sets every density offset (set in Economy > Taxation) back to 0, so buildings pay the vanilla rates again." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ResetDensityTaxes)), "Reset all density tax offsets to 0?" },
            };
        }

        public void Unload()
        {
        }
    }
}
