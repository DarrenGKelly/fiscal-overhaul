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

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            AssetDatabase.global.LoadSettings(nameof(FiscalOverhaul), m_Setting, new Setting(this));
            DensityTaxState.Load(m_Setting.DensityTaxOffsets);

            // Density taxes: adjust the rate right before TaxSystem charges it, undo right after.
            // Same update interval as TaxSystem, so both inherit its update offset (same frames).
            updateSystem.UpdateBefore<DensityTaxApplySystem, TaxSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DensityTaxRestoreSystem, TaxSystem>(SystemUpdatePhase.GameSimulation);

            // UI bindings for the Economy panel.
            updateSystem.UpdateAt<BudgetHistoryUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<DensityTaxUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.World.GetOrCreateSystemManaged<DensityTaxUISystem>().SetSettings(m_Setting);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            DensityTaxState.Dispose();
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
