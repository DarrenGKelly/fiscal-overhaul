using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
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
    /// Per-district service budgets (BETA): an absolute % (50-150) per (service, district), Outside Districts included,
    /// kept in the save. A building with no override runs at the city slider, as in vanilla.
    ///
    /// This system is the store and the efficiency half: right after vanilla's CityServiceEfficiencySystem (which writes
    /// the city % to buildings) it writes each money-upkeep service building's ServiceBudget efficiency factor at its
    /// district's %. <see cref="DistrictServiceUpkeepSystem"/> is the upkeep half. Both do nothing while
    /// <see cref="FeatureGates.AppliesDistrictServiceBudgets"/> is false; when it turns false, one full pass puts the
    /// city values back.
    /// </summary>
    public partial class DistrictServiceBudgetSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        /// <summary>The safety full pass: every 1/32 month (a month is 262144 frames).</summary>
        public const uint kSafetyPassFrames = 262144 / 32;

        /// <summary>
        /// One Mod.log line per override change and per triggered full pass (what applies, the efficiency each % gives),
        /// none for the periodic safety passes. On while the feature is BETA, for the in-game checks.
        /// </summary>
        public static bool LogPasses = true;

        private Setting m_Setting;
        private PrefabSystem m_PrefabSystem;
        private SimulationSystem m_SimulationSystem;

        private EntityQuery m_BuildingQuery;
        private EntityQuery m_ChangedBuildingQuery;
        private EntityQuery m_UpdatedBudgetQuery;
        private EntityQuery m_ChangedDistrictQuery;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_BudgetDataQuery;
        private EntityQuery m_EfficiencyParameterQuery;

        private readonly DistrictServiceBudgetTable<Entity> m_Table = new DistrictServiceBudgetTable<Entity>(Entity.Null);

        // The table resolved to service entities, for the jobs. Written on the main thread only, after its readers finish.
        private NativeHashMap<ServiceDistrictKey, int> m_Overrides;
        private JobHandle m_OverridesReaders;
        private int m_BuiltRevision = -1;
        private bool m_ResolveDirty = true;

        private int m_PassRevision = -1; // the table revision the last pass saw (GetOverrides may rebuild between passes)
        private bool m_AppliedBefore;
        private bool m_LastApplies;
        private bool m_JustLoaded;
        private uint m_LastFullPassFrame;

        public void SetSettings(Setting setting) => m_Setting = setting;

        /// <summary>The stored overrides (prefab names, district entities), whether or not they apply.</summary>
        public DistrictServiceBudgetTable<Entity> Table => m_Table;

        private bool SimulationChanges => m_Setting?.EnableSimulationChanges ?? FeatureGates.SimulationChangesDefault;
        private bool FeatureEnabled => m_Setting?.EnableDistrictServiceBudget ?? FeatureGates.DistrictServiceBudgetDefault;

        /// <summary>Whether the overrides change the simulation right now (master switch, feature switch, any stored).</summary>
        public bool Applies => FeatureGates.AppliesDistrictServiceBudgets(SimulationChanges, FeatureEnabled, m_Table.HasAny);

        /// <summary>
        /// The overrides as (service entity, district entity) → %, read-only, for jobs (Entity.Null district = outside).
        /// Main thread; a job reading it must be passed to <see cref="AddOverridesReader"/>. Rebuilt from the table first
        /// when it changed. Empty while nothing is stored, but check <see cref="Applies"/> for whether to use it.
        /// </summary>
        public NativeHashMap<ServiceDistrictKey, int> GetOverrides()
        {
            RebuildOverridesIfNeeded();
            return m_Overrides;
        }

        public void AddOverridesReader(JobHandle reader) => m_OverridesReaders = JobHandle.CombineDependencies(m_OverridesReaders, reader);

        /// <summary>
        /// Sets one override (clamped to 50-150; 0 clears). <paramref name="isOutside"/> = Outside Districts, else
        /// <paramref name="district"/> must be a live district. Returns the stored % (0 = none, or refused).
        /// </summary>
        public int Set(string service, bool isOutside, Entity district, int budget)
        {
            if (string.IsNullOrEmpty(service)) return 0;
            if (!isOutside && !IsLiveDistrict(district)) return 0;
            int stored = m_Table.Set(service, isOutside ? Entity.Null : district, budget);
            if (LogPasses) Mod.log.Info($"District service budget: {service} in {(isOutside ? "outside" : district.ToString())} = {(stored == 0 ? "city %" : stored + "%")} ({m_Table.Count} overrides; applies: {Applies})");
            return stored;
        }

        /// <summary>The Options page's reset: every override cleared, so every building runs at the city slider again.</summary>
        public void ResetAll()
        {
            m_Table.Clear();
            if (LogPasses) Mod.log.Info("District service budgets reset");
        }

        /// <summary>The service prefab entity a service key names (its prefab name), or Entity.Null.</summary>
        public Entity ResolveService(string service)
        {
            if (string.IsNullOrEmpty(service)) return Entity.Null;
            using (NativeArray<Entity> services = m_ServiceQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < services.Length; i++)
                    if (m_PrefabSystem.GetPrefabName(services[i]) == service) return services[i];
            }
            return Entity.Null;
        }

        /// <summary>A service prefab entity's key (its prefab name).</summary>
        public string ServiceKey(Entity service) => m_PrefabSystem.GetPrefabName(service);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Overrides = new NativeHashMap<ServiceDistrictKey, int>(16, Allocator.Persistent);

            // As CityServiceEfficiencySystem's queries.
            m_BuildingQuery = GetEntityQuery(ComponentType.ReadOnly<CityServiceUpkeep>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadWrite<Efficiency>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_ChangedBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CityServiceUpkeep>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadWrite<Efficiency>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_UpdatedBudgetQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>(), ComponentType.ReadOnly<Updated>());
            // CurrentDistrictSystem rewrites buildings' CurrentDistrict without marking them Updated, so a drawn,
            // changed or deleted district means a full pass.
            m_ChangedDistrictQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<CollectedCityServiceBudgetData>());
            m_BudgetDataQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            m_EfficiencyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingEfficiencyParameterData>());
            RequireForUpdate(m_BudgetDataQuery);
            RequireForUpdate(m_EfficiencyParameterQuery);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_OverridesReaders.Complete();
            m_Overrides.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_JustLoaded = true;
            // The save's Efficiency buffers may carry override values: owe a pass either way (apply, or restore).
            m_AppliedBefore = true;
            m_ResolveDirty = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool districtsChanged = !m_ChangedDistrictQuery.IsEmptyIgnoreFilter;
            if ((districtsChanged || m_JustLoaded) && m_Table.HasAny) m_Table.Prune(IsLiveDistrict);

            bool applies = Applies;
            bool fullTrigger = m_JustLoaded || districtsChanged || applies != m_LastApplies
                || m_Table.Revision != m_PassRevision || m_ResolveDirty || !m_UpdatedBudgetQuery.IsEmptyIgnoreFilter;
            uint frame = m_SimulationSystem.frameIndex;
            bool safetyDue = frame - m_LastFullPassFrame >= kSafetyPassFrames;
            ServiceBudgetPassKind pass = ServiceBudgetPass.Plan(applies, m_AppliedBefore, fullTrigger, safetyDue);
            m_JustLoaded = false;
            m_LastApplies = applies;
            m_PassRevision = m_Table.Revision;
            if (pass == ServiceBudgetPassKind.None) return;

            RebuildOverridesIfNeeded();
            EntityQuery query = pass == ServiceBudgetPassKind.Full ? m_BuildingQuery : m_ChangedBuildingQuery;
            if (pass == ServiceBudgetPassKind.Full)
            {
                m_LastFullPassFrame = frame;
                if (LogPasses && (fullTrigger || !applies)) LogFullPass(applies); // not the quiet safety passes
            }

            var job = new EfficiencyJob
            {
                m_PrefabRefType = GetComponentTypeHandle<PrefabRef>(true),
                m_InstalledUpgradeType = GetBufferTypeHandle<InstalledUpgrade>(true),
                m_EntityType = GetEntityTypeHandle(),
                m_EfficiencyType = GetBufferTypeHandle<Efficiency>(false),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_ServiceObjectDatas = GetComponentLookup<ServiceObjectData>(true),
                m_ServiceUpkeepDatas = GetBufferLookup<ServiceUpkeepData>(true),
                m_CurrentDistricts = GetComponentLookup<CurrentDistrict>(true),
                m_Owners = GetComponentLookup<Owner>(true),
                m_ServiceBudgets = m_BudgetDataQuery.GetSingletonBuffer<ServiceBudgetData>(true),
                m_Curve = m_EfficiencyParameterQuery.GetSingleton<BuildingEfficiencyParameterData>().m_ServiceBudgetEfficiencyFactor,
                m_Overrides = m_Overrides,
                m_UseOverrides = applies,
            };
            Dependency = job.ScheduleParallel(query, Dependency);
            AddOverridesReader(Dependency);
            m_AppliedBefore = applies;
        }

        private void LogFullPass(bool applies)
        {
            if (!applies)
            {
                Mod.log.Info($"District service budgets: restore pass (every building back to the city %; {m_Table.Count} overrides stored, kept)");
                return;
            }
            AnimationCurve1 curve = m_EfficiencyParameterQuery.GetSingleton<BuildingEfficiencyParameterData>().m_ServiceBudgetEfficiencyFactor;
            var line = new System.Text.StringBuilder($"District service budgets: full pass, {m_Overrides.Count} overrides:");
            foreach (KVPair<ServiceDistrictKey, int> pair in m_Overrides)
            {
                line.Append($" {m_PrefabSystem.GetPrefabName(pair.Key.Service)}@{(pair.Key.District == Entity.Null ? "outside" : pair.Key.District.ToString())}={pair.Value}% (efficiency {curve.Evaluate(pair.Value / 100f):0.00})");
            }
            Mod.log.Info(line.ToString());
        }

        private void RebuildOverridesIfNeeded()
        {
            if (m_Table.Revision == m_BuiltRevision && !m_ResolveDirty) return;
            m_OverridesReaders.Complete();
            m_OverridesReaders = default;
            m_Overrides.Clear();
            var byName = new Dictionary<string, Entity>();
            using (NativeArray<Entity> services = m_ServiceQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < services.Length; i++) byName[m_PrefabSystem.GetPrefabName(services[i])] = services[i];
            }
            foreach (DistrictServiceBudgetTable<Entity>.Entry entry in m_Table.ToEntries())
            {
                // A service this build's prefabs don't have (a removed DLC or asset) is kept in the table, not applied.
                if (!byName.TryGetValue(entry.Service, out Entity service)) continue;
                m_Overrides[new ServiceDistrictKey(service, entry.IsOutside ? Entity.Null : entry.District)] = entry.Budget;
            }
            m_BuiltRevision = m_Table.Revision;
            m_ResolveDirty = byName.Count == 0; // no services yet (prefabs not loaded): try again next time
        }

        private bool IsLiveDistrict(Entity district)
        {
            return district != Entity.Null && EntityManager.Exists(district) && EntityManager.HasComponent<District>(district)
                && !EntityManager.HasComponent<Deleted>(district) && !EntityManager.HasComponent<Temp>(district);
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            List<DistrictServiceBudgetTable<Entity>.Entry> entries = m_Table.ToEntries();
            writer.Write(DistrictServiceBudgetTable<Entity>.FormatVersion);
            writer.Write(entries.Count);
            foreach (DistrictServiceBudgetTable<Entity>.Entry entry in entries)
            {
                writer.Write(entry.Service);
                writer.Write(entry.IsOutside);
                writer.Write(entry.District);
                writer.Write(entry.Budget);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            if (version != DistrictServiceBudgetTable<Entity>.FormatVersion)
            {
                m_Table.Load(version, null); // a format this build doesn't know: start empty
                return;
            }
            reader.Read(out int count);
            var entries = new List<DistrictServiceBudgetTable<Entity>.Entry>(count);
            for (int i = 0; i < count; i++)
            {
                reader.Read(out string service);
                reader.Read(out bool isOutside);
                reader.Read(out Entity district);
                reader.Read(out int budget);
                entries.Add(new DistrictServiceBudgetTable<Entity>.Entry { Service = service, IsOutside = isOutside, District = district, Budget = budget });
            }
            m_Table.Load(version, entries);
        }

        public void SetDefaults(Context context) => m_Table.Clear();

        /// <summary>CityServiceEfficiencySystem's job, with each building's % its district's override (or the city's).</summary>
        [BurstCompile]
        private struct EfficiencyJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> m_PrefabRefType;
            [ReadOnly] public BufferTypeHandle<InstalledUpgrade> m_InstalledUpgradeType;
            public BufferTypeHandle<Efficiency> m_EfficiencyType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
            [ReadOnly] public ComponentLookup<ServiceObjectData> m_ServiceObjectDatas;
            [ReadOnly] public BufferLookup<ServiceUpkeepData> m_ServiceUpkeepDatas;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public DynamicBuffer<ServiceBudgetData> m_ServiceBudgets;
            [ReadOnly] public NativeHashMap<ServiceDistrictKey, int> m_Overrides;
            public AnimationCurve1 m_Curve;
            public bool m_UseOverrides;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabRefType);
                BufferAccessor<InstalledUpgrade> upgrades = chunk.GetBufferAccessor(ref m_InstalledUpgradeType);
                BufferAccessor<Efficiency> efficiencies = chunk.GetBufferAccessor(ref m_EfficiencyType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!m_ServiceObjectDatas.TryGetComponent(prefabs[i].m_Prefab, out ServiceObjectData serviceObject)) continue;
                    float efficiency = 1f;
                    if (HasMoneyUpkeep(prefabs[i].m_Prefab) || (upgrades.Length != 0 && HasMoneyUpkeep(upgrades[i])))
                    {
                        Entity service = serviceObject.m_Service;
                        int city = ServiceUpkeepCost.CityBudget(m_ServiceBudgets, service);
                        Entity district = m_UseOverrides ? ServiceUpkeepCost.DistrictOf(entities[i], ref m_CurrentDistricts, ref m_Owners) : Entity.Null;
                        int budget = ServiceUpkeepCost.EffectiveBudget(m_Overrides, m_UseOverrides, service, district, city);
                        efficiency = m_Curve.Evaluate(budget / 100f);
                    }
                    BuildingUtils.SetEfficiencyFactor(efficiencies[i], EfficiencyFactor.ServiceBudget, efficiency);
                }
            }

            private bool HasMoneyUpkeep(Entity prefab)
            {
                if (!m_ServiceUpkeepDatas.TryGetBuffer(prefab, out DynamicBuffer<ServiceUpkeepData> upkeeps)) return false;
                for (int i = 0; i < upkeeps.Length; i++)
                    if (upkeeps[i].m_Upkeep.m_Resource == Game.Economy.Resource.Money) return true;
                return false;
            }

            private bool HasMoneyUpkeep(DynamicBuffer<InstalledUpgrade> installed)
            {
                for (int i = 0; i < installed.Length; i++)
                    if (m_Prefabs.TryGetComponent(installed[i].m_Upgrade, out PrefabRef prefab) && HasMoneyUpkeep(prefab.m_Prefab)) return true;
                return false;
            }
        }
    }
}
