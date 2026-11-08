using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
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
    /// Records each district's budget 32 times a month, whether or not the Economy panel is open, and keeps the
    /// history in the save (<see cref="IDefaultSerializable"/>: the game serializes every world system implementing it,
    /// and a save loaded without the mod skips the block).
    ///
    /// Each sample weighs every district (plus "outside", <see cref="Entity.Null"/>, for buildings in no district) per
    /// source and splits the vanilla Budget row by those weights (<see cref="DistrictBudgetMath.Samples"/>):
    /// taxes and subsidies by the payers' paid sums (scaled so the policy-blind vanilla row is matched at the city rate,
    /// so a district tax discount stays in its district; while the tax engine charges, the parts it books beyond vanilla
    /// count as paid and a negative density part weighs the subsidy slot, <see cref="DistrictBudgetMath.PayerTaxWeights"/>); electricity / water by fulfilled consumption; healthcare /
    /// education by patients / students; garbage by garbage produced; parking by parked cars x their lane's fee;
    /// public transport by residents; service upkeep by each service building's upkeep (the road / network part stays
    /// city-only, it is not apportioned). The upkeep is also weighed per service (each building's upkeep under its
    /// service), so the Budget tab can split a district's Service Upkeep per service
    /// (<see cref="ServiceUpkeepSplit.SplitSeries"/>); those weights are kept beside each district's history.
    /// </summary>
    public partial class DistrictBudgetSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        /// <summary>One sample per 1/32 month (a month is 262144 frames). Provisional.</summary>
        public const int kUpdateInterval = 262144 / 32;

        /// <summary>Writes one Mod.log line per sample (district count, tax scales, sums against vanilla). Off; set true for an in-game check.</summary>
        public static bool LogSamples = false;

        private const int S = DistrictBudgetMath.SourceCount;
        // Slots within a sample (DistrictIncomeSources then DistrictExpenseSources).
        private const int kSlotElectricity = 4, kSlotWater = 5, kSlotHealthcare = 6, kSlotEducation = 7, kSlotGarbage = 8;
        private const int kSlotParking = 9, kSlotPublicTransport = 10, kSlotSubsidyStart = 11, kSlotUpkeep = 15;

        private Setting m_Setting;
        private CityServiceBudgetSystem m_BudgetSystem;
        private TaxSystem m_TaxSystem;
        private ResourceSystem m_ResourceSystem;
        private CitySystem m_CitySystem;
        private DistrictServiceBudgetSystem m_ServiceBudgetStore;
        private TaxEngineSystem m_TaxEngine;

        private EntityQuery m_DistrictQuery;
        private EntityQuery m_PayerQuery;
        private EntityQuery m_UpkeepQuery;
        private EntityQuery m_ConsumerQuery;
        private EntityQuery m_ParkedCarQuery;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_BudgetDataQuery;
        private EntityQuery m_EconomyParameterQuery;

        private readonly Dictionary<Entity, DistrictHistory> m_Histories = new Dictionary<Entity, DistrictHistory>();
        // Per area, the per-service upkeep weights of each sample (by m_ServiceColumns), aligned with the newest of its
        // history (a district loaded from a format 1 save has fewer, or none).
        private readonly Dictionary<Entity, ServiceWeightHistory> m_ServiceWeights = new Dictionary<Entity, ServiceWeightHistory>();
        private readonly ServiceColumns m_ServiceColumns = new ServiceColumns();
        private PrefabSystem m_PrefabSystem;

        // The latest sample's inputs, for LiveValues.
        private readonly Dictionary<Entity, int> m_LatestAreas = new Dictionary<Entity, int>();
        private double[] m_LatestWeights = new double[0];
        private readonly double[] m_LatestAtCityRate = new double[DistrictBudgetMath.TaxRowCount];
        private int m_LatestNetworkUpkeep;

        public void SetSettings(Setting setting) => m_Setting = setting;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kUpdateInterval;

        /// <summary>The recorded samples of a district (<see cref="Entity.Null"/> = outside any district).</summary>
        public bool TryGetHistory(Entity district, out DistrictHistory history) => m_Histories.TryGetValue(district, out history);

        /// <summary>Every area with a history: the districts, and Entity.Null for outside.</summary>
        public IEnumerable<Entity> Areas => m_Histories.Keys;

        /// <summary>The district's per-service upkeep weights, by <see cref="ServiceColumns"/>; false when none were recorded.</summary>
        public bool TryGetServiceWeights(Entity district, out ServiceWeightHistory weights) => m_ServiceWeights.TryGetValue(district, out weights);

        /// <summary>The service names of the weight columns.</summary>
        public ServiceColumns ServiceColumns => m_ServiceColumns;

        /// <summary>
        /// The district's values now (SourceCount wide, slot order of <see cref="DistrictBudgetMath"/>): the latest
        /// sample's weights times the live vanilla Budget rows. Null for an area the latest sample didn't have.
        /// </summary>
        public float[] LiveValues(Entity district)
        {
            if (!m_LatestAreas.TryGetValue(district, out int area)) return null;
            int areaCount = m_LatestWeights.Length / S;
            float[][] samples = DistrictBudgetMath.Samples(m_LatestWeights, areaCount, ReadVanilla(m_LatestNetworkUpkeep), m_LatestAtCityRate);
            return samples[area];
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_ServiceBudgetStore = World.GetOrCreateSystemManaged<DistrictServiceBudgetSystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // As Game.UI.InGame.DistrictsSection.
            m_DistrictQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Areas.District>(), ComponentType.Exclude<Temp>());
            m_PayerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TaxPayer>(), ComponentType.ReadOnly<PropertyRenter>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            // As CityServiceBudgetSystem's upkeep group.
            m_UpkeepQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CityServiceUpkeep>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ConsumerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<ElectricityConsumer>(), ComponentType.ReadOnly<WaterConsumer>(),
                    ComponentType.ReadOnly<GarbageProducer>(), ComponentType.ReadOnly<Patient>(),
                    ComponentType.ReadOnly<Game.Buildings.Student>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ParkedCarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkedCar>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<CollectedCityServiceBudgetData>());
            m_BudgetDataQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Setting == null || !FeatureGates.RecordsDistrictBudget(m_Setting.EnableBudgetChart, m_Setting.EnableDistrictBudget)) return;
            if (m_CitySystem.City == Entity.Null || m_EconomyParameterQuery.IsEmptyIgnoreFilter || m_BudgetDataQuery.IsEmptyIgnoreFilter) return;
            Sample();
        }

        private void Sample()
        {
            NativeArray<Entity> districts = m_DistrictQuery.ToEntityArray(Allocator.TempJob);
            int areaCount = districts.Length + 1; // + outside, last
            var areaOf = new NativeHashMap<Entity, int>(districts.Length, Allocator.TempJob);
            var weights = new NativeArray<double>(areaCount * S, Allocator.TempJob);
            var atCityRate = new NativeArray<double>(DistrictBudgetMath.TaxRowCount, Allocator.TempJob);
            List<KeyValuePair<Entity, int>> services = ServiceUpkeepSystem.ServiceColumnsOf(EntityManager, m_PrefabSystem, m_ServiceQuery, m_ServiceColumns);
            int columnCount = m_ServiceColumns.Count;
            var serviceColumn = new NativeHashMap<Entity, int>(services.Count, Allocator.TempJob);
            var serviceWeights = new NativeArray<double>(areaCount * columnCount, Allocator.TempJob);
            // The tax engine's rates, to split what each payer paid as the city books it (TaxBookingSystem).
            NativeArray<int> wages = TaxRateResolver.Wages(m_EconomyParameterQuery, Allocator.TempJob);
            TaxRateResolver rates = TaxRateResolver.Create(this, m_TaxSystem, m_ResourceSystem.GetPrefabs(), wages, Allocator.TempJob);
            TaxParameterData taxParameters = m_TaxSystem.GetTaxParameterData();
            try
            {
                foreach (KeyValuePair<Entity, int> service in services) serviceColumn[service.Key] = service.Value;
                for (int i = 0; i < districts.Length; i++) areaOf[districts[i]] = i;
                int outside = districts.Length;

                // The engine's charge records: while it charges, only payers it charged in the last 1/32 month weigh.
                NativeParallelHashMap<Entity, int2> records = m_TaxEngine.GetChargeRecords(out JobHandle recordDeps, out int nowTick);
                var currentDistricts = GetComponentLookup<CurrentDistrict>(true);
                var owners = GetComponentLookup<Owner>(true);
                var prefabRefs = GetComponentLookup<PrefabRef>(true);

                var taxJob = new TaxJob
                {
                    m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(true),
                    m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                    m_HouseholdType = GetComponentTypeHandle<Household>(true),
                    m_ServiceAvailableType = GetComponentTypeHandle<ServiceAvailable>(true),
                    m_ProcessingType = GetComponentTypeHandle<Game.Companies.ProcessingCompany>(true),
                    m_StorageType = GetComponentTypeHandle<Game.Companies.StorageCompany>(true),
                    m_HouseholdCitizenType = GetBufferTypeHandle<HouseholdCitizen>(true),
                    m_EntityType = GetEntityTypeHandle(),
                    m_Workers = GetComponentLookup<Worker>(true),
                    m_PrefabRefs = prefabRefs,
                    m_ProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                    m_ResourceDatas = GetComponentLookup<ResourceData>(true),
                    m_ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    m_TaxRates = m_TaxSystem.GetTaxRates(),
                    m_CurrentDistricts = currentDistricts,
                    m_Owners = owners,
                    m_AreaOf = areaOf,
                    m_Outside = outside,
                    m_Weights = weights,
                    m_AtCityRate = atCityRate,
                    m_DistrictModifiers = GetBufferLookup<DistrictModifier>(true),
                    m_Rates = rates,
                    m_EngineCharges = m_TaxEngine.Mode == TaxEngineMode.Engine,
                    m_Records = records,
                    m_NowTick = nowTick,
                    m_JobLevelLimits = taxParameters.m_JobLevelTaxLimits,
                    m_ResourceLimits = taxParameters.m_ResourceTaxLimits,
                };
                JobHandle handle = taxJob.Schedule(m_PayerQuery, JobHandle.CombineDependencies(Dependency, recordDeps));
                m_TaxEngine.AddChargeRecordsReader(handle);

                // Per-district service budgets: a building's upkeep runs at its district's %, as DistrictServiceUpkeepSystem charges it.
                bool useOverrides = m_ServiceBudgetStore.Applies;
                NativeHashMap<ServiceDistrictKey, int> overrides = m_ServiceBudgetStore.GetOverrides();
                var upkeepJob = new UpkeepJob
                {
                    m_EntityType = GetEntityTypeHandle(),
                    m_PrefabType = GetComponentTypeHandle<PrefabRef>(true),
                    m_BuildingType = GetComponentTypeHandle<Building>(true),
                    m_Prefabs = prefabRefs,
                    m_ResourceDatas = GetComponentLookup<ResourceData>(true),
                    m_ServiceObjectDatas = GetComponentLookup<ServiceObjectData>(true),
                    m_ServiceUpkeepDatas = GetBufferLookup<ServiceUpkeepData>(true),
                    m_ServiceUsages = GetComponentLookup<ServiceUsage>(true),
                    m_InstalledUpgradeBufs = GetBufferLookup<InstalledUpgrade>(true),
                    m_EmployeeBufs = GetBufferLookup<Employee>(true),
                    m_ServiceBudgets = m_BudgetDataQuery.GetSingletonBuffer<ServiceBudgetData>(true),
                    m_ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    m_EconomyParameterData = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>(),
                    m_CityModifiers = EntityManager.GetBuffer<CityModifier>(m_CitySystem.City, true),
                    m_CurrentDistricts = currentDistricts,
                    m_Owners = owners,
                    m_AreaOf = areaOf,
                    m_Outside = outside,
                    m_Weights = weights,
                    m_ServiceColumn = serviceColumn,
                    m_ServiceWeights = serviceWeights,
                    m_ColumnCount = columnCount,
                    m_Overrides = overrides,
                    m_UseOverrides = useOverrides,
                };
                handle = upkeepJob.Schedule(m_UpkeepQuery, handle);

                var consumerJob = new ConsumerJob
                {
                    m_EntityType = GetEntityTypeHandle(),
                    m_ElectricityType = GetComponentTypeHandle<ElectricityConsumer>(true),
                    m_WaterType = GetComponentTypeHandle<WaterConsumer>(true),
                    m_GarbageType = GetComponentTypeHandle<GarbageProducer>(true),
                    m_PatientType = GetBufferTypeHandle<Patient>(true),
                    m_StudentType = GetBufferTypeHandle<Game.Buildings.Student>(true),
                    m_CurrentDistricts = currentDistricts,
                    m_Owners = owners,
                    m_AreaOf = areaOf,
                    m_Outside = outside,
                    m_Weights = weights,
                };
                handle = consumerJob.Schedule(m_ConsumerQuery, handle);

                var parkingJob = new ParkingJob
                {
                    m_ParkedCarType = GetComponentTypeHandle<ParkedCar>(true),
                    m_ParkingLanes = GetComponentLookup<Game.Net.ParkingLane>(true),
                    m_BorderDistricts = GetComponentLookup<BorderDistrict>(true),
                    m_CurrentDistricts = currentDistricts,
                    m_Owners = owners,
                    m_AreaOf = areaOf,
                    m_Outside = outside,
                    m_Weights = weights,
                };
                handle = parkingJob.Schedule(m_ParkedCarQuery, handle);

                m_TaxSystem.AddReader(handle);
                m_ResourceSystem.AddPrefabsReader(handle);
                // Completed at once: one single-threaded pass per 1/32 month, and the sample is stored this frame.
                handle.Complete();
                m_ServiceBudgetStore.AddOverridesReader(handle);

                int networkUpkeep = ReadNetworkUpkeep();
                double[] vanilla = ReadVanilla(networkUpkeep);
                double[] weightsCopy = weights.ToArray();
                double[] atCity = atCityRate.ToArray();
                float[][] samples = DistrictBudgetMath.Samples(weightsCopy, areaCount, vanilla, atCity);

                var current = new HashSet<Entity>();
                m_LatestAreas.Clear();
                for (int a = 0; a < areaCount; a++)
                {
                    Entity area = a < districts.Length ? districts[a] : Entity.Null;
                    current.Add(area);
                    m_LatestAreas[area] = a;
                    if (!m_Histories.TryGetValue(area, out DistrictHistory history))
                        m_Histories[area] = history = new DistrictHistory();
                    history.Add(samples[a]);

                    var areaWeights = new float[columnCount];
                    for (int c = 0; c < columnCount; c++) areaWeights[c] = (float)serviceWeights[a * columnCount + c];
                    if (!m_ServiceWeights.TryGetValue(area, out ServiceWeightHistory perService))
                        m_ServiceWeights[area] = perService = new ServiceWeightHistory();
                    // Tags only order a district's samples; they line up with its history by position (newest last).
                    perService.Add(perService.Count > 0 ? perService.TagAt(perService.Count - 1) + 1 : 0, areaWeights);
                }
                // Districts gone since the last sample.
                var gone = new List<Entity>();
                foreach (Entity key in m_Histories.Keys)
                    if (!current.Contains(key)) gone.Add(key);
                foreach (Entity key in gone)
                {
                    m_Histories.Remove(key);
                    m_ServiceWeights.Remove(key);
                }

                m_LatestWeights = weightsCopy;
                System.Array.Copy(atCity, m_LatestAtCityRate, atCity.Length);
                m_LatestNetworkUpkeep = networkUpkeep;

                if (LogSamples) Log(districts.Length, samples, vanilla, atCity, networkUpkeep);
            }
            finally
            {
                districts.Dispose();
                areaOf.Dispose();
                weights.Dispose();
                atCityRate.Dispose();
                serviceColumn.Dispose();
                serviceWeights.Dispose();
                rates.Dispose(default);
                wages.Dispose();
            }
        }

        /// <summary>The service upkeep of networks (roads, pipes, ...): CityServiceBudgetSystem's per-service base cost.</summary>
        private int ReadNetworkUpkeep()
        {
            int total = 0;
            using (NativeArray<CollectedCityServiceBudgetData> budgets = m_ServiceQuery.ToComponentDataArray<CollectedCityServiceBudgetData>(Allocator.Temp))
                for (int i = 0; i < budgets.Length; i++) total += budgets[i].m_BaseCost;
            return total;
        }

        /// <summary>The vanilla Budget row per sample slot (expenses positive); upkeep less the network part.</summary>
        private double[] ReadVanilla(int networkUpkeep)
        {
            var vanilla = new double[S];
            int[] income = DistrictBudgetMath.DistrictIncomeSources;
            int[] expense = DistrictBudgetMath.DistrictExpenseSources;
            for (int i = 0; i < income.Length; i++) vanilla[i] = m_BudgetSystem.GetIncome((IncomeSource)income[i]);
            for (int i = 0; i < expense.Length; i++)
                vanilla[income.Length + i] = System.Math.Abs(m_BudgetSystem.GetExpense((ExpenseSource)expense[i]));
            vanilla[kSlotUpkeep] = System.Math.Max(0, vanilla[kSlotUpkeep] - networkUpkeep);
            return vanilla;
        }

        private void Log(int districtCount, float[][] samples, double[] vanilla, double[] atCity, int networkUpkeep)
        {
            var line = new StringBuilder();
            int count = m_Histories.TryGetValue(Entity.Null, out DistrictHistory outside) ? outside.Count : 0;
            line.Append($"District budget sample #{count}: {districtCount} districts + outside; tax scale");
            for (int slot = 0; slot < S; slot++)
            {
                int row = DistrictBudgetMath.TaxRowOf(slot);
                if (row >= 0) line.Append($" {slot}={DistrictBudgetMath.TaxScale(vanilla[slot], atCity[row]):0.###}");
            }
            line.Append($"; network upkeep {networkUpkeep} (city-only); sum/vanilla");
            for (int slot = 0; slot < S; slot++)
            {
                if (DistrictBudgetMath.TaxRowOf(slot) >= 0) continue;
                double sum = 0;
                for (int a = 0; a < samples.Length; a++) sum += samples[a][slot];
                line.Append($" {slot}={sum:0}/{vanilla[slot]:0}");
            }
            line.Append("; taxes sum");
            for (int slot = 0; slot < S; slot++)
            {
                if (DistrictBudgetMath.TaxRowOf(slot) < 0) continue;
                double sum = 0;
                for (int a = 0; a < samples.Length; a++) sum += samples[a][slot];
                line.Append($" {slot}={sum:0}/{vanilla[slot]:0}");
            }
            Mod.log.Info(line.ToString());
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var save = new SaveWriter<TWriter>(writer);
            ServiceUpkeepSave.WriteDistricts<Entity>(save, save.WriteEntity, m_ServiceColumns, new List<KeyValuePair<Entity, DistrictHistory>>(m_Histories),
                district => m_ServiceWeights.TryGetValue(district, out ServiceWeightHistory weights) ? weights : null);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            ClearAll();
            var save = new SaveReader<TReader>(reader);
            // A format this build doesn't know starts empty; format 1 (before the per-service weights) loads without them.
            ServiceUpkeepSave.ReadDistricts<Entity>(save, save.ReadEntity, m_ServiceColumns, (district, history, weights) =>
            {
                // A district deleted before the save may come back as Entity.Null; outside's own entry wins.
                if (m_Histories.ContainsKey(district)) return;
                m_Histories[district] = history;
                if (weights != null) m_ServiceWeights[district] = weights;
            });
        }

        public void SetDefaults(Context context) => ClearAll();

        private void ClearAll()
        {
            m_Histories.Clear();
            m_ServiceWeights.Clear();
            m_ServiceColumns.Clear();
            m_LatestAreas.Clear();
            m_LatestWeights = new double[0];
            System.Array.Clear(m_LatestAtCityRate, 0, m_LatestAtCityRate.Length);
            m_LatestNetworkUpkeep = 0;
        }

        /// <summary>The area index of a district entity: its index, or outside for none / an unknown one.</summary>
        private static int AreaOf(Entity district, NativeHashMap<Entity, int> areaOf, int outside)
        {
            return district != Entity.Null && areaOf.TryGetValue(district, out int area) ? area : outside;
        }

        /// <summary>The district of a building, or of its owner (an upgrade, a sub-building), up the owner chain.</summary>
        private static int AreaOfBuilding(Entity entity, ref ComponentLookup<CurrentDistrict> currentDistricts, ref ComponentLookup<Owner> owners, NativeHashMap<Entity, int> areaOf, int outside)
        {
            return AreaOf(DistrictOfBuilding(entity, ref currentDistricts, ref owners), areaOf, outside);
        }

        /// <summary>The district entity of a building or of its owner, up the owner chain; Entity.Null for none.</summary>
        private static Entity DistrictOfBuilding(Entity entity, ref ComponentLookup<CurrentDistrict> currentDistricts, ref ComponentLookup<Owner> owners)
        {
            for (int depth = 0; depth < 8 && entity != Entity.Null; depth++)
            {
                if (currentDistricts.TryGetComponent(entity, out CurrentDistrict current)) return current.m_District;
                if (!owners.TryGetComponent(entity, out Owner owner)) break;
                entity = owner.m_Owner;
            }
            return Entity.Null;
        }

        /// <summary>Tax payers: paid sums per tax area (grouped as TaxSystem), restated at the city rate, and residents.</summary>
        [BurstCompile]
        private struct TaxJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public ComponentTypeHandle<Household> m_HouseholdType;
            [ReadOnly] public ComponentTypeHandle<ServiceAvailable> m_ServiceAvailableType;
            [ReadOnly] public ComponentTypeHandle<Game.Companies.ProcessingCompany> m_ProcessingType;
            [ReadOnly] public ComponentTypeHandle<Game.Companies.StorageCompany> m_StorageType;
            [ReadOnly] public BufferTypeHandle<HouseholdCitizen> m_HouseholdCitizenType;
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentLookup<Worker> m_Workers;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefs;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> m_ProcessDatas;
            [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
            [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;
            [ReadOnly] public NativeArray<int> m_TaxRates;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public BufferLookup<DistrictModifier> m_DistrictModifiers;
            [ReadOnly] public NativeHashMap<Entity, int> m_AreaOf;
            public int m_Outside;
            public NativeArray<double> m_Weights;
            public NativeArray<double> m_AtCityRate;
            /// <summary>The tax engine's rates; read only while it charges (<see cref="m_EngineCharges"/>).</summary>
            public TaxRateResolver m_Rates;
            public bool m_EngineCharges;
            [ReadOnly] public NativeParallelHashMap<Entity, int2> m_Records;
            public int m_NowTick;
            public int2 m_JobLevelLimits;
            public int2 m_ResourceLimits;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<TaxPayer> taxPayers = chunk.GetNativeArray(ref m_TaxPayerType);
                NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref m_RenterType);
                bool isHousehold = chunk.Has(ref m_HouseholdType);
                bool isCommercial = chunk.Has(ref m_ServiceAvailableType);
                bool isIndustrial = !isCommercial && chunk.Has(ref m_ProcessingType) && !chunk.Has(ref m_StorageType);
                if (!isHousehold && !isCommercial && !isIndustrial) return;
                BufferAccessor<HouseholdCitizen> citizens = chunk.GetBufferAccessor(ref m_HouseholdCitizenType);

                for (int i = 0; i < taxPayers.Length; i++)
                {
                    Entity district = DistrictOfBuilding(renters[i].m_Property, ref m_CurrentDistricts, ref m_Owners);
                    int area = AreaOf(district, m_AreaOf, m_Outside);
                    int baseIndex = area * S;
                    int row; // 0 residential, 1 commercial, 2 industrial, 3 office
                    int cityRate;
                    // The rate this payer's district charges now. Only Low Commercial Tax makes it differ from the city
                    // rate; the payer's own stored average is not used (it blends job levels, lags rate changes, and
                    // vanilla blends office companies toward the industrial rate), so every other row restates 1:1.
                    int districtRate;
                    if (isHousehold)
                    {
                        row = 0;
                        int jobLevel = 0;
                        if (citizens.Length != 0)
                        {
                            DynamicBuffer<HouseholdCitizen> members = citizens[i];
                            m_Weights[baseIndex + kSlotPublicTransport] += members.Length;
                            for (int c = 0; c < members.Length; c++)
                            {
                                if (m_Workers.TryGetComponent(members[c].m_Citizen, out Worker worker))
                                {
                                    jobLevel = worker.m_Level;
                                    break;
                                }
                            }
                        }
                        cityRate = TaxSystem.GetResidentialTaxRate(jobLevel, m_TaxRates);
                        districtRate = cityRate;
                    }
                    else
                    {
                        Resource output = Resource.NoResource;
                        if (m_PrefabRefs.TryGetComponent(entities[i], out PrefabRef prefab) && m_ProcessDatas.TryGetComponent(prefab.m_Prefab, out IndustrialProcessData process))
                            output = process.m_Output.m_Resource;
                        if (isCommercial)
                        {
                            row = 1;
                            cityRate = output != Resource.NoResource ? TaxSystem.GetCommercialTaxRate(output, m_TaxRates) : TaxSystem.GetTaxRate(TaxAreaType.Commercial, m_TaxRates);
                            districtRate = output != Resource.NoResource
                                ? TaxSystem.GetModifiedCommercialTaxRate(output, m_TaxRates, district, m_DistrictModifiers)
                                : TaxSystem.GetModifiedTaxRate(TaxAreaType.Commercial, m_TaxRates, district, m_DistrictModifiers);
                        }
                        else if (output != Resource.NoResource && m_ResourceDatas.TryGetComponent(m_ResourcePrefabs[output], out ResourceData data) && data.m_Weight == 0f)
                        {
                            // As TaxSystem.PayTax: a processing company whose output weighs nothing pays office tax.
                            row = 3;
                            cityRate = TaxSystem.GetOfficeTaxRate(output, m_TaxRates);
                            districtRate = cityRate;
                        }
                        else
                        {
                            row = 2;
                            cityRate = output != Resource.NoResource ? TaxSystem.GetIndustrialTaxRate(output, m_TaxRates) : TaxSystem.GetTaxRate(TaxAreaType.Industrial, m_TaxRates);
                            districtRate = cityRate;
                        }
                    }

                    TaxPayer payer = taxPayers[i];
                    int paid = payer.m_AverageTaxPaid;
                    int2 charge = default;
                    bool hasRecord = m_EngineCharges && m_Records.TryGetValue(entities[i], out charge);
                    if (!DistrictBudgetMath.PayerCounts(m_EngineCharges, hasRecord, charge.y, m_NowTick)) continue;
                    if (paid == 0) continue;
                    // The rates the tax engine charged it at (its district's and density rates), as TaxEngineSystem's
                    // charge (untaxed income taken as positive: a payment that paid something had some).
                    int stored = payer.m_AverageTaxRate, withoutDensity = stored, charged = stored;
                    if (m_EngineCharges)
                    {
                        TaxPayerKind kind = isHousehold ? TaxPayerKind.Household : isCommercial ? TaxPayerKind.Commercial : TaxPayerKind.Industrial;
                        int2 limits = isHousehold ? m_JobLevelLimits : m_ResourceLimits;
                        m_Rates.Adjustments(entities[i], renters[i].m_Property, kind, out int incomeDelta, out int densityTax);
                        withoutDensity = EffectiveTaxRate.Compute(stored, 0, incomeDelta, limits.x, limits.y, 1);
                        charged = EffectiveTaxRate.Compute(stored, densityTax, incomeDelta, limits.x, limits.y, 1);
                    }
                    // A subsidy is kept as a positive amount like the vanilla expense row.
                    DistrictBudgetMath.PayerTaxWeights(paid, districtRate, cityRate, stored, withoutDensity, charged,
                        out double taxWeight, out double taxAtCity, out double subsidyWeight, out double subsidyAtCity);
                    m_Weights[baseIndex + row] += taxWeight;
                    m_AtCityRate[row] += taxAtCity;
                    m_Weights[baseIndex + kSlotSubsidyStart + row] += subsidyWeight;
                    m_AtCityRate[4 + row] += subsidyAtCity;
                }
            }
        }

        /// <summary>Service building upkeep, as CityServiceBudgetSystem's building job.</summary>
        [BurstCompile]
        private struct UpkeepJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> m_PrefabType;
            [ReadOnly] public ComponentTypeHandle<Building> m_BuildingType;
            [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
            [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
            [ReadOnly] public ComponentLookup<ServiceObjectData> m_ServiceObjectDatas;
            [ReadOnly] public BufferLookup<ServiceUpkeepData> m_ServiceUpkeepDatas;
            [ReadOnly] public ComponentLookup<ServiceUsage> m_ServiceUsages;
            [ReadOnly] public BufferLookup<InstalledUpgrade> m_InstalledUpgradeBufs;
            [ReadOnly] public BufferLookup<Employee> m_EmployeeBufs;
            [ReadOnly] public DynamicBuffer<ServiceBudgetData> m_ServiceBudgets;
            [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;
            [ReadOnly] public EconomyParameterData m_EconomyParameterData;
            [ReadOnly] public DynamicBuffer<CityModifier> m_CityModifiers;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public NativeHashMap<Entity, int> m_AreaOf;
            public int m_Outside;
            public NativeArray<double> m_Weights;
            [ReadOnly] public NativeHashMap<Entity, int> m_ServiceColumn;
            public NativeArray<double> m_ServiceWeights;
            public int m_ColumnCount;
            // Per-district service budgets: the override map, used only while the feature applies (else the city %).
            [ReadOnly] public NativeHashMap<ServiceDistrictKey, int> m_Overrides;
            public bool m_UseOverrides;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabType);
                NativeArray<Building> buildings = chunk.GetNativeArray(ref m_BuildingType);
                bool hasBuilding = buildings.Length != 0;
                var upkeeps = new NativeList<ServiceUpkeepData>(4, Allocator.Temp);
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = entities[i];
                    Entity prefab = prefabs[i].m_Prefab;
                    if (!m_ServiceObjectDatas.TryGetComponent(prefab, out ServiceObjectData serviceObject)) continue;
                    if (!m_ServiceUpkeepDatas.HasBuffer(prefab)) continue;
                    int cityBudget = GetServiceBudget(serviceObject.m_Service);
                    int budget = ServiceUpkeepCost.EffectiveBudget(m_Overrides, m_UseOverrides, serviceObject.m_Service,
                        ServiceUpkeepCost.DistrictOf(entity, ref m_CurrentDistricts, ref m_Owners), cityBudget);
                    bool inactive = hasBuilding && BuildingUtils.CheckOption(buildings[i], BuildingOption.Inactive);
                    upkeeps.Clear();
                    CityServiceUpkeepSystem.GetUpkeepWithUsageScale(upkeeps, m_ServiceUpkeepDatas, m_InstalledUpgradeBufs, m_Prefabs, m_ServiceUsages, entity, prefab, inactive);
                    int wages = CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage(m_EmployeeBufs, entity, m_EconomyParameterData, inactive);
                    double total = 0;
                    for (int j = 0; j < upkeeps.Length; j++)
                    {
                        ServiceUpkeepData upkeep = upkeeps[j];
                        if (upkeep.m_Upkeep.m_Amount <= 0) continue;
                        float value = upkeep.m_Upkeep.m_Amount * EconomyUtils.GetMarketPrice(upkeep.m_Upkeep.m_Resource, m_ResourcePrefabs, ref m_ResourceDatas);
                        if (upkeep.m_Upkeep.m_Resource == Resource.Money)
                        {
                            CityUtils.ApplyModifier(ref value, m_CityModifiers, CityModifierType.CityServiceBuildingBaseUpkeepCost);
                            value += wages;
                            if (inactive) value *= 0.1f;
                            value *= budget / 100f;
                        }
                        total += value;
                    }
                    if (total <= 0) continue;
                    int area = AreaOfBuilding(entity, ref m_CurrentDistricts, ref m_Owners, m_AreaOf, m_Outside);
                    m_Weights[area * S + kSlotUpkeep] += total;
                    if (m_ServiceColumn.TryGetValue(serviceObject.m_Service, out int column))
                        m_ServiceWeights[area * m_ColumnCount + column] += total;
                }
                upkeeps.Dispose();
            }

            private int GetServiceBudget(Entity service)
            {
                for (int i = 0; i < m_ServiceBudgets.Length; i++)
                    if (m_ServiceBudgets[i].m_Service == service) return m_ServiceBudgets[i].m_Budget;
                return 100;
            }
        }

        /// <summary>Per building: electricity and water use, garbage, patients and students.</summary>
        [BurstCompile]
        private struct ConsumerJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<ElectricityConsumer> m_ElectricityType;
            [ReadOnly] public ComponentTypeHandle<WaterConsumer> m_WaterType;
            [ReadOnly] public ComponentTypeHandle<GarbageProducer> m_GarbageType;
            [ReadOnly] public BufferTypeHandle<Patient> m_PatientType;
            [ReadOnly] public BufferTypeHandle<Game.Buildings.Student> m_StudentType;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public NativeHashMap<Entity, int> m_AreaOf;
            public int m_Outside;
            public NativeArray<double> m_Weights;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<ElectricityConsumer> electricity = chunk.GetNativeArray(ref m_ElectricityType);
                NativeArray<WaterConsumer> water = chunk.GetNativeArray(ref m_WaterType);
                NativeArray<GarbageProducer> garbage = chunk.GetNativeArray(ref m_GarbageType);
                BufferAccessor<Patient> patients = chunk.GetBufferAccessor(ref m_PatientType);
                BufferAccessor<Game.Buildings.Student> students = chunk.GetBufferAccessor(ref m_StudentType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    int baseIndex = AreaOfBuilding(entities[i], ref m_CurrentDistricts, ref m_Owners, m_AreaOf, m_Outside) * S;
                    if (electricity.Length != 0) m_Weights[baseIndex + kSlotElectricity] += electricity[i].m_FulfilledConsumption;
                    if (water.Length != 0) m_Weights[baseIndex + kSlotWater] += water[i].m_FulfilledFresh + water[i].m_FulfilledSewage;
                    if (garbage.Length != 0) m_Weights[baseIndex + kSlotGarbage] += garbage[i].m_Garbage;
                    if (patients.Length != 0) m_Weights[baseIndex + kSlotHealthcare] += patients[i].Length;
                    if (students.Length != 0) m_Weights[baseIndex + kSlotEducation] += students[i].Length;
                }
            }
        }

        /// <summary>Parked cars x their lane's fee, by the lane's district (as ParkingLaneDataSystem picks the fee).</summary>
        [BurstCompile]
        private struct ParkingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ParkedCar> m_ParkedCarType;
            [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> m_ParkingLanes;
            [ReadOnly] public ComponentLookup<BorderDistrict> m_BorderDistricts;
            [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
            [ReadOnly] public ComponentLookup<Owner> m_Owners;
            [ReadOnly] public NativeHashMap<Entity, int> m_AreaOf;
            public int m_Outside;
            public NativeArray<double> m_Weights;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<ParkedCar> parked = chunk.GetNativeArray(ref m_ParkedCarType);
                for (int i = 0; i < parked.Length; i++)
                {
                    Entity lane = parked[i].m_Lane;
                    if (!m_ParkingLanes.TryGetComponent(lane, out Game.Net.ParkingLane parkingLane) || parkingLane.m_ParkingFee == 0) continue;
                    int area = m_Outside;
                    if (m_Owners.TryGetComponent(lane, out Owner owner))
                    {
                        if (m_BorderDistricts.TryGetComponent(owner.m_Owner, out BorderDistrict border))
                        {
                            Entity district = (parkingLane.m_Flags & Game.Net.ParkingLaneFlags.RightSide) != 0 ? border.m_Right : border.m_Left;
                            area = AreaOf(district, m_AreaOf, m_Outside);
                        }
                        else
                        {
                            // The top owner (a building with its own lot or garage).
                            Entity top = owner.m_Owner;
                            for (int depth = 0; depth < 8 && m_Owners.TryGetComponent(top, out Owner up); depth++) top = up.m_Owner;
                            if (m_CurrentDistricts.TryGetComponent(top, out CurrentDistrict current)) area = AreaOf(current.m_District, m_AreaOf, m_Outside);
                        }
                    }
                    m_Weights[area * S + kSlotParking] += parkingLane.m_ParkingFee;
                }
            }
        }
    }
}
