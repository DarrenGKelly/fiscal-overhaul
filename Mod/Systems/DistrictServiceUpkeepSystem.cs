using FiscalOverhaul.Logic;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
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
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// The upkeep half of per-district service budgets. Right after vanilla CityServiceBudgetSystem has computed this
    /// frame's expense array (which BudgetApplySystem charges and the Budget tab shows), it OVERWRITES the Service
    /// Upkeep entry with the same sum vanilla makes — Σ per service of base (network) cost + non-money upkeep + money
    /// upkeep — but with each building's money upkeep at its district's % (<see cref="ServiceUpkeepMath"/>). Never a
    /// delta on top of vanilla's number, so a missed frame can't compound. Writes nothing while overrides don't apply:
    /// vanilla's own number stands, and vanilla rewrites the entry every frame, so switching off restores it at once.
    /// </summary>
    public partial class DistrictServiceUpkeepSystem : GameSystemBase
    {
        private DistrictServiceBudgetSystem m_Store;
        private CityServiceBudgetSystem m_BudgetSystem;
        private ResourceSystem m_ResourceSystem;
        private CitySystem m_CitySystem;

        private EntityQuery m_UpkeepGroup;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_BudgetDataQuery;
        private EntityQuery m_EconomyParameterQuery;

        // [0] = the row at the city % (vanilla's number from this frame's data), [1] = what was written. For the log.
        private NativeArray<int> m_Report;
        private JobHandle m_ReportDeps;
        private bool m_ReportPending;
        private int m_LoggedRevision = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Store = World.GetOrCreateSystemManaged<DistrictServiceBudgetSystem>();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            // As CityServiceBudgetSystem's upkeep group and service query.
            m_UpkeepGroup = GetEntityQuery(ComponentType.ReadOnly<CityServiceUpkeep>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<CollectedCityServiceBudgetData>(), ComponentType.ReadOnly<CollectedCityServiceUpkeepData>());
            m_BudgetDataQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_Report = new NativeArray<int>(2, Allocator.Persistent);
            RequireForUpdate(m_BudgetDataQuery);
            RequireForUpdate(m_ServiceQuery);
            RequireForUpdate(m_EconomyParameterQuery);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_ReportDeps.Complete();
            m_Report.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            LogReport();
            // Vanilla's budget system only runs in game mode; then there's no expense array to correct.
            if (!m_BudgetSystem.Enabled || !m_Store.Applies || m_CitySystem.City == Entity.Null) return;

            NativeHashMap<ServiceDistrictKey, int> overrides = m_Store.GetOverrides();
            var groups = new NativeHashMap<ServiceBudgetKey, int>(64, Allocator.TempJob);
            var moneyJob = new MoneyJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_PrefabType = GetComponentTypeHandle<PrefabRef>(true),
                m_BuildingType = GetComponentTypeHandle<Building>(true),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_ResourceDatas = GetComponentLookup<ResourceData>(true),
                m_ServiceObjectDatas = GetComponentLookup<ServiceObjectData>(true),
                m_ServiceUpkeepDatas = GetBufferLookup<ServiceUpkeepData>(true),
                m_ServiceUsages = GetComponentLookup<ServiceUsage>(true),
                m_InstalledUpgrades = GetBufferLookup<InstalledUpgrade>(true),
                m_Employees = GetBufferLookup<Employee>(true),
                m_CurrentDistricts = GetComponentLookup<CurrentDistrict>(true),
                m_Owners = GetComponentLookup<Owner>(true),
                m_ServiceBudgets = m_BudgetDataQuery.GetSingletonBuffer<ServiceBudgetData>(true),
                m_ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                m_EconomyParameterData = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>(),
                m_CityModifiers = EntityManager.GetBuffer<CityModifier>(m_CitySystem.City, true),
                m_Overrides = overrides,
                m_Groups = groups,
            };
            JobHandle handle = moneyJob.Schedule(m_UpkeepGroup, Dependency);
            m_ResourceSystem.AddPrefabsReader(handle);

            NativeList<Entity> services = m_ServiceQuery.ToEntityListAsync(Allocator.TempJob, out JobHandle servicesDeps);
            NativeArray<int> expenses = m_BudgetSystem.GetExpenseArray(out JobHandle expenseDeps);
            m_ReportDeps.Complete();
            var sumJob = new SumJob
            {
                m_Services = services,
                m_BudgetDatas = GetComponentLookup<CollectedCityServiceBudgetData>(true),
                m_UpkeepDatas = GetBufferLookup<CollectedCityServiceUpkeepData>(true),
                m_ServiceBudgets = m_BudgetDataQuery.GetSingletonBuffer<ServiceBudgetData>(true),
                m_Groups = groups,
                m_Expenses = expenses,
                m_Report = m_Report,
            };
            handle = sumJob.Schedule(JobHandle.CombineDependencies(handle, servicesDeps, expenseDeps));
            m_BudgetSystem.AddArrayReader(handle);
            m_Store.AddOverridesReader(handle);
            groups.Dispose(handle);
            services.Dispose(handle);
            Dependency = handle;
            m_ReportDeps = handle;

            if (DistrictServiceBudgetSystem.LogPasses && m_LoggedRevision != m_Store.Table.Revision)
            {
                m_LoggedRevision = m_Store.Table.Revision;
                m_ReportPending = true;
            }
        }

        /// <summary>After an override change, one line: the Service Upkeep row at the city % against what is charged.</summary>
        private void LogReport()
        {
            if (!m_ReportPending) return;
            m_ReportPending = false;
            m_ReportDeps.Complete();
            Mod.log.Info($"District service budgets: Service Upkeep row {m_Report[1]} (at the city % it would be {m_Report[0]})");
        }

        /// <summary>Each service's money upkeep at 100%, grouped by the % its buildings run at.</summary>
        [BurstCompile]
        private struct MoneyJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> m_PrefabType;
            [ReadOnly] public ComponentTypeHandle<Building> m_BuildingType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
            [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
            [ReadOnly] public ComponentLookup<ServiceObjectData> m_ServiceObjectDatas;
            [ReadOnly] public BufferLookup<ServiceUpkeepData> m_ServiceUpkeepDatas;
            [ReadOnly] public ComponentLookup<ServiceUsage> m_ServiceUsages;
            [ReadOnly] public BufferLookup<InstalledUpgrade> m_InstalledUpgrades;
            [ReadOnly] public BufferLookup<Employee> m_Employees;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public DynamicBuffer<ServiceBudgetData> m_ServiceBudgets;
            [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;
            [ReadOnly] public EconomyParameterData m_EconomyParameterData;
            [ReadOnly] public DynamicBuffer<CityModifier> m_CityModifiers;
            [ReadOnly] public NativeHashMap<ServiceDistrictKey, int> m_Overrides;
            public NativeHashMap<ServiceBudgetKey, int> m_Groups;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabType);
                NativeArray<Building> buildings = chunk.GetNativeArray(ref m_BuildingType);
                bool hasBuilding = buildings.Length != 0;
                var scratch = new NativeList<ServiceUpkeepData>(4, Allocator.Temp);
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = entities[i];
                    Entity prefab = prefabs[i].m_Prefab;
                    if (!m_ServiceObjectDatas.TryGetComponent(prefab, out ServiceObjectData serviceObject)) continue;
                    bool inactive = hasBuilding && BuildingUtils.CheckOption(buildings[i], BuildingOption.Inactive);
                    int cost = ServiceUpkeepCost.MoneyFullCost(entity, prefab, inactive, scratch, ref m_ServiceUpkeepDatas, ref m_InstalledUpgrades,
                        ref m_Prefabs, ref m_ServiceUsages, ref m_Employees, ref m_ResourceDatas, m_ResourcePrefabs, m_EconomyParameterData, m_CityModifiers);
                    if (cost == 0) continue;
                    Entity service = serviceObject.m_Service;
                    int city = ServiceUpkeepCost.CityBudget(m_ServiceBudgets, service);
                    Entity district = ServiceUpkeepCost.DistrictOf(entity, ref m_CurrentDistricts, ref m_Owners);
                    var key = new ServiceBudgetKey(service, ServiceUpkeepCost.EffectiveBudget(m_Overrides, true, service, district, city));
                    m_Groups.TryGetValue(key, out int sum);
                    m_Groups[key] = sum + cost;
                }
                scratch.Dispose();
            }
        }

        /// <summary>Overwrites the Service Upkeep expense: Σ services of base + non-money + money at each group's %.</summary>
        private struct SumJob : IJob
        {
            [ReadOnly] public NativeList<Entity> m_Services;
            [ReadOnly] public ComponentLookup<CollectedCityServiceBudgetData> m_BudgetDatas;
            [ReadOnly] public BufferLookup<CollectedCityServiceUpkeepData> m_UpkeepDatas;
            [ReadOnly] public DynamicBuffer<ServiceBudgetData> m_ServiceBudgets;
            [ReadOnly] public NativeHashMap<ServiceBudgetKey, int> m_Groups;
            public NativeArray<int> m_Expenses;
            public NativeArray<int> m_Report;

            public void Execute()
            {
                var money = new NativeHashMap<Entity, float>(m_Services.Length, Allocator.Temp);
                var moneyAtCity = new NativeHashMap<Entity, int>(m_Services.Length, Allocator.Temp);
                foreach (KVPair<ServiceBudgetKey, int> group in m_Groups)
                {
                    Entity service = group.Key.Service;
                    money.TryGetValue(service, out float m);
                    money[service] = m + ServiceUpkeepMath.MoneyAt(group.Value, group.Key.Budget);
                    moneyAtCity.TryGetValue(service, out int full);
                    moneyAtCity[service] = full + group.Value;
                }

                int total = 0, atCity = 0;
                for (int s = 0; s < m_Services.Length; s++)
                {
                    Entity service = m_Services[s];
                    if (!m_BudgetDatas.TryGetComponent(service, out CollectedCityServiceBudgetData data)) continue;
                    int nonMoney = 0;
                    if (m_UpkeepDatas.TryGetBuffer(service, out DynamicBuffer<CollectedCityServiceUpkeepData> upkeeps))
                    {
                        for (int i = 0; i < upkeeps.Length; i++)
                            if (upkeeps[i].m_Resource != Resource.Money) nonMoney += upkeeps[i].m_FullCost;
                    }
                    money.TryGetValue(service, out float m);
                    moneyAtCity.TryGetValue(service, out int full);
                    total += ServiceUpkeepMath.ServiceUpkeep(data.m_BaseCost, nonMoney, m);
                    atCity += ServiceUpkeepMath.VanillaServiceUpkeep(data.m_BaseCost, nonMoney, full, ServiceUpkeepCost.CityBudget(m_ServiceBudgets, service));
                }
                m_Expenses[(int)ExpenseSource.ServiceUpkeep] = total;
                m_Report[0] = atCity;
                m_Report[1] = total;
                money.Dispose();
                moneyAtCity.Dispose();
            }
        }
    }
}
