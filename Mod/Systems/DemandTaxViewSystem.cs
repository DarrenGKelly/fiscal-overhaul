using System.Reflection;
using System.Runtime.Serialization;
using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
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
    /// The rates vanilla's demand systems see: a stand-in TaxSystem whose rate array is the demand view
    /// (<see cref="DemandViewMath"/>), not the city's. Never created through the World: it is an uninitialized object
    /// (no constructor, no OnCreate), so it is not one of <c>World.Systems</c>, never updates and is never saved
    /// (the game's SystemSerializerLibrary serializes only systems in <c>World.Systems</c>). The demand systems call
    /// only the non-virtual <c>GetTaxRates()</c> (which returns this instance's private <c>m_TaxRates</c>, set by
    /// reflection to the view) and <c>AddReader()</c> (which combines into this instance's <c>m_Readers</c>) on it.
    /// </summary>
    [DisableAutoCreation]
    public sealed partial class DemandTaxView : TaxSystem
    {
    }

    /// <summary>
    /// Makes vanilla demand react to the density and district tax adjustments the tax engine charges. Vanilla's
    /// Residential, Commercial and Industrial demand systems read only city-level rates (TaxSystem's 92-int array);
    /// this system keeps a second array, the <b>view</b>: each rate slot is the vanilla rate plus the mean adjustment its
    /// payers are charged (each payer weighing 1; households count for all five education slots, companies for their
    /// output resource's slot), clamped as the engine clamps. It points the demand systems' private <c>m_TaxSystem</c>
    /// field at a <see cref="DemandTaxView"/> that serves the view, and back at vanilla TaxSystem whenever the engine
    /// isn't charging (Simulation changes off, guard blocked) or <see cref="TaxEngineGuard.DemandStructureOk"/> fails.
    ///
    /// The payers' means are taken every <see cref="kAggregateInterval"/> frames (a single-threaded job, read back the
    /// next update); the view is recomposed every update from the current vanilla rates, so a slider move on the
    /// vanilla rates reaches demand at once. With every adjustment 0 the view is vanilla's array.
    /// </summary>
    public partial class DemandTaxViewSystem : GameSystemBase
    {
        public const int kAggregateInterval = 4096;

        private TaxSystem m_TaxSystem;
        private TaxEngineSystem m_Engine;
        private SimulationSystem m_SimulationSystem;
        private ResourceSystem m_ResourceSystem;
        private ComponentSystemBase[] m_DemandSystems;
        private FieldInfo m_DemandTaxSystemField;

        private EntityQuery m_HouseholdQuery;
        private EntityQuery m_CommercialQuery;
        private EntityQuery m_IndustrialQuery;
        private EntityQuery m_TaxParameterQuery;

        private DemandTaxView m_View;
        private NativeArray<int> m_ViewRates;
        private NativeArray<long> m_JobSums;
        private NativeArray<int> m_JobCounts;
        private NativeArray<byte> m_JobWeightless;
        private JobHandle m_AggregateHandle;
        private bool m_AggregatePending;
        private bool m_NeedAggregate = true;
        private uint m_LastAggregateFrame;

        private readonly long[] m_Sums = new long[DemandViewMath.SlotCount];
        private readonly int[] m_Counts = new int[DemandViewMath.SlotCount];
        private readonly bool[] m_Weightless = new bool[DemandViewMath.SlotCount];
        private readonly int[] m_Vanilla = new int[DemandViewMath.SlotCount];
        private readonly int[] m_Composed = new int[DemandViewMath.SlotCount];
        private readonly int[] m_Written = new int[DemandViewMath.SlotCount];
        private bool m_HasWritten;

        private bool m_StructureOk;
        private bool m_Swapped;
        private bool m_ForeignLogged;

        /// <summary>Whether the demand systems read the view right now.</summary>
        public bool Active => m_Swapped;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_Engine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_DemandSystems = new ComponentSystemBase[]
            {
                World.GetOrCreateSystemManaged<ResidentialDemandSystem>(),
                World.GetOrCreateSystemManaged<CommercialDemandSystem>(),
                World.GetOrCreateSystemManaged<IndustrialDemandSystem>(),
            };

            // The payers TaxSystem charges (TaxSystem.OnCreate's queries, game version 1.6.0f1), read-only and without
            // the update-frame filter: every payer counts.
            m_HouseholdQuery = GetEntityQuery(ComponentType.ReadOnly<TaxPayer>(), ComponentType.ReadOnly<Household>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_CommercialQuery = GetEntityQuery(ComponentType.ReadOnly<TaxPayer>(), ComponentType.ReadOnly<ServiceAvailable>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_IndustrialQuery = GetEntityQuery(ComponentType.ReadOnly<TaxPayer>(), ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Game.Companies.StorageCompany>(), ComponentType.Exclude<ServiceAvailable>());
            m_TaxParameterQuery = GetEntityQuery(ComponentType.ReadOnly<TaxParameterData>());
            RequireForUpdate(m_TaxParameterQuery);

            m_ViewRates = new NativeArray<int>(DemandViewMath.SlotCount, Allocator.Persistent);
            m_JobSums = new NativeArray<long>(DemandViewMath.SlotCount, Allocator.Persistent);
            m_JobCounts = new NativeArray<int>(DemandViewMath.SlotCount, Allocator.Persistent);
            m_JobWeightless = new NativeArray<byte>(DemandViewMath.SlotCount, Allocator.Persistent);

            m_StructureOk = TaxEngineGuard.DemandStructureOk(out string problem) && CreateView(ref problem);
            Mod.log.Info($"DemandTaxView: structure {(m_StructureOk ? "ok" : "MISMATCH: " + problem)}");
        }

        /// <summary>
        /// The stand-in TaxSystem, built without the World (so it is never registered, updated or saved), its rates
        /// pointed at the view. Checks that GetTaxRates() then really returns the view.
        /// </summary>
        private bool CreateView(ref string problem)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                m_DemandTaxSystemField = typeof(ResidentialDemandSystem).GetField("m_TaxSystem", flags);
                var view = (DemandTaxView)FormatterServices.GetUninitializedObject(typeof(DemandTaxView));
                typeof(TaxSystem).GetField("m_TaxRates", flags).SetValue(view, m_ViewRates);
                // GetTaxRates() must hand out the view itself: a sentinel written to the view reads back through it.
                m_ViewRates[0] = 12345;
                bool same = view.GetTaxRates().Length == DemandViewMath.SlotCount && view.GetTaxRates()[0] == 12345;
                m_ViewRates[0] = 0;
                if (!same)
                {
                    problem = "DemandTaxView.GetTaxRates() doesn't return the view";
                    return false;
                }
                foreach (ComponentSystemBase system in World.Systems)
                {
                    if (ReferenceEquals(system, view))
                    {
                        problem = "DemandTaxView is registered in the World";
                        return false;
                    }
                }
                m_View = view;
                return true;
            }
            catch (System.Exception e)
            {
                problem = $"creating the view threw: {e.Message}";
                return false;
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            Restore();
            m_AggregateHandle.Complete();
            if (m_View != null) m_View.Readers.Complete();
            if (m_ViewRates.IsCreated) m_ViewRates.Dispose();
            if (m_JobSums.IsCreated) m_JobSums.Dispose();
            if (m_JobCounts.IsCreated) m_JobCounts.Dispose();
            if (m_JobWeightless.IsCreated) m_JobWeightless.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            // A new city: the last one's means don't apply. Vanilla until the first aggregate is read.
            m_AggregateHandle.Complete();
            m_AggregatePending = false;
            System.Array.Clear(m_Sums, 0, m_Sums.Length);
            System.Array.Clear(m_Counts, 0, m_Counts.Length);
            System.Array.Clear(m_Weightless, 0, m_Weightless.Length);
            m_NeedAggregate = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool want = m_StructureOk && m_Engine != null && m_Engine.Mode == TaxEngineMode.Engine;
            if (!want)
            {
                Restore();
                return;
            }

            if (m_AggregatePending)
            {
                m_AggregateHandle.Complete();
                m_AggregatePending = false;
                for (int i = 0; i < DemandViewMath.SlotCount; i++)
                {
                    m_Sums[i] = m_JobSums[i];
                    m_Counts[i] = m_JobCounts[i];
                    m_Weightless[i] = m_JobWeightless[i] != 0;
                }
            }

            uint frame = m_SimulationSystem.frameIndex;
            if (m_NeedAggregate || frame - m_LastAggregateFrame >= kAggregateInterval)
            {
                ScheduleAggregate();
                m_LastAggregateFrame = frame;
                m_NeedAggregate = false;
            }

            WriteView();
            Swap();
        }

        /// <summary>Recomposes the view from the current vanilla rates and the last means; writes it only when it changed.</summary>
        private void WriteView()
        {
            m_TaxSystem.GetTaxRates().CopyTo(m_Vanilla);
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            DemandViewMath.Compose(m_Vanilla, m_Sums, m_Counts, m_Weightless,
                parameters.m_JobLevelTaxLimits.x, parameters.m_JobLevelTaxLimits.y,
                parameters.m_ResourceTaxLimits.x, parameters.m_ResourceTaxLimits.y, m_Composed);
            if (m_HasWritten && Same(m_Composed, m_Written)) return;
            // The demand jobs that read the view finish before it changes (vanilla's AddReader pattern).
            m_View.Readers.Complete();
            m_ViewRates.CopyFrom(m_Composed);
            System.Array.Copy(m_Composed, m_Written, m_Composed.Length);
            m_HasWritten = true;
        }

        private static bool Same(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Points the demand systems at the view; leaves any a third party re-pointed alone.</summary>
        private void Swap()
        {
            if (m_Swapped) return;
            foreach (ComponentSystemBase demand in m_DemandSystems)
            {
                object current = m_DemandTaxSystemField.GetValue(demand);
                if (ReferenceEquals(current, m_TaxSystem)) m_DemandTaxSystemField.SetValue(demand, m_View);
                else if (!ReferenceEquals(current, m_View) && !m_ForeignLogged)
                {
                    Mod.log.Warn($"DemandTaxView: {demand.GetType().Name}.m_TaxSystem was replaced by someone else ({current?.GetType().FullName ?? "null"}); left alone");
                    m_ForeignLogged = true;
                }
            }
            m_Swapped = true;
            Mod.log.Info("DemandTaxView: demand reads the adjusted view");
        }

        /// <summary>Points the demand systems back at vanilla TaxSystem (only those pointing at the view).</summary>
        private void Restore()
        {
            if (!m_Swapped || m_DemandTaxSystemField == null) return;
            foreach (ComponentSystemBase demand in m_DemandSystems)
                if (ReferenceEquals(m_DemandTaxSystemField.GetValue(demand), m_View)) m_DemandTaxSystemField.SetValue(demand, m_TaxSystem);
            m_Swapped = false;
            Mod.log.Info("DemandTaxView: demand reads vanilla rates");
        }

        private void ScheduleAggregate()
        {
            for (int i = 0; i < DemandViewMath.SlotCount; i++)
            {
                m_JobSums[i] = 0;
                m_JobCounts[i] = 0;
                m_JobWeightless[i] = 0;
            }
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            var density = new NativeArray<int>(DensityTaxState.Snapshot(), Allocator.TempJob);
            NativeParallelHashMap<Entity, int4> districts = TaxEngineSystem.BuildDistrictAdjustments();

            JobHandle handle = Dependency;
            handle = Schedule(m_HouseholdQuery, 0, parameters.m_JobLevelTaxLimits, density, districts, handle);
            handle = Schedule(m_CommercialQuery, 1, parameters.m_ResourceTaxLimits, density, districts, handle);
            handle = Schedule(m_IndustrialQuery, 2, parameters.m_ResourceTaxLimits, density, districts, handle);
            density.Dispose(handle);
            districts.Dispose(handle);
            Dependency = handle;
            m_AggregateHandle = handle;
            m_AggregatePending = true;
        }

        private JobHandle Schedule(EntityQuery query, int kind, int2 limits, NativeArray<int> density, NativeParallelHashMap<Entity, int4> districts, JobHandle deps)
        {
            var job = new AggregateJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(true),
                m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_ProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                m_ResourceDatas = GetComponentLookup<ResourceData>(true),
                m_ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                m_Kind = kind,
                m_Limits = limits,
                m_DensityAdjustments = density,
                m_DistrictAdjustments = districts,
                m_Keys = TaxKeyResolver.Create(this),
                m_Sums = m_JobSums,
                m_Counts = m_JobCounts,
                m_Weightless = m_JobWeightless,
            };
            return job.Schedule(query, deps);
        }

        /// <summary>
        /// Sums each payer's charged adjustment (<see cref="DemandViewMath.PayerAdjustment"/>, the engine's keys and
        /// clamp) per rate slot. Single-threaded (Schedule): the sums are plain arrays.
        /// Kind 0: households (all five education slots); 1: commercial companies (their output's commercial slot);
        /// 2: processing companies (their output's industrial slot, flagged weightless when vanilla books it as Office).
        /// </summary>
        [BurstCompile]
        private struct AggregateJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> m_ProcessDatas;
            [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
            [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;
            public int m_Kind;
            public int2 m_Limits;
            [ReadOnly] public NativeArray<int> m_DensityAdjustments;
            [ReadOnly] public NativeParallelHashMap<Entity, int4> m_DistrictAdjustments;
            public TaxKeyResolver m_Keys;
            public NativeArray<long> m_Sums;
            public NativeArray<int> m_Counts;
            public NativeArray<byte> m_Weightless;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<TaxPayer> payers = chunk.GetNativeArray(ref m_TaxPayerType);
                bool hasRenter = chunk.Has(ref m_RenterType);
                NativeArray<PropertyRenter> renters = hasRenter ? chunk.GetNativeArray(ref m_RenterType) : default;
                bool isHousehold = m_Kind == 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    int slot = -1;
                    bool weightless = false;
                    if (!isHousehold)
                    {
                        if (!m_Prefabs.TryGetComponent(entity, out PrefabRef prefabRef)) continue;
                        if (!m_ProcessDatas.TryGetComponent(prefabRef.m_Prefab, out IndustrialProcessData process)) continue;
                        Resource resource = process.m_Output.m_Resource;
                        int index = EconomyUtils.GetResourceIndex(resource);
                        slot = (m_Kind == 1 ? DemandViewMath.CommercialFirst : DemandViewMath.IndustrialFirst) + index;
                        if (slot < 0 || slot >= DemandViewMath.SlotCount) continue;
                        if (m_Kind == 2)
                        {
                            Entity resourcePrefab = m_ResourcePrefabs[resource];
                            weightless = m_ResourceDatas.TryGetComponent(resourcePrefab, out ResourceData data) && data.m_Weight == 0f;
                        }
                    }

                    int adjustment = Adjustment(payers[i].m_AverageTaxRate, entity, hasRenter ? renters[i].m_Property : Entity.Null, isHousehold, weightless);
                    if (isHousehold)
                    {
                        for (int k = 0; k < 5; k++)
                        {
                            m_Sums[DemandViewMath.ResidentialFirst + k] += adjustment;
                            m_Counts[DemandViewMath.ResidentialFirst + k] += 1;
                        }
                    }
                    else
                    {
                        m_Sums[slot] += adjustment;
                        m_Counts[slot] += 1;
                        if (weightless) m_Weightless[slot] = 1;
                    }
                }
            }

            /// <summary>The engine's adjustment for this payer (TaxEngineSystem.PayTaxJob.ChargedRate, as a change).</summary>
            private int Adjustment(int baseRate, Entity entity, Entity property, bool isHousehold, bool weightless)
            {
                if (property == Entity.Null) return 0;
                int density = 0;
                if (m_Keys.TryDensityCell(property, isHousehold, out int cell)) density = m_DensityAdjustments[cell];
                int district = 0;
                if (!m_DistrictAdjustments.IsEmpty)
                {
                    Entity districtEntity = m_Keys.District(property);
                    if (districtEntity != Entity.Null && m_DistrictAdjustments.TryGetValue(districtEntity, out int4 adjustments))
                    {
                        TaxZone zone = TaxPayerZone.Of(isHousehold, m_Kind == 1, weightless);
                        district = adjustments[(int)zone];
                    }
                }
                return DemandViewMath.PayerAdjustment(baseRate, density, district, m_Limits.x, m_Limits.y);
            }
        }
    }
}
