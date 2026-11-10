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
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for density offsets (group "fiscalOverhaul"):
    ///   densityTaxesEnabled  bool  <see cref="Setting.EnableDensityTaxes"/>; false = the UI shows no density rows
    ///   densityTaxes         raw   { minRate, maxRate, cells: [{ zone, density, offset, estimate }], districtParts: [4] }
    ///                              (supported cells only; offset = the city's density tax rate for the cell;
    ///                              districtParts = per zone, what district and Outside income-tax overrides add to
    ///                              vanilla's tax, <see cref="PayerTaxParts.DistrictPart"/>: the zone totals add it)
    ///   setDensityTax        trigger(int zone, int density, int offset)
    ///   taxEngineWarning     int   <see cref="TaxEngineWarning"/>: why the Taxation tab warns that taxes are vanilla (0 = no warning)
    /// The estimate is what the cell's density tax brings in a month as the city books it: each payer's own clamped
    /// density part (<see cref="PayerTaxParts.DensityPart"/>, at its resolved rates, <see cref="TaxRateResolver"/>: a
    /// district's override included), summed per cell; negative is the zone's subsidy. Refreshed every ~2 s while the
    /// Economy panel is open and density taxes are enabled, and within <see cref="kRateChangeFrames"/> of a rate change.
    /// </summary>
    public partial class DensityTaxUISystem : UISystemBase
    {
        private const int kRefreshFrames = 120;
        /// <summary>After a rate change, the next pass is at most this many frames away (a dragged slider changes it every frame).</summary>
        private const int kRateChangeFrames = 10;

        private GamePanelUISystem m_PanelSystem;
        private RawValueBinding m_Binding;
        private ValueBinding<bool> m_EnabledBinding;
        private ValueBinding<int> m_EngineWarningBinding;
        private TaxEngineSystem m_TaxEngine;
        private EntityQuery m_PayerQuery;
        private Setting m_Setting;

        private double[] m_DensityPart = new double[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount];
        private readonly double[] m_DistrictPart = new double[DensityTaxTable.ZoneCount];
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
        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool enabled = DensityTaxState.Enabled;
            m_EnabledBinding.Update(enabled);
            m_EngineWarningBinding.Update((int)m_TaxEngine.Warning);
            // District overrides charge with density taxes off too: their parts are needed whenever the engine charges.
            bool needed = enabled || m_TaxEngine.Mode == TaxEngineMode.Engine;
            if (!FeatureGates.BuildsPanelData(needed, m_PanelSystem.activePanel is EconomyPanel))
            {
                m_FramesSinceRefresh = kRefreshFrames; // refresh as soon as the rows show again
                return;
            }

            if (DensityTaxState.Version != m_LastVersion)
            {
                // The rows' rates change at once; their estimates within a few frames.
                m_LastVersion = DensityTaxState.Version;
                m_FramesSinceRefresh = System.Math.Max(m_FramesSinceRefresh, kRefreshFrames - kRateChangeFrames);
                m_Binding.Update();
            }
            if (++m_FramesSinceRefresh >= kRefreshFrames)
            {
                m_FramesSinceRefresh = 0;
                RefreshPaidTotals();
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
                    writer.Write((int)DistrictTaxEstimate.Round(m_DensityPart[cell]));
                    writer.TypeEnd();
                }
            }
            writer.ArrayEnd();
            writer.PropertyName("districtParts");
            writer.ArrayBegin(DensityTaxTable.ZoneCount);
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++) writer.Write((int)DistrictTaxEstimate.Round(m_DistrictPart[z]));
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private void RefreshPaidTotals()
        {
            int cells = m_DensityPart.Length;
            var densityPart = new NativeArray<double>(cells, Allocator.TempJob);
            var districtPart = new NativeArray<double>(DensityTaxTable.ZoneCount, Allocator.TempJob);
            NativeArray<int> wages = TaxRateResolver.Wages(m_EconomyParameterQuery, Allocator.TempJob);
            TaxRateResolver rates = TaxRateResolver.Create(this, m_TaxSystem, m_ResourceSystem.GetPrefabs(), wages, Allocator.TempJob);
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            NativeParallelHashMap<Entity, int2> records = m_TaxEngine.GetChargeRecords(out JobHandle recordDeps, out int nowTick);
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
                    m_Part = densityPart,
                    m_DistrictPart = districtPart,
                    m_Records = records,
                    m_NowTick = nowTick,
                };
                // This system's Dependency includes the tax engine's TaxPayer writes. Completed at once: one
                // single-threaded pass every ~2 s while the panel is open, and the binding writes the result this frame.
                JobHandle handle = job.Schedule(m_PayerQuery, JobHandle.CombineDependencies(Dependency, recordDeps));
                m_TaxEngine.AddChargeRecordsReader(handle);
                handle.Complete();

                // The first 16 ticks after a load have charged only some update-frame groups: scale up to the whole.
                double scale = m_TaxEngine.Mode == TaxEngineMode.Engine ? ChargeWindow.WarmUpScale(nowTick) : 1;
                for (int i = 0; i < cells; i++) m_DensityPart[i] = densityPart[i] * scale;
                for (int z = 0; z < m_DistrictPart.Length; z++) m_DistrictPart[z] = districtPart[z] * scale;
            }
            finally
            {
                densityPart.Dispose();
                districtPart.Dispose();
                rates.Dispose(default);
                wages.Dispose();
            }
        }

        /// <summary>
        /// Per density cell: its payers' summed monthly density parts; per zone (the payer's: <see cref="TaxPayerZone"/>,
        /// as the booking), their district parts (<see cref="PayerTaxParts"/>, clamped as the engine charges them; none
        /// while the engine doesn't charge).
        /// </summary>
        [BurstCompile]
        private struct SumJob : IJobChunk
        {
            /// <summary>Vanilla TaxSystem.kUpdatesPerDay: a payer is charged 32 times a month.</summary>
            private const int kChargesPerMonth = 32;

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
            public NativeArray<double> m_Part;
            public NativeArray<double> m_DistrictPart;
            /// <summary>The engine's charge records: per payer (untaxed income charged, tick) (<see cref="TaxEngineSystem.GetChargeRecords"/>).</summary>
            [ReadOnly] public NativeParallelHashMap<Entity, int2> m_Records;
            /// <summary>The engine's last charging tick (<see cref="ChargeWindow.IsCurrent"/>).</summary>
            public int m_NowTick;

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
                    // The engine charges no district or density tax while it is off.
                    if (!m_EngineCharges) continue;
                    Entity property = renters[i].m_Property;
                    if (property == Entity.Null) continue;
                    // Only a payer the engine charged in the last 1/32 month, from the income it charged (its
                    // m_AverageTaxPaid stays on it for good once it stops earning); restated at its rates now, its own
                    // district and density rates, so a moved slider moves the estimate at once.
                    if (!m_Records.TryGetValue(entities[i], out int2 charge) || !ChargeWindow.IsCurrent(charge.y, m_NowTick)) continue;
                    TaxPayer payer = taxPayers[i];
                    m_Rates.Adjustments(entities[i], property, kind, out int incomeDelta, out int densityTax);
                    PayerTaxParts parts = PayerTaxParts.FromCharge(charge.x, payer.m_AverageTaxRate, densityTax, incomeDelta, limits.x, limits.y, kChargesPerMonth);
                    if (parts.Base <= 0) continue;
                    if (DensityClassifier.TryClassify(property, isHousehold, ref m_PrefabRefs, ref m_Spawnables, ref m_Zones, ref m_ZoneProperties, out int cell))
                        m_Part[cell] += parts.DensityPart;
                    if (incomeDelta == 0) continue;
                    TaxZone zone = TaxZone.Residential;
                    if (!isHousehold && !m_Rates.TryCompanyRow(entities[i], kind, out zone, out _)) continue;
                    m_DistrictPart[(int)zone] += parts.DistrictPart;
                }
            }
        }
    }
}
