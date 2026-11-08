using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for the per-district rates (group "fiscalOverhaul"):
    ///   taxRates     raw      { engineOn, taxEffect, districts: [{ area, type, zone, key, value }] }: taxEffect =
    ///                         vanilla DemandParameterData.m_TaxEffect {x,y,z}, for the info-only district demand; every stored district
    ///                         override (a missing one follows the city), keyed by <see cref="DistrictKey"/>, value as
    ///                         stored (<see cref="TaxType"/>: a full zone rate, a level/resource offset from the zone
    ///                         rate, a density tax rate); engineOn = the tax engine charges (<see cref="TaxEngineMode.Engine"/>)
    ///   setTaxRate   trigger(int type, int zone, string target, int value)  target = "key@area" (<see cref="TaxRateRows.SplitTarget"/>;
    ///                         a trigger takes at most 4 arguments): a district's value as its slider
    ///                         shows it (<see cref="TaxRateModel.SetDistrictValue"/>: clamped to vanilla's limits; the
    ///                         value it would have without the override removes it); area "" + Density = the city's
    ///                         density tax rate
    ///   resetDistrictTaxRates  trigger(string area)  removes every override of the district ("Use city rates")
    /// The UI picks the selected district's rows itself, so the selection stays the UI's (district/selection.ts). The
    /// district keys change on every load (entity indexes): the UI takes them from the <c>districts</c> binding only.
    /// Rewritten only when a rate or the engine's mode changes.
    /// </summary>
    public partial class TaxRatesUISystem : UISystemBase
    {
        private RawValueBinding m_Binding;
        private TaxEngineSystem m_TaxEngine;
        private TaxSystem m_TaxSystem;
        private int m_LastVersion = -1;
        private bool m_LastEngineOn;
        private EntityQuery m_DemandQuery;
        private Unity.Mathematics.float3 m_TaxEffect = new Unity.Mathematics.float3(1f, 1f, 1f);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_DemandQuery = GetEntityQuery(ComponentType.ReadOnly<DemandParameterData>());
            AddBinding(m_Binding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "taxRates", Write));
            AddBinding(new TriggerBinding<int, int, string, int>(BudgetHistoryUISystem.kGroup, "setTaxRate", SetTaxRate));
            AddBinding(new TriggerBinding<string>(BudgetHistoryUISystem.kGroup, "resetDistrictTaxRates", ResetDistrict));
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
        private void SetTaxRate(int type, int zone, string target, int value)
        {
            TaxRateRows.SplitTarget(target, out string key, out string area);
            if (!TaxRateRows.TryResolve(type, zone, key, area, out TaxType taxType, out int keyIndex, out int areaIndex, out int areaVersion)) return;
            if (areaIndex == TaxRateTable.CityArea && areaVersion == 0)
            {
                // Only density has a city value here (TryResolve): the city's zone and resource rates are vanilla's sliders.
                DensityTaxState.Set((TaxZone)zone, (Density)keyIndex, value);
                return;
            }
            if (!DistrictExists(areaIndex, areaVersion)) return;
            int[] cityRates = TaxRateResolver.CityRates(m_TaxSystem);
            TaxLimits limits = TaxRateResolver.LimitsFrom(m_TaxSystem.GetTaxParameterData());
            DensityTaxState.SetDistrictValue(taxType, (TaxZone)zone, keyIndex, areaIndex, areaVersion, value, cityRates, limits);
        }

        private void ResetDistrict(string area)
        {
            if (!DistrictKey.TryParse(area, out int index, out int version)) return;
            DensityTaxState.ClearDistrict(index, version);
        }

        private bool DistrictExists(int index, int version)
        {
            var district = new Entity { Index = index, Version = version };
            return EntityManager.Exists(district) && EntityManager.HasComponent<District>(district)
                && !EntityManager.HasComponent<Deleted>(district) && !EntityManager.HasComponent<Temp>(district);
        }

        private void Write(IJsonWriter writer)
        {
            var rows = TaxRateRows.DistrictRows(DensityTaxState.Table.Rates);
            writer.TypeBegin("FiscalOverhaul.TaxRates");
            writer.PropertyName("engineOn");
            writer.Write(EngineOn());
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
                writer.PropertyName("area");
                writer.Write(row.Area);
                writer.PropertyName("type");
                writer.Write((int)row.Type);
                writer.PropertyName("zone");
                writer.Write(row.Zone);
                writer.PropertyName("key");
                writer.Write(row.Key);
                writer.PropertyName("value");
                writer.Write(row.Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }
    }
}
