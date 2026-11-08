using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;

namespace FiscalOverhaul
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(FiscalOverhaul)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;
        private static TaxEngineSystem s_TaxEngine;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            // Every l10n/<locale>.json: the Options page's text and the UI's (which falls back to English on its own).
            LocaleRegistry.Register(m_Setting);
            AssetDatabase.global.LoadSettings(nameof(FiscalOverhaul), m_Setting, new Setting(this));
            // Tax rates are per save: TaxRatesSystem loads them with each city (seeding a city that has none from the
            // DensityTaxOffsets template) and points DensityTaxState at them.
            // Registered so it gets the load hooks; its OnUpdate does nothing.
            updateSystem.UpdateAt<TaxRatesSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.World.GetOrCreateSystemManaged<TaxRatesSystem>().SetSettings(m_Setting);
            ApplyFeatureSwitches(m_Setting);
            // The options page calls ApplyAndSave on every change, so the switches take effect live.
            m_Setting.onSettingsApplied += ApplyFeatureSwitches;

            // The tax engine: charges tax in place of vanilla TaxSystem's update (which it turns off while it runs), at
            // each payer's vanilla rate plus the city's adjustments. Right before TaxSystem with the same update
            // interval, so it inherits TaxSystem's update offset and charges on exactly its frames; when the guard or
            // the Simulation changes switch says vanilla, TaxSystem runs again right after it on the same frame.
            updateSystem.UpdateBefore<TaxEngineSystem, TaxSystem>(SystemUpdatePhase.GameSimulation);
            s_TaxEngine = updateSystem.World.GetOrCreateSystemManaged<TaxEngineSystem>();
            s_TaxEngine.SetSettings(m_Setting);

            // District budget recording: a sample per 1/32 month, panel open or not, kept in the save. After TaxSystem
            // (and so after the engine); its own interval, so this orders only.
            updateSystem.UpdateAfter<DistrictBudgetSystem, TaxSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.World.GetOrCreateSystemManaged<DistrictBudgetSystem>().SetSettings(m_Setting);

            // UI bindings for the Economy panel. UIUpdate runs every rendered frame, paused or not; the systems gate
            // their own work on the panel being open (FeatureGates).
            updateSystem.UpdateAt<BudgetHistoryUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<DensityTaxUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.World.GetOrCreateSystemManaged<BudgetHistoryUISystem>().SetSettings(m_Setting);
            updateSystem.World.GetOrCreateSystemManaged<DensityTaxUISystem>().SetSettings(m_Setting);
        }

        /// <summary>
        /// Pushes the density tax switch to the simulation, and has the tax engine re-decide (Simulation changes, Run on
        /// untested game version) right away. (The budget chart switch is read by <see cref="BudgetHistoryUISystem"/>
        /// each frame, as it only matters while the UI runs.)
        /// </summary>
        private static void ApplyFeatureSwitches(Game.Settings.Setting setting)
        {
            if (setting is Setting s) DensityTaxState.SetEnabled(s.EnableDensityTaxes);
            s_TaxEngine?.ApplyMode();
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.onSettingsApplied -= ApplyFeatureSwitches;
                s_TaxEngine = null;
                m_Setting.UnregisterInOptionsUI();
                LocaleRegistry.Unregister();
                m_Setting = null;
            }
        }
    }
}
