using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Companies;
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
    /// The Taxation tab's estimates in a district view (group "fiscalOverhaul"):
    ///   districtTaxEstimates  raw  { area, items: [{ zone, key, estimate }], density: [{ zone, density, estimate }],
    ///                              zones: [4], total }: the district's rows as the city books its payers' charges
    ///                              (<see cref="DistrictTaxEstimate"/>, <see cref="PayerTaxParts"/>); area "" = none
    ///   setTaxEstimateArea    trigger(string area)  the district the tab shows (a <see cref="DistrictKey"/> or
    ///                              "outside"; "" = the city view, which keeps vanilla's estimates and needs none)
    /// Each payer's parts at its resolved rates (<see cref="TaxRateResolver"/>) are summed per row every ~2 s while the
    /// Economy panel is open and a district is shown, and within <see cref="kRateChangeFrames"/> of a rate change.
    /// </summary>
    public partial class DistrictTaxEstimateUISystem : UISystemBase
    {
        private const int kRefreshFrames = 120;
        /// <summary>After a rate change, the next pass is at most this many frames away (a dragged slider changes it every frame).</summary>
        private const int kRateChangeFrames = 10;

        private GamePanelUISystem m_PanelSystem;
        private TaxEngineSystem m_TaxEngine;
        private TaxSystem m_TaxSystem;
        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_PayerQuery;
        private EntityQuery m_EconomyParameterQuery;
        private RawValueBinding m_Binding;

        private string m_Area = DistrictKey.City;
        private string m_BasesArea = DistrictKey.City;
        private readonly double[] m_Items = new double[TaxRateModel.BlockSize];
        private readonly double[] m_Density = new double[TaxRateModel.DensityCells];
        private DistrictTaxEstimates m_Estimates;
        private int m_FramesSinceRefresh = kRefreshFrames;
        private int m_LastVersion = -1;

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
            AddBinding(m_Binding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "districtTaxEstimates", Write));
            AddBinding(new TriggerBinding<string>(BudgetHistoryUISystem.kGroup, "setTaxEstimateArea", SetArea));
        }

        private void SetArea(string area)
        {
            area = area ?? DistrictKey.City;
            if (area == m_Area) return;
            m_Area = area;
            m_FramesSinceRefresh = kRefreshFrames; // the new district's estimates at once
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool shown = m_PanelSystem.activePanel is EconomyPanel && m_Area != DistrictKey.City && m_TaxEngine.Mode == TaxEngineMode.Engine;
            if (!shown || !TryArea(m_Area, out Entity district, out bool outside))
            {
                m_FramesSinceRefresh = kRefreshFrames;
                if (m_Estimates != null)
                {
                    m_Estimates = null;
                    m_BasesArea = DistrictKey.City;
                    m_Binding.Update();
                }
                return;
            }

            if (DensityTaxState.Version != m_LastVersion)
            {
                m_LastVersion = DensityTaxState.Version;
                m_FramesSinceRefresh = System.Math.Max(m_FramesSinceRefresh, kRefreshFrames - kRateChangeFrames);
            }
            if (++m_FramesSinceRefresh < kRefreshFrames) return;
            m_FramesSinceRefresh = 0;
            Refresh(district, outside);
            m_BasesArea = m_Area;
            m_Estimates = DistrictTaxEstimate.Compute(m_Items, m_Density);
            m_Binding.Update();
        }

        /// <summary>
        /// A key the tab may show: a live district's entity, or Outside Districts (<paramref name="outside"/>: the
        /// payers whose building is in no district). False for the city or a gone district.
        /// </summary>
        private bool TryArea(string key, out Entity district, out bool outside)
        {
            district = Entity.Null;
            outside = key == DistrictKey.Outside;
            if (outside) return true;
            if (!DistrictKey.TryParse(key, out int index, out int version)) return false;
            district = new Entity { Index = index, Version = version };
            return EntityManager.Exists(district) && EntityManager.HasComponent<District>(district)
                && !EntityManager.HasComponent<Deleted>(district) && !EntityManager.HasComponent<Temp>(district);
        }

        private void Refresh(Entity district, bool outside)
        {
            var items = new NativeArray<double>(TaxRateModel.BlockSize, Allocator.TempJob);
            var density = new NativeArray<double>(TaxRateModel.DensityCells, Allocator.TempJob);
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
                    m_Rates = rates,
                    m_District = district,
                    m_Outside = outside,
                    m_JobLevelLimits = parameters.m_JobLevelTaxLimits,
                    m_ResourceLimits = parameters.m_ResourceTaxLimits,
                    m_Items = items,
                    m_Density = density,
                };
                // One single-threaded pass every ~2 s while a district view is open; the binding writes it this frame.
                job.Schedule(m_PayerQuery, Dependency).Complete();
                for (int i = 0; i < m_Items.Length; i++) m_Items[i] = items[i];
                for (int i = 0; i < m_Density.Length; i++) m_Density[i] = density[i];
            }
            finally
            {
                items.Dispose();
                density.Dispose();
                rates.Dispose(default);
                wages.Dispose();
            }
        }

        private void Write(IJsonWriter writer)
        {
            DistrictTaxEstimates e = m_Estimates;
            writer.TypeBegin("FiscalOverhaul.DistrictTaxEstimates");
            writer.PropertyName("area");
            writer.Write(e != null ? m_BasesArea : DistrictKey.City);

            int items = 0;
            if (e != null) foreach (long v in e.Items) if (v != 0) items++;
            writer.PropertyName("items");
            writer.ArrayBegin(items);
            if (e != null)
            {
                for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
                {
                    for (int k = 0; k < TaxRateModel.ZoneStride; k++)
                    {
                        long v = e.Items[TaxRateModel.DeltaIndex((TaxZone)z, k)];
                        if (v == 0) continue;
                        writer.TypeBegin("FiscalOverhaul.TaxEstimateRow");
                        writer.PropertyName("zone");
                        writer.Write(z);
                        writer.PropertyName("key");
                        writer.Write(k);
                        writer.PropertyName("estimate");
                        writer.Write((int)v);
                        writer.TypeEnd();
                    }
                }
            }
            writer.ArrayEnd();

            int cells = 0;
            if (e != null) foreach (long v in e.Density) if (v != 0) cells++;
            writer.PropertyName("density");
            writer.ArrayBegin(cells);
            if (e != null)
            {
                for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
                {
                    for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                    {
                        long v = e.Density[DensityTaxTable.Index((TaxZone)z, (Density)d)];
                        if (v == 0) continue;
                        writer.TypeBegin("FiscalOverhaul.TaxEstimateCell");
                        writer.PropertyName("zone");
                        writer.Write(z);
                        writer.PropertyName("density");
                        writer.Write(d);
                        writer.PropertyName("estimate");
                        writer.Write((int)v);
                        writer.TypeEnd();
                    }
                }
            }
            writer.ArrayEnd();

            writer.PropertyName("zones");
            writer.ArrayBegin(DensityTaxTable.ZoneCount);
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++) writer.Write(e != null ? (int)e.Zones[z] : 0);
            writer.ArrayEnd();
            writer.PropertyName("total");
            writer.Write(e != null ? (int)e.Total : 0);
            writer.TypeEnd();
        }

        /// <summary>
        /// Per row of the shown district: its payers' monthly parts (<see cref="PayerTaxParts"/>, at the rates the engine
        /// charges them, as if they had income): the income part on the level/resource row, the density part on the density row.
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
            public TaxRateResolver m_Rates;
            public Entity m_District;
            public bool m_Outside;
            public int2 m_JobLevelLimits;
            public int2 m_ResourceLimits;
            public NativeArray<double> m_Items;
            public NativeArray<double> m_Density;

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
                    if (property == Entity.Null) continue;
                    // Outside Districts: a building in no district; else the shown district's buildings.
                    if (m_Rates.m_Keys.District(property) != (m_Outside ? Entity.Null : m_District)) continue;
                    TaxPayer payer = taxPayers[i];
                    m_Rates.Adjustments(entities[i], property, kind, out int incomeDelta, out int densityTax);
                    PayerTaxParts parts = PayerTaxParts.Of(payer.m_AverageTaxPaid, payer.m_UntaxedIncome, payer.m_AverageTaxRate, densityTax, incomeDelta, limits.x, limits.y, kChargesPerMonth);
                    if (parts.Base <= 0) continue;

                    if (isHousehold) m_Rates.AddHouseholdAmount(entities[i], parts.IncomePart, m_Items);
                    else if (m_Rates.TryCompanyRow(entities[i], kind, out TaxZone zone, out int key)) m_Items[TaxRateModel.DeltaIndex(zone, key)] += parts.IncomePart;
                    if (m_Rates.m_Keys.TryDensityCell(property, isHousehold, out int cell)) m_Density[cell] += parts.DensityPart;
                }
            }
        }
    }
}
