using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
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
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// The district data the Economy panel's tabs share (group "fiscalOverhaul"), built only while the panel is open:
    ///   districts                     raw   [{ key, name }] the city's districts by name; published while any district
    ///                                       feature is on, the tax engine's per-district rates included
    ///                                       (<see cref="FeatureGates.PublishesDistrictList"/>), else []; the one list
    ///                                       every tab's selector reads (Budget, Taxation, Services)
    ///   districtServiceBudgetEnabled  bool  the Services tab shows the selector and per-district sliders
    ///                                       (<see cref="FeatureGates.ShowsDistrictServiceBudgets"/>); false = vanilla tab
    ///   districtServiceBudgets        raw   [{ service, district, budget, upkeep }] (<see cref="DistrictServiceBudgetRows"/>):
    ///                                       service = the service prefab entity "index:version" (vanilla's serviceDetails
    ///                                       entity), district = a DistrictKey or "outside", budget = the override % or
    ///                                       0 (the city slider), upkeep = the area's money upkeep at its effective %
    /// </summary>
    public partial class DistrictListUISystem : UISystemBase
    {
        private const int kRefreshFrames = 30;

        private Setting m_Setting;
        private GamePanelUISystem m_PanelSystem;
        private NameSystem m_NameSystem;
        private DistrictServiceBudgetSystem m_Store;
        private ResourceSystem m_ResourceSystem;
        private CitySystem m_CitySystem;
        private TaxEngineSystem m_TaxEngine;

        private EntityQuery m_DistrictQuery;
        private EntityQuery m_UpkeepGroup;
        private EntityQuery m_BudgetDataQuery;
        private EntityQuery m_EconomyParameterQuery;

        private RawValueBinding m_DistrictsBinding;
        private ValueBinding<bool> m_ServiceEnabledBinding;
        private RawValueBinding m_ServiceBudgetsBinding;

        private bool m_LastPublishes;
        private int m_LastDistrictCount = -1;
        private int m_LastRevision = -1;
        private int m_Frames;

        public void SetSettings(Setting setting) => m_Setting = setting;

        private bool RecordsDistrictBudget => m_Setting != null && FeatureGates.RecordsDistrictBudget(m_Setting.EnableBudgetChart, m_Setting.EnableDistrictBudget);
        private bool ShowsServiceBudgets => m_Setting != null && FeatureGates.ShowsDistrictServiceBudgets(m_Setting.EnableSimulationChanges, m_Setting.EnableDistrictServiceBudget);
        /// <summary>The tax engine charges, so the Taxation tab shows its district selector (TaxRatesUISystem's engineOn).</summary>
        private bool TaxEngineCharges => m_TaxEngine != null && m_TaxEngine.Mode == TaxEngineMode.Engine;
        private bool PublishesList => FeatureGates.PublishesDistrictList(RecordsDistrictBudget, ShowsServiceBudgets, TaxEngineCharges);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_Store = World.GetOrCreateSystemManaged<DistrictServiceBudgetSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_DistrictQuery = GetEntityQuery(ComponentType.ReadOnly<District>(), ComponentType.Exclude<Temp>());
            // As CityServiceBudgetSystem's upkeep group (DistrictServiceUpkeepSystem uses the same).
            m_UpkeepGroup = GetEntityQuery(ComponentType.ReadOnly<CityServiceUpkeep>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_BudgetDataQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            AddBinding(m_DistrictsBinding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "districts", WriteDistricts));
            AddBinding(m_ServiceEnabledBinding = new ValueBinding<bool>(BudgetHistoryUISystem.kGroup, "districtServiceBudgetEnabled", false));
            AddBinding(m_ServiceBudgetsBinding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "districtServiceBudgets", WriteServiceBudgets));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool services = ShowsServiceBudgets;
            m_ServiceEnabledBinding.Update(services);
            bool publishes = PublishesList;
            if (!FeatureGates.BuildsPanelData(publishes, m_PanelSystem.activePanel is EconomyPanel))
            {
                // Switched off: send the empty list once; closed: rebuild as soon as the panel opens again.
                if (m_LastPublishes && !publishes) m_DistrictsBinding.Update();
                m_LastPublishes = publishes;
                m_LastDistrictCount = -1;
                m_LastRevision = -1;
                return;
            }
            m_LastPublishes = true;

            // Names can change without the count changing, and upkeep moves with every building: refresh every
            // kRefreshFrames frames, and at once when a district comes or goes or an override is set.
            int count = m_DistrictQuery.CalculateEntityCount();
            int revision = m_Store.Table.Revision;
            if (count == m_LastDistrictCount && revision == m_LastRevision && ++m_Frames < kRefreshFrames) return;
            m_Frames = 0;
            m_LastDistrictCount = count;
            m_LastRevision = revision;
            m_DistrictsBinding.Update();
            if (services) m_ServiceBudgetsBinding.Update();
        }

        private void WriteDistricts(IJsonWriter writer)
        {
            var choices = new List<KeyValuePair<string, string>>();
            if (PublishesList)
            {
                using (NativeArray<Entity> entities = m_DistrictQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity e in entities)
                        choices.Add(new KeyValuePair<string, string>(DistrictKey.Format(e.Index, e.Version), m_NameSystem.GetRenderedLabelName(e) ?? string.Empty));
                }
                choices.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
            }
            writer.ArrayBegin(choices.Count);
            foreach (var choice in choices)
            {
                writer.TypeBegin("FiscalOverhaul.DistrictChoice");
                writer.PropertyName("key");
                writer.Write(choice.Key);
                writer.PropertyName("name");
                writer.Write(choice.Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private void WriteServiceBudgets(IJsonWriter writer)
        {
            List<DistrictServiceBudgetRows.Row> rows = ShowsServiceBudgets ? BuildRows() : new List<DistrictServiceBudgetRows.Row>();
            writer.ArrayBegin(rows.Count);
            foreach (DistrictServiceBudgetRows.Row row in rows)
            {
                // Every property every time: Gameface fixes a type's properties at its first write.
                writer.TypeBegin("FiscalOverhaul.DistrictServiceBudget");
                writer.PropertyName("service");
                writer.Write(row.Service);
                writer.PropertyName("district");
                writer.Write(row.District);
                writer.PropertyName("budget");
                writer.Write(row.Budget);
                writer.PropertyName("upkeep");
                writer.Write(row.Upkeep);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static string AreaKey(Entity district) => district == Entity.Null ? DistrictKey.Outside : DistrictKey.Format(district.Index, district.Version);
        private static string ServiceKey(Entity service) => DistrictKey.Format(service.Index, service.Version);

        /// <summary>Each (service, area)'s money upkeep at its effective %, merged with the stored overrides. Main thread; completes its job.</summary>
        private List<DistrictServiceBudgetRows.Row> BuildRows()
        {
            var money = new List<KeyValuePair<(string, string), float>>();
            var overrides = new List<KeyValuePair<(string, string), int>>();
            NativeHashMap<ServiceDistrictKey, int> overrideMap = m_Store.GetOverrides();
            foreach (KVPair<ServiceDistrictKey, int> pair in overrideMap)
                overrides.Add(new KeyValuePair<(string, string), int>((ServiceKey(pair.Key.Service), AreaKey(pair.Key.District)), pair.Value));

            if (m_CitySystem.City != Entity.Null && !m_BudgetDataQuery.IsEmptyIgnoreFilter && !m_EconomyParameterQuery.IsEmptyIgnoreFilter)
            {
                var groups = new NativeHashMap<ServiceDistrictKey, float>(64, Allocator.TempJob);
                try
                {
                    var job = new AreaUpkeepJob
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
                        m_Overrides = overrideMap,
                        m_Groups = groups,
                    };
                    JobHandle handle = job.Schedule(m_UpkeepGroup, Dependency);
                    m_ResourceSystem.AddPrefabsReader(handle);
                    m_Store.AddOverridesReader(handle);
                    handle.Complete();
                    foreach (KVPair<ServiceDistrictKey, float> pair in groups)
                        money.Add(new KeyValuePair<(string, string), float>((ServiceKey(pair.Key.Service), AreaKey(pair.Key.District)), pair.Value));
                }
                finally
                {
                    groups.Dispose();
                }
            }
            return DistrictServiceBudgetRows.Build(money, overrides);
        }

        /// <summary>Each building's money upkeep at its effective %, summed per (service, area) — the MoneyJob of DistrictServiceUpkeepSystem, keyed by area.</summary>
        [BurstCompile]
        private struct AreaUpkeepJob : IJobChunk
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
            public NativeHashMap<ServiceDistrictKey, float> m_Groups;

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
                    Entity district = ServiceUpkeepCost.DistrictOf(entity, ref m_CurrentDistricts, ref m_Owners);
                    int city = ServiceUpkeepCost.CityBudget(m_ServiceBudgets, service);
                    int budget = ServiceUpkeepCost.EffectiveBudget(m_Overrides, true, service, district, city);
                    var key = new ServiceDistrictKey(service, district);
                    m_Groups.TryGetValue(key, out float sum);
                    m_Groups[key] = sum + ServiceUpkeepMath.MoneyAt(cost, budget);
                }
                scratch.Dispose();
            }
        }
    }
}
