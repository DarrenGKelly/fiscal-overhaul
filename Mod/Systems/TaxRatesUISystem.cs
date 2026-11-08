using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for the per-save tax adjustments other than the density rows (group "fiscalOverhaul"):
    ///   taxRates     raw      { engineOn, districtCap, districts: [{ key, zone, value }] }: every stored district
    ///                         adjustment (sparse: a missing row is 0), keyed by <see cref="DistrictKey"/>; engineOn =
    ///                         the tax engine charges (<see cref="TaxEngineMode.Engine"/>), so adjustments take effect
    ///                         taxEffect = vanilla DemandParameterData.m_TaxEffect {x,y,z}, for the info-only district demand
    ///   setTaxRate   trigger(int type, int zone, string key, int value)  <see cref="TaxType"/>, <see cref="TaxZone"/>,
    ///                         a district key (District) or a density "0".."2" (Density); clamped to the type's cap
    /// The UI picks the selected district's rows itself, so the selection stays the UI's (district/selection.ts).
    /// The district list comes from <see cref="BudgetHistoryUISystem"/>'s <c>districts</c> binding.
    /// Rewritten only when a rate or the engine's mode changes.
    /// </summary>
    public partial class TaxRatesUISystem : UISystemBase
    {
        private RawValueBinding m_Binding;
        private TaxEngineSystem m_TaxEngine;
        private int m_LastVersion = -1;
        private bool m_LastEngineOn;
        private EntityQuery m_DemandQuery;
        private Unity.Mathematics.float3 m_TaxEffect = new Unity.Mathematics.float3(1f, 1f, 1f);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_DemandQuery = GetEntityQuery(ComponentType.ReadOnly<DemandParameterData>());
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            AddBinding(m_Binding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "taxRates", Write));
            AddBinding(new TriggerBinding<int, int, string, int>(BudgetHistoryUISystem.kGroup, "setTaxRate", SetTaxRate));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool engineOn = EngineOn();
            bool effectChanged = false;
            if (!m_DemandQuery.IsEmptyIgnoreFilter)
            {
                Unity.Mathematics.float3 effect = m_DemandQuery.GetSingleton<DemandParameterData>().m_TaxEffect;
                if (!effect.Equals(m_TaxEffect)) { m_TaxEffect = effect; effectChanged = true; }
            }
            if (DensityTaxState.Version == m_LastVersion && engineOn == m_LastEngineOn && !effectChanged) return;
            m_LastVersion = DensityTaxState.Version;
            m_LastEngineOn = engineOn;
            m_Binding.Update();
        }

        private bool EngineOn() => m_TaxEngine.Mode == TaxEngineMode.Engine;

        /// <summary>A slider moved: stored in the loaded city's table, charged from the next tax payment.</summary>
        private void SetTaxRate(int type, int zone, string key, int value)
        {
            if (!TaxRateRows.TryResolve(type, zone, key, out TaxType taxType, out int keyIndex, out int keyVersion)) return;
            if (taxType == TaxType.Density)
            {
                DensityTaxState.Set((TaxZone)zone, (Density)keyIndex, value);
                return;
            }
            var district = new Entity { Index = keyIndex, Version = keyVersion };
            if (!EntityManager.Exists(district) || !EntityManager.HasComponent<District>(district)
                || EntityManager.HasComponent<Deleted>(district) || EntityManager.HasComponent<Temp>(district)) return;
            DensityTaxState.SetRate(TaxType.District, zone, keyIndex, keyVersion, value);
        }

        private void Write(IJsonWriter writer)
        {
            var rows = TaxRateRows.DistrictRows(DensityTaxState.Table.Rates);
            writer.TypeBegin("FiscalOverhaul.TaxRates");
            writer.PropertyName("engineOn");
            writer.Write(EngineOn());
            writer.PropertyName("districtCap");
            writer.Write(TaxRateTable.DistrictCap);
            writer.PropertyName("taxEffect");
            writer.TypeBegin("FiscalOverhaul.TaxEffect");
            writer.PropertyName("x");
            writer.Write(m_TaxEffect.x);
            writer.PropertyName("y");
            writer.Write(m_TaxEffect.y);
            writer.PropertyName("z");
            writer.Write(m_TaxEffect.z);
            writer.TypeEnd();
            writer.PropertyName("districts");
            writer.ArrayBegin(rows.Count);
            foreach (DistrictRateRow row in rows)
            {
                writer.TypeBegin("FiscalOverhaul.DistrictTaxRate");
                writer.PropertyName("key");
                writer.Write(row.Key);
                writer.PropertyName("zone");
                writer.Write(row.Zone);
                writer.PropertyName("value");
                writer.Write(row.Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }
    }
}
