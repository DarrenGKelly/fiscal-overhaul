using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Areas;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for the per-district rates (group "fiscalOverhaul"):
    ///   taxRates     raw      { engineOn, defaults: { zoneRate, itemOffset, density }, districts: [{ area, type, zone, key, value }] }:
    ///                         vanilla's default rates (<see cref="TaxRateModel.DefaultZoneRate"/>: a city row's reset
    ///                         target) and every stored district
    ///                         override (a missing one follows the city), keyed by <see cref="TaxRateRows.AreaKey"/>
    ///                         (a district's <see cref="DistrictKey"/>, or "outside" for Outside Districts), value as
    ///                         stored (<see cref="TaxType"/>: a full zone rate, a level/resource offset from the zone
    ///                         rate, a density tax rate); engineOn = the tax engine charges (<see cref="TaxEngineMode.Engine"/>)
    ///   setTaxRate   trigger(int type, int zone, string target, int value)  target = "key@area" (<see cref="TaxRateRows.SplitTarget"/>;
    ///                         a trigger takes at most 4 arguments): a district's value as its slider
    ///                         shows it (<see cref="TaxRateModel.SetDistrictValue"/>: clamped to vanilla's limits; the
    ///                         value it would have without the override removes it); area "" + Density = the city's
    ///                         density tax rate
    ///   clearTaxRate trigger(int type, int zone, string target)  target "key@area": removes that one district
    ///                         override (a row's reset button), so the row follows the city again
    ///   resetDistrictTaxRates  trigger(string area)  removes every override of the district (no UI uses it now)
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

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            AddBinding(m_Binding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "taxRates", Write));
            AddBinding(new TriggerBinding<int, int, string, int>(BudgetHistoryUISystem.kGroup, "setTaxRate", SetTaxRate));
            AddBinding(new TriggerBinding<int, int, string>(BudgetHistoryUISystem.kGroup, "clearTaxRate", ClearTaxRate));
            AddBinding(new TriggerBinding<string>(BudgetHistoryUISystem.kGroup, "resetDistrictTaxRates", ResetDistrict));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool engineOn = EngineOn();
            if (DensityTaxState.Version == m_LastVersion && engineOn == m_LastEngineOn) return;
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
            if (!TaxRateTable.IsOutside(areaIndex, areaVersion) && !DistrictExists(areaIndex, areaVersion)) return;
            int[] cityRates = TaxRateResolver.CityRates(m_TaxSystem);
            TaxLimits limits = TaxRateResolver.LimitsFrom(m_TaxSystem.GetTaxParameterData());
            DensityTaxState.SetDistrictValue(taxType, (TaxZone)zone, keyIndex, areaIndex, areaVersion, value, cityRates, limits);
        }

        /// <summary>A district row's reset button: its override removed (the city has none to remove).</summary>
        private void ClearTaxRate(int type, int zone, string target)
        {
            TaxRateRows.SplitTarget(target, out string key, out string area);
            if (!TaxRateRows.TryResolve(type, zone, key, area, out TaxType taxType, out int keyIndex, out int areaIndex, out int areaVersion)) return;
            if (areaIndex == TaxRateTable.CityArea && areaVersion == 0) return;
            DensityTaxState.ClearDistrictValue(taxType, (TaxZone)zone, keyIndex, areaIndex, areaVersion);
        }

        private void ResetDistrict(string area)
        {
            if (!TaxRateRows.TryArea(area, out int index, out int version)) return;
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
            writer.PropertyName("defaults");
            writer.TypeBegin("FiscalOverhaul.TaxDefaults");
            writer.PropertyName("zoneRate");
            writer.Write(TaxRateModel.DefaultZoneRate);
            writer.PropertyName("itemOffset");
            writer.Write(TaxRateModel.DefaultItemOffset);
            writer.PropertyName("density");
            writer.Write(TaxRateModel.DefaultDensityRate);
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
