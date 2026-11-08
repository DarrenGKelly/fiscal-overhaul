using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Taxation tab bindings for density offsets (group "fiscalOverhaul"):
    ///   densityTaxesEnabled  bool  <see cref="Setting.EnableDensityTaxes"/>; false = the UI shows no density rows
    ///   densityTaxes         raw   { maxOffset, cells: [{ zone, density, offset, estimate }] } (supported cells only)
    ///   setDensityTax        trigger(int zone, int density, int offset)
    ///   taxEngineWarning     int   <see cref="TaxEngineWarning"/>: why the Taxation tab warns that taxes are vanilla (0 = no warning)
    /// The estimate is the extra (or lost) monthly revenue the offset brings: each cell's monthly tax paid scaled back
    /// to its taxable base at the rate charged (stored rate + offset), times the offset. Refreshed every ~2 s while the Economy panel is open and density
    /// taxes are enabled.
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

        private long[] m_TaxPaid = new long[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount];
        private double[] m_AverageRate = new double[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount];
        private int m_FramesSinceRefresh = kRefreshFrames;
        private int m_LastVersion = -1;

        public void SetSettings(Setting setting) => m_Setting = setting;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
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
            writer.PropertyName("maxOffset");
            writer.Write(DensityTaxTable.MaxOffset);
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
                    writer.Write((int)DensityTaxTable.EstimateExtraRevenue(m_TaxPaid[cell], m_AverageRate[cell], offset));
                    writer.TypeEnd();
                }
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private void RefreshPaidTotals()
        {
            int cells = m_TaxPaid.Length;
            var paid = new NativeArray<long>(cells, Allocator.TempJob);
            var weightedRate = new NativeArray<double>(cells, Allocator.TempJob);
            try
            {
                var job = new SumJob
                {
                    m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(true),
                    m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                    m_HouseholdType = GetComponentTypeHandle<Household>(true),
                    m_PrefabRefs = GetComponentLookup<PrefabRef>(true),
                    m_Spawnables = GetComponentLookup<SpawnableBuildingData>(true),
                    m_Zones = GetComponentLookup<ZoneData>(true),
                    m_ZoneProperties = GetComponentLookup<ZonePropertiesData>(true),
                    m_Paid = paid,
                    m_WeightedRate = weightedRate,
                };
                // This system's Dependency includes the tax engine's TaxPayer writes. Completed at once: one
                // single-threaded pass every ~2 s while the panel is open, and the binding writes the result this frame.
                job.Schedule(m_PayerQuery, Dependency).Complete();

                for (int i = 0; i < cells; i++)
                {
                    m_TaxPaid[i] = paid[i];
                    m_AverageRate[i] = paid[i] != 0 ? weightedRate[i] / paid[i] : 0;
                }
            }
            finally
            {
                paid.Dispose();
                weightedRate.Dispose();
            }
        }

        /// <summary>Per density cell: monthly tax paid, tax-paid-weighted rate sum.</summary>
        [BurstCompile]
        private struct SumJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public ComponentTypeHandle<Household> m_HouseholdType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefs;
            [ReadOnly] public ComponentLookup<SpawnableBuildingData> m_Spawnables;
            [ReadOnly] public ComponentLookup<ZoneData> m_Zones;
            [ReadOnly] public ComponentLookup<ZonePropertiesData> m_ZoneProperties;
            public NativeArray<long> m_Paid;
            public NativeArray<double> m_WeightedRate;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<TaxPayer> taxPayers = chunk.GetNativeArray(ref m_TaxPayerType);
                NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref m_RenterType);
                bool isHousehold = chunk.Has(ref m_HouseholdType);
                for (int i = 0; i < taxPayers.Length; i++)
                {
                    if (!DensityClassifier.TryClassify(renters[i].m_Property, isHousehold, ref m_PrefabRefs, ref m_Spawnables, ref m_Zones, ref m_ZoneProperties, out int cell)) continue;
                    TaxPayer payer = taxPayers[i];
                    if (payer.m_AverageTaxPaid <= 0) continue;
                    m_Paid[cell] += payer.m_AverageTaxPaid;
                    m_WeightedRate[cell] += (double)payer.m_AverageTaxPaid * payer.m_AverageTaxRate;
                }
            }
        }
    }
}
