using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Prefabs.Modes;
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
    /// The tax engine: charges every tax payer in place of vanilla TaxSystem's update, at the payer's vanilla rate plus
    /// the city's adjustments (<see cref="TaxRateTable"/>: density, district), kept inside the vanilla limits
    /// (<see cref="EffectiveTaxRate"/>).
    ///
    /// Vanilla TaxSystem stays the data store (its rate array, saved by vanilla and read by the other systems and the
    /// UI); only its update is replaced: while the engine runs, <c>TaxSystem.Enabled</c> is false (that skips its
    /// OnUpdate and nothing else) and this system, registered right before it with the same interval so it inherits its
    /// update offset, charges the same payers on the same frames. With every adjustment 0 the charge is vanilla's.
    ///
    /// <see cref="TaxEngineGuardLogic"/> decides each update whether the engine runs: the "Simulation changes" switch
    /// (<see cref="Setting.EnableSimulationChanges"/>), the game version (<see cref="TaxEngineGuardLogic.KnownGameVersions"/>,
    /// or <see cref="Setting.RunOnUntestedGameVersion"/>) and <see cref="TaxEngineGuard.StructureOk"/>. Anything but
    /// Engine turns vanilla's update back on, live. If vanilla TaxSystem was already disabled by someone else (another
    /// mod) the engine backs off for the session and never touches it.
    /// </summary>
    public partial class TaxEngineSystem : GameSystemBase
    {
        private Setting m_Setting;
        private TaxSystem m_TaxSystem;
        private SimulationSystem m_SimulationSystem;
        private CityStatisticsSystem m_CityStatisticsSystem;
        private ResourceSystem m_ResourceSystem;

        private EntityQuery m_ResidentialTaxPayerGroup;
        private EntityQuery m_CommercialTaxPayerGroup;
        private EntityQuery m_IndustrialTaxPayerGroup;
        private EntityQuery m_TaxParameterGroup;
        private EntityQuery m_GameModeSettingQuery;
        private float3 m_TaxPaidMultiplier;

        private string m_GameVersion;
        private bool m_VersionKnown;
        private bool m_StructureOk;
        private bool m_Ready;
        private bool m_WeDisabledVanilla;
        private bool m_ForeignDisabled;
        private bool m_Logged;

        /// <summary>What charges tax right now.</summary>
        public TaxEngineMode Mode { get; private set; } = TaxEngineMode.Vanilla;

        /// <summary>What the Taxation tab warns about, if anything.</summary>
        public TaxEngineWarning Warning { get; private set; } = TaxEngineWarning.None;

        public void SetSettings(Setting setting) => m_Setting = setting;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Same as TaxSystem: 262144 / (kUpdatesPerDay * 16), so this inherits its update offset.
            return 262144 / (TaxSystem.kUpdatesPerDay * 16);
        }

        // Queries: copied from Game.Simulation.TaxSystem.OnCreate (TaxSystem.cs:333-350), game version 1.6.0f1.
        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_ResidentialTaxPayerGroup = GetEntityQuery(ComponentType.ReadWrite<TaxPayer>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.ReadWrite<Resources>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.ReadOnly<Household>());
            m_CommercialTaxPayerGroup = GetEntityQuery(ComponentType.ReadWrite<TaxPayer>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.ReadWrite<Resources>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.ReadOnly<ServiceAvailable>());
            m_IndustrialTaxPayerGroup = GetEntityQuery(ComponentType.ReadWrite<TaxPayer>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.ReadWrite<Resources>(), ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Game.Companies.StorageCompany>(), ComponentType.Exclude<ServiceAvailable>());
            m_TaxParameterGroup = GetEntityQuery(ComponentType.ReadOnly<TaxParameterData>());
            m_GameModeSettingQuery = GetEntityQuery(ComponentType.ReadOnly<ModeSettingData>());
            m_TaxPaidMultiplier = new float3(1f, 1f, 1f);
            RequireForUpdate(m_TaxParameterGroup);

            m_GameVersion = TaxEngineGuard.GameVersion();
            m_VersionKnown = TaxEngineGuardLogic.IsKnownVersion(m_GameVersion, TaxEngineGuardLogic.KnownGameVersions);
            m_StructureOk = TaxEngineGuard.StructureOk(m_TaxSystem, out string problem);
            Mod.log.Info($"TaxEngine: game version '{m_GameVersion}' (known: {m_VersionKnown}, known list: {string.Join(", ", TaxEngineGuardLogic.KnownGameVersions)}); structure {(m_StructureOk ? "ok" : "MISMATCH: " + problem)}");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            // Leave vanilla charging tax.
            if (m_WeDisabledVanilla && m_TaxSystem != null) m_TaxSystem.Enabled = true;
            m_WeDisabledVanilla = false;
            base.OnDestroy();
        }

        // Copied from Game.Simulation.TaxSystem.OnGameLoaded (TaxSystem.cs:242-258), game version 1.6.0f1.
        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            if (m_GameModeSettingQuery.IsEmptyIgnoreFilter)
            {
                m_TaxPaidMultiplier = new float3(1f, 1f, 1f);
                return;
            }
            ModeSettingData singleton = m_GameModeSettingQuery.GetSingleton<ModeSettingData>();
            m_TaxPaidMultiplier = singleton.m_Enable ? singleton.m_TaxPaidMultiplier : new float3(1f, 1f, 1f);
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // Decided only once a city is loading, after every mod's OnLoad: a mod that disables TaxSystem itself has
            // done so by now, and the engine backs off.
            m_Ready = true;
            ApplyMode();
        }

        /// <summary>
        /// Decides who charges tax (<see cref="TaxEngineGuardLogic.Decide(bool, bool, bool, bool)"/>) and turns vanilla
        /// TaxSystem's update off or back on to match. Called every engine update and whenever Options change.
        /// </summary>
        public void ApplyMode()
        {
            if (!m_Ready || m_TaxSystem == null) return;
            bool master = m_Setting?.EnableSimulationChanges ?? FeatureGates.SimulationChangesDefault;
            bool force = m_Setting?.RunOnUntestedGameVersion ?? FeatureGates.RunOnUntestedGameVersionDefault;

            if (!m_WeDisabledVanilla && !m_TaxSystem.Enabled && !m_ForeignDisabled)
            {
                m_ForeignDisabled = true;
                Mod.log.Warn("TaxEngine: vanilla TaxSystem was already disabled (another mod?); the tax engine stays off and leaves it alone");
            }
            bool structureOk = m_StructureOk && !m_ForeignDisabled;

            TaxEngineMode mode = TaxEngineGuardLogic.Decide(m_VersionKnown, structureOk, force, master);
            Warning = TaxEngineGuardLogic.WarningFor(m_VersionKnown, structureOk, force, master);

            if (!m_ForeignDisabled)
            {
                if (mode == TaxEngineMode.Engine && !m_WeDisabledVanilla)
                {
                    m_TaxSystem.Enabled = false;
                    m_WeDisabledVanilla = true;
                }
                else if (mode != TaxEngineMode.Engine && m_WeDisabledVanilla)
                {
                    m_TaxSystem.Enabled = true;
                    m_WeDisabledVanilla = false;
                }
            }

            if (mode != Mode || !m_Logged)
            {
                Mod.log.Info($"TaxEngine: decision {mode} (simulation changes {(master ? "on" : "off")}, version '{m_GameVersion}' known {m_VersionKnown}, run on untested {force}, structure ok {structureOk}); vanilla TaxSystem {(m_TaxSystem.Enabled ? "enabled" : "disabled")}");
                m_Logged = true;
            }
            Mode = mode;
        }

        // Copied from Game.Simulation.TaxSystem.OnUpdate (TaxSystem.cs:373-436), game version 1.6.0f1. Changes: the
        // mode check first; vanilla's private fields read through GetTaxParameterData(); the adjustments, limits and
        // key lookups passed to the job; the three jobs built by one helper.
        [Preserve]
        protected override void OnUpdate()
        {
            ApplyMode();
            if (Mode != TaxEngineMode.Engine) return;

            // Runs vanilla's EnsureTaxParameterData (its rate setters clamp with the same data).
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, TaxSystem.kUpdatesPerDay, 16);
            ResourcePrefabs prefabs = m_ResourceSystem.GetPrefabs();

            // The jobs get their own copies of the adjustments, freed with them: the main thread can change the table
            // (a slider, Options) while they run.
            var density = new NativeArray<int>(DensityTaxState.Snapshot(), Allocator.TempJob);
            NativeParallelHashMap<Entity, int4> districts = BuildDistrictAdjustments();

            JobHandle residential = Schedule(m_ResidentialTaxPayerGroup, IncomeSource.TaxResidential, m_TaxPaidMultiplier.x, parameters.m_JobLevelTaxLimits, updateFrame, prefabs, density, districts);
            JobHandle commercial = Schedule(m_CommercialTaxPayerGroup, IncomeSource.TaxCommercial, m_TaxPaidMultiplier.y, parameters.m_ResourceTaxLimits, updateFrame, prefabs, density, districts);
            JobHandle industrial = Schedule(m_IndustrialTaxPayerGroup, IncomeSource.TaxIndustrial, m_TaxPaidMultiplier.z, parameters.m_ResourceTaxLimits, updateFrame, prefabs, density, districts);
            Dependency = JobHandle.CombineDependencies(residential, commercial, industrial);
            density.Dispose(Dependency);
            districts.Dispose(Dependency);
        }

        private JobHandle Schedule(EntityQuery query, IncomeSource type, float paidMultiplier, int2 rateLimits, uint updateFrame, ResourcePrefabs prefabs, NativeArray<int> density, NativeParallelHashMap<Entity, int4> districts)
        {
            var job = new PayTaxJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_TaxPayerType = GetComponentTypeHandle<TaxPayer>(false),
                m_UpdateFrameType = GetSharedComponentTypeHandle<UpdateFrame>(),
                m_ResourceType = GetBufferTypeHandle<Resources>(false),
                m_RenterType = GetComponentTypeHandle<PropertyRenter>(true),
                m_HouseholdCitizens = GetBufferLookup<HouseholdCitizen>(true),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_ProcessDatas = GetComponentLookup<IndustrialProcessData>(true),
                m_Workers = GetComponentLookup<Worker>(true),
                m_ResourceDatas = GetComponentLookup<ResourceData>(true),
                m_ResourcePrefabs = prefabs,
                m_Type = type,
                m_UpdateFrameIndex = updateFrame,
                m_PaidMultiplier = paidMultiplier,
                m_RateLimits = rateLimits,
                m_DensityAdjustments = density,
                m_DistrictAdjustments = districts,
                m_Keys = TaxKeyResolver.Create(this),
                m_StatisticsEventQueue = m_CityStatisticsSystem.GetStatisticsEventQueue(out JobHandle deps).AsParallelWriter(),
            };
            JobHandle handle = job.ScheduleParallel(query, JobHandle.CombineDependencies(Dependency, deps));
            m_CityStatisticsSystem.AddWriter(handle);
            return handle;
        }

        /// <summary>The district adjustments by district entity: x..w = Residential, Commercial, Industrial, Office (<see cref="TaxZone"/>).</summary>
        private static NativeParallelHashMap<Entity, int4> BuildDistrictAdjustments()
        {
            var entries = DensityTaxState.Table.Rates.Entries();
            var map = new NativeParallelHashMap<Entity, int4>(math.max(1, entries.Count), Allocator.TempJob);
            foreach (TaxRateEntry e in entries)
            {
                if (e.Type != TaxType.District || e.Zone < 0 || e.Zone >= DensityTaxTable.ZoneCount) continue;
                var district = new Entity { Index = e.Key, Version = e.KeyVersion };
                map.TryGetValue(district, out int4 adjustments);
                adjustments[e.Zone] = e.Value;
                map[district] = adjustments;
            }
            return map;
        }

        /// <summary>
        /// Copied from Game.Simulation.TaxSystem.PayTaxJob (TaxSystem.cs:30-150), game version 1.6.0f1. Changes: the
        /// tax is charged at <see cref="EffectiveTaxRate.Compute"/>'s rate (the payer's vanilla rate plus its density
        /// and district adjustments) instead of the payer's stored rate, which is left as it is; everything else
        /// (paid multiplier, statistics, office detection, zeroing the untaxed income, the average paid) is vanilla's.
        /// </summary>
        [BurstCompile]
        private struct PayTaxJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            public ComponentTypeHandle<TaxPayer> m_TaxPayerType;
            [ReadOnly] public SharedComponentTypeHandle<UpdateFrame> m_UpdateFrameType;
            public BufferTypeHandle<Resources> m_ResourceType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> m_RenterType;
            [ReadOnly] public BufferLookup<HouseholdCitizen> m_HouseholdCitizens;
            [ReadOnly] public ComponentLookup<Worker> m_Workers;
            [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
            [ReadOnly] public ComponentLookup<IndustrialProcessData> m_ProcessDatas;
            [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
            [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;
            public uint m_UpdateFrameIndex;
            public IncomeSource m_Type;
            public float m_PaidMultiplier;
            /// <summary>The vanilla limits for this payer kind's full rate (TaxParameterData).</summary>
            public int2 m_RateLimits;
            [ReadOnly] public NativeArray<int> m_DensityAdjustments;
            [ReadOnly] public NativeParallelHashMap<Entity, int4> m_DistrictAdjustments;
            public TaxKeyResolver m_Keys;
            public NativeQueue<StatisticsEvent>.ParallelWriter m_StatisticsEventQueue;

            /// <summary>Whether an industrial payer's output weighs nothing, so vanilla books its tax as Office (PayTax).</summary>
            private bool IsWeightless(Entity entity)
            {
                if (!m_Prefabs.HasComponent(entity)) return false;
                Entity prefab = m_Prefabs[entity].m_Prefab;
                if (!m_ProcessDatas.HasComponent(prefab)) return false;
                Resource resource = m_ProcessDatas[prefab].m_Output.m_Resource;
                return m_ResourceDatas[m_ResourcePrefabs[resource]].m_Weight == 0f;
            }

            /// <summary>The rate this payer is charged: its vanilla rate plus its adjustments, inside the limits.</summary>
            private int ChargedRate(TaxPayer taxPayer, Entity entity, bool hasProperty, Entity property)
            {
                if (!hasProperty) return taxPayer.m_AverageTaxRate;
                bool isHousehold = m_Type == IncomeSource.TaxResidential;
                int density = 0;
                if (m_Keys.TryDensityCell(property, isHousehold, out int cell)) density = m_DensityAdjustments[cell];
                int district = 0;
                if (!m_DistrictAdjustments.IsEmpty)
                {
                    Entity districtEntity = m_Keys.District(property);
                    if (districtEntity != Entity.Null && m_DistrictAdjustments.TryGetValue(districtEntity, out int4 adjustments))
                    {
                        bool weightless = m_Type == IncomeSource.TaxIndustrial && IsWeightless(entity);
                        TaxZone zone = TaxPayerZone.Of(isHousehold, m_Type == IncomeSource.TaxCommercial, weightless);
                        district = adjustments[(int)zone];
                    }
                }
                return EffectiveTaxRate.Compute(taxPayer.m_AverageTaxRate, density, district, m_RateLimits.x, m_RateLimits.y, taxPayer.m_UntaxedIncome);
            }

            private void PayTax(ref TaxPayer taxPayer, Entity entity, int chargedRate, DynamicBuffer<Resources> resources, IncomeSource taxType, NativeQueue<StatisticsEvent>.ParallelWriter statisticsEventQueue)
            {
                // Vanilla: int tax = GetTax(taxPayer); here at the charged rate. The stored rate is not changed.
                TaxPayer charged = taxPayer;
                charged.m_AverageTaxRate = chargedRate;
                int tax = TaxSystem.GetTax(charged);
                int num = (int)math.round(m_PaidMultiplier * (float)tax);
                EconomyUtils.AddResources(Resource.Money, -num, resources);
                if (taxType == IncomeSource.TaxResidential)
                {
                    int parameter = 0;
                    if (m_HouseholdCitizens.HasBuffer(entity))
                    {
                        DynamicBuffer<HouseholdCitizen> dynamicBuffer = m_HouseholdCitizens[entity];
                        for (int i = 0; i < dynamicBuffer.Length; i++)
                        {
                            Entity citizen = dynamicBuffer[i].m_Citizen;
                            if (m_Workers.HasComponent(citizen))
                            {
                                parameter = m_Workers[citizen].m_Level;
                                break;
                            }
                        }
                    }
                    statisticsEventQueue.Enqueue(new StatisticsEvent
                    {
                        m_Statistic = StatisticType.ResidentialTaxableIncome,
                        m_Change = taxPayer.m_UntaxedIncome * TaxSystem.kUpdatesPerDay,
                        m_Parameter = parameter
                    });
                }
                else
                {
                    int parameter2 = 0;
                    StatisticType statisticType = ((taxType == IncomeSource.TaxCommercial) ? StatisticType.CommercialTaxableIncome : StatisticType.IndustrialTaxableIncome);
                    if (m_Prefabs.HasComponent(entity))
                    {
                        Entity prefab = m_Prefabs[entity].m_Prefab;
                        if (m_ProcessDatas.HasComponent(prefab))
                        {
                            Resource resource = m_ProcessDatas[prefab].m_Output.m_Resource;
                            parameter2 = EconomyUtils.GetResourceIndex(resource);
                            if (statisticType == StatisticType.IndustrialTaxableIncome && m_ResourceDatas[m_ResourcePrefabs[resource]].m_Weight == 0f)
                            {
                                taxType = IncomeSource.TaxOffice;
                                statisticType = StatisticType.OfficeTaxableIncome;
                            }
                        }
                    }
                    statisticsEventQueue.Enqueue(new StatisticsEvent
                    {
                        m_Statistic = statisticType,
                        m_Change = taxPayer.m_UntaxedIncome * TaxSystem.kUpdatesPerDay,
                        m_Parameter = parameter2
                    });
                }
                taxPayer.m_UntaxedIncome = 0;
                taxPayer.m_AverageTaxPaid = tax * TaxSystem.kUpdatesPerDay;
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex)
                {
                    return;
                }
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                NativeArray<TaxPayer> nativeArray2 = chunk.GetNativeArray(ref m_TaxPayerType);
                BufferAccessor<Resources> bufferAccessor = chunk.GetBufferAccessor(ref m_ResourceType);
                bool hasRenter = chunk.Has(ref m_RenterType);
                NativeArray<PropertyRenter> renters = hasRenter ? chunk.GetNativeArray(ref m_RenterType) : default;
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    TaxPayer taxPayer = nativeArray2[i];
                    DynamicBuffer<Resources> resources = bufferAccessor[i];
                    if (taxPayer.m_UntaxedIncome != 0)
                    {
                        Entity property = hasRenter ? renters[i].m_Property : Entity.Null;
                        int rate = ChargedRate(taxPayer, nativeArray[i], hasRenter && property != Entity.Null, property);
                        PayTax(ref taxPayer, nativeArray[i], rate, resources, m_Type, m_StatisticsEventQueue);
                        nativeArray2[i] = taxPayer;
                    }
                }
            }
        }
    }
}
