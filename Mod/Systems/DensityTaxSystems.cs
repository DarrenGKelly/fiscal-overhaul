using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Density taxes, part 1 of 2. Runs right before the vanilla TaxSystem, on the same frames (same interval,
    /// inherited offset). For the tax payers TaxSystem is about to charge this frame, it adds the density offset
    /// for their building to <see cref="TaxPayer.m_AverageTaxRate"/> — the rate TaxSystem charges — and records
    /// exactly what it added so <see cref="DensityTaxRestoreSystem"/> can take it off again after payment.
    /// Net effect: only the tax actually paid changes; the stored average rate the rest of the game reads is
    /// untouched between payments.
    /// </summary>
    public partial class DensityTaxApplySystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_PayerQuery;
        private NativeParallelHashMap<Entity, int> m_Applied;
        private JobHandle m_AppliedWriter;
        private JobHandle m_AppliedReaders;

        /// <summary>Entities adjusted this frame and by how much. Read by the restore system after TaxSystem.</summary>
        public NativeParallelHashMap<Entity, int> GetApplied(out JobHandle writer)
        {
            writer = m_AppliedWriter;
            return m_Applied;
        }

        /// <summary>Registers a job reading <see cref="GetApplied"/>; the map isn't cleared until it finishes.</summary>
        public void AddReader(JobHandle reader)
        {
            m_AppliedReaders = JobHandle.CombineDependencies(m_AppliedReaders, reader);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Same as TaxSystem: 262144 / (kUpdatesPerDay * 16).
            return 262144 / (TaxSystem.kUpdatesPerDay * 16);
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PayerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<TaxPayer>(),
                    ComponentType.ReadOnly<PropertyRenter>(),
                    ComponentType.ReadOnly<UpdateFrame>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_Applied = new NativeParallelHashMap<Entity, int>(1024, Allocator.Persistent);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_AppliedWriter.Complete();
            m_AppliedReaders.Complete();
            m_Applied.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            m_AppliedWriter.Complete();
            m_AppliedReaders.Complete();
            m_AppliedReaders = default;
            m_Applied.Clear();
            if (!DensityTaxState.IsCharged) return; // disabled in Options, or no offset set: vanilla rates

            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, TaxSystem.kUpdatesPerDay, 16);
            m_PayerQuery.ResetFilter();
            m_PayerQuery.SetSharedComponentFilter(new UpdateFrame(updateFrame));
            int count = m_PayerQuery.CalculateEntityCount();
            if (count == 0) return;
            if (m_Applied.Capacity < count) m_Applied.Capacity = count;

            var job = new ApplyJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(false),
                m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                m_HouseholdType = GetComponentTypeHandle<Household>(true),
                m_PrefabRefs = GetComponentLookup<PrefabRef>(true),
                m_Spawnables = GetComponentLookup<SpawnableBuildingData>(true),
                m_Zones = GetComponentLookup<ZoneData>(true),
                m_ZoneProperties = GetComponentLookup<ZonePropertiesData>(true),
                m_Offsets = DensityTaxState.Native,
                m_Applied = m_Applied.AsParallelWriter(),
            };
            Dependency = job.ScheduleParallel(m_PayerQuery, Dependency);
            m_AppliedWriter = Dependency;
        }

        [BurstCompile]
        private struct ApplyJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public ComponentTypeHandle<Household> m_HouseholdType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefs;
            [ReadOnly] public ComponentLookup<SpawnableBuildingData> m_Spawnables;
            [ReadOnly] public ComponentLookup<ZoneData> m_Zones;
            [ReadOnly] public ComponentLookup<ZonePropertiesData> m_ZoneProperties;
            [ReadOnly] public NativeArray<int> m_Offsets;
            public NativeParallelHashMap<Entity, int>.ParallelWriter m_Applied;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<TaxPayer> payers = chunk.GetNativeArray(ref m_TaxPayerType);
                NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref m_RenterType);
                bool isHousehold = chunk.Has(ref m_HouseholdType);

                for (int i = 0; i < entities.Length; i++)
                {
                    TaxPayer payer = payers[i];
                    if (payer.m_UntaxedIncome <= 0) continue; // nothing to pay, nothing to adjust

                    if (!DensityClassifier.TryClassify(renters[i].m_Property, isHousehold, ref m_PrefabRefs, ref m_Spawnables, ref m_Zones, ref m_ZoneProperties, out int cell)) continue;

                    int offset = m_Offsets[cell];
                    if (offset == 0) continue;

                    int delta = Logic.DensityTaxTable.AppliedDelta(payer.m_AverageTaxRate, offset);
                    if (delta == 0) continue;

                    payer.m_AverageTaxRate += delta;
                    payers[i] = payer;
                    m_Applied.TryAdd(entities[i], delta);
                }
            }
        }
    }

    /// <summary>
    /// Density taxes, part 2 of 2. Runs right after TaxSystem and takes back exactly what
    /// <see cref="DensityTaxApplySystem"/> added, so the stored average rate never drifts.
    /// </summary>
    public partial class DensityTaxRestoreSystem : GameSystemBase
    {
        private DensityTaxApplySystem m_ApplySystem;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / (TaxSystem.kUpdatesPerDay * 16);
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ApplySystem = World.GetOrCreateSystemManaged<DensityTaxApplySystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            NativeParallelHashMap<Entity, int> applied = m_ApplySystem.GetApplied(out JobHandle writer);

            var job = new RestoreJob
            {
                m_Applied = applied,
                m_TaxPayers = GetComponentLookup<TaxPayer>(false),
            };
            // Declaring the TaxPayer lookup makes Dependency include TaxSystem's pay jobs.
            Dependency = job.Schedule(JobHandle.CombineDependencies(Dependency, writer));
            m_ApplySystem.AddReader(Dependency);
        }

        [BurstCompile]
        private struct RestoreJob : IJob
        {
            [ReadOnly] public NativeParallelHashMap<Entity, int> m_Applied;
            public ComponentLookup<TaxPayer> m_TaxPayers;

            public void Execute()
            {
                var enumerator = m_Applied.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var pair = enumerator.Current;
                    if (!m_TaxPayers.HasComponent(pair.Key)) continue;
                    TaxPayer payer = m_TaxPayers[pair.Key];
                    payer.m_AverageTaxRate -= pair.Value;
                    m_TaxPayers[pair.Key] = payer;
                }
                enumerator.Dispose();
            }
        }
    }
}
