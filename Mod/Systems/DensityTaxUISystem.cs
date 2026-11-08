using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for density offsets (group "fiscalOverhaul"):
    ///   densityTaxesEnabled  bool  <see cref="Setting.EnableDensityTaxes"/>; false = the UI shows no density rows
    ///   densityTaxes         raw   { minRate, maxRate, cells: [{ zone, density, offset, estimate }] } (supported cells only;
    ///                              offset = the city's density tax rate for the cell)
    ///   setDensityTax        trigger(int zone, int density, int offset)
    ///   taxEngineWarning     int   <see cref="TaxEngineWarning"/>: why the Taxation tab warns that taxes are vanilla (0 = no warning)
    /// The estimate is the extra (or lost) monthly revenue the city's rate brings: each payer's monthly tax paid scaled
    /// back to its taxable base at the rate it was charged (its own resolved rate, <see cref="TaxRateResolver"/>: a
    /// district's override included), summed per cell, times the city's rate. Refreshed every ~2 s while the Economy
    /// panel is open and density taxes are enabled.
    /// </summary>
    public partial class DensityTaxUISystem : UISystemBase
    {
        private const int kRefreshFrames = 120;

        private GamePanelUISystem m_PanelSystem;
        private RawValueBinding m_Binding;
        private ValueBinding<bool> m_EnabledBinding;
        private ValueBinding<int> m_EngineWarningBinding;
        private TaxEngineSystem m_TaxEngine;
        private EntityQuery m_PayerQuery;
        private Setting m_Setting;

        private double[] m_TaxableBase = new double[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount];
        private TaxSystem m_TaxSystem;
        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_EconomyParameterQuery;
        private int m_FramesSinceRefresh = kRefreshFrames;
        private int m_LastVersion = -1;

        public void SetSettings(Setting setting) => m_Setting = setting;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_PayerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TaxPayer>(), ComponentType.ReadOnly<PropertyRenter>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            AddBinding(m_EnabledBinding = new ValueBinding<bool>(BudgetHistoryUISystem.kGroup, "densityTaxesEnabled", FeatureGates.DensityTaxesDefault));
            AddBinding(m_Binding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "densityTaxes", Write));
            AddBinding(new TriggerBinding<int, int, int>(BudgetHistoryUISystem.kGroup, "setDensityTax", SetDensityTax));
            AddBinding(m_EngineWarningBinding = new ValueBinding<int>(BudgetHistoryUISystem.kGroup, "taxEngineWarning", (int)TaxEngineWarning.None));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool enabled = DensityTaxState.Enabled;
            m_EnabledBinding.Update(enabled);
            m_EngineWarningBinding.Update((int)m_TaxEngine.Warning);
            if (!FeatureGates.BuildsPanelData(enabled, m_PanelSystem.activePanel is EconomyPanel))
            {
                m_FramesSinceRefresh = kRefreshFrames; // refresh as soon as the rows show again
                return;
            }

            bool changed = DensityTaxState.Version != m_LastVersion;
            if (++m_FramesSinceRefresh >= kRefreshFrames)
            {
                m_FramesSinceRefresh = 0;
                RefreshPaidTotals();
                changed = true;
            }
            if (changed)
            {
                m_LastVersion = DensityTaxState.Version;
                m_Binding.Update();
            }
        }

        /// <summary>
        /// A slider moved. The offset applies from the next tax payment. It is the loaded city's (kept in its save by
        /// <see cref="TaxRatesSystem"/>); the Options template is left as it is.
        /// </summary>
        private void SetDensityTax(int zone, int density, int offset)
        {
            if (zone < 0 || zone >= DensityTaxTable.ZoneCount || density < 0 || density >= DensityTaxTable.DensityCount) return;
            DensityTaxState.Set((TaxZone)zone, (Density)density, offset);
        }

        private void Write(IJsonWriter writer)
        {
            DensityTaxTable table = DensityTaxState.Table;
            int supported = 0;
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                    if (DensityTaxTable.IsSupported((TaxZone)z, (Density)d)) supported++;

            writer.TypeBegin("FiscalOverhaul.DensityTaxes");
            writer.PropertyName("minRate");
            writer.Write(DensityTaxTable.MinRate);
            writer.PropertyName("maxRate");
            writer.Write(DensityTaxTable.MaxRate);
            writer.PropertyName("cells");
            writer.ArrayBegin(supported);
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
            {
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                {
                    if (!DensityTaxTable.IsSupported((TaxZone)z, (Density)d)) continue;
                    int cell = DensityTaxTable.Index((TaxZone)z, (Density)d);
                    int offset = table.Get((TaxZone)z, (Density)d);
                    writer.TypeBegin("FiscalOverhaul.DensityTaxCell");
                    writer.PropertyName("zone");
                    writer.Write(z);
                    writer.PropertyName("density");
                    writer.Write(d);
                    writer.PropertyName("offset");
                    writer.Write(offset);
                    writer.PropertyName("estimate");
                    writer.Write((int)DensityTaxTable.EstimateFromBase(m_TaxableBase[cell], offset));
                    writer.TypeEnd();
                }
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private void RefreshPaidTotals()
        {
            int cells = m_TaxableBase.Length;
            var taxableBase = new NativeArray<double>(cells, Allocator.TempJob);
            NativeArray<int> wages = TaxRateResolver.Wages(m_EconomyParameterQuery, Allocator.TempJob);
            TaxRateResolver rates = TaxRateResolver.Create(this, m_TaxSystem, m_ResourceSystem.GetPrefabs(), wages, Allocator.TempJob);
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            try
            {
                var job = new SumJob
                {
                    m_EntityType = GetEntityTypeHandle(),
                    m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(true),
                    m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                    m_HouseholdType = GetComponentTypeHandle<Household>(true),
                    m_CommercialType = GetComponentTypeHandle<ServiceAvailable>(true),
                    m_PrefabRefs = GetComponentLookup<PrefabRef>(true),
                    m_Spawnables = GetComponentLookup<SpawnableBuildingData>(true),
                    m_Zones = GetComponentLookup<ZoneData>(true),
                    m_ZoneProperties = GetComponentLookup<ZonePropertiesData>(true),
                    m_Rates = rates,
                    m_EngineCharges = m_TaxEngine.Mode == TaxEngineMode.Engine,
                    m_JobLevelLimits = parameters.m_JobLevelTaxLimits,
                    m_ResourceLimits = parameters.m_ResourceTaxLimits,
                    m_Base = taxableBase,
                };
                // This system's Dependency includes the tax engine's TaxPayer writes. Completed at once: one
                // single-threaded pass every ~2 s while the panel is open, and the binding writes the result this frame.
                job.Schedule(m_PayerQuery, Dependency).Complete();

                for (int i = 0; i < cells; i++) m_TaxableBase[i] = taxableBase[i];
            }
            finally
            {
                taxableBase.Dispose();
                rates.Dispose(default);
                wages.Dispose();
            }
        }

        /// <summary>
        /// Per density cell: the summed taxable base, each payer's monthly tax paid over the rate it was charged
        /// (<see cref="TaxRateResolver.ChargedRate"/> while the engine charges, its stored rate otherwise).
        /// </summary>
        [BurstCompile]
        private struct SumJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public ComponentTypeHandle<Household> m_HouseholdType;
            [ReadOnly] public ComponentTypeHandle<ServiceAvailable> m_CommercialType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefs;
            [ReadOnly] public ComponentLookup<SpawnableBuildingData> m_Spawnables;
            [ReadOnly] public ComponentLookup<ZoneData> m_Zones;
            [ReadOnly] public ComponentLookup<ZonePropertiesData> m_ZoneProperties;
            public TaxRateResolver m_Rates;
            public bool m_EngineCharges;
            public int2 m_JobLevelLimits;
            public int2 m_ResourceLimits;
            public NativeArray<double> m_Base;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<TaxPayer> taxPayers = chunk.GetNativeArray(ref m_TaxPayerType);
                NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref m_RenterType);
                bool isHousehold = chunk.Has(ref m_HouseholdType);
                TaxPayerKind kind = isHousehold ? TaxPayerKind.Household : chunk.Has(ref m_CommercialType) ? TaxPayerKind.Commercial : TaxPayerKind.Industrial;
                int2 limits = isHousehold ? m_JobLevelLimits : m_ResourceLimits;
                for (int i = 0; i < taxPayers.Length; i++)
                {
                    Entity property = renters[i].m_Property;
                    if (!DensityClassifier.TryClassify(property, isHousehold, ref m_PrefabRefs, ref m_Spawnables, ref m_Zones, ref m_ZoneProperties, out int cell)) continue;
                    TaxPayer payer = taxPayers[i];
                    if (payer.m_AverageTaxPaid <= 0) continue;
                    // The rate this payer was charged: the engine's (the payer's own district and density rates), as
                    // if it had income (a payment with none paid nothing and was skipped above).
                    int charged = payer.m_AverageTaxRate;
                    if (m_EngineCharges)
                    {
                        m_Rates.Adjustments(entities[i], property, kind, out int incomeDelta, out int densityTax);
                        charged = EffectiveTaxRate.Compute(payer.m_AverageTaxRate, densityTax, incomeDelta, limits.x, limits.y, 1);
                    }
                    m_Base[cell] += DensityTaxTable.TaxableBase(payer.m_AverageTaxPaid, charged);
                }
            }
        }
    }
}
