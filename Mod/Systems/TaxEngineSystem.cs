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
    /// One chunk's charges on one zone's line, per charge (not yet monthly): the sums of their parts beyond vanilla
    /// (<see cref="DensityTaxBooking.Parts"/>; density by sign) and of their tax at the vanilla rate (for the log's check
    /// of the scale against vanilla's estimate).
    /// </summary>
    public struct TaxBookingEvent
    {
        public byte m_Zone;
        /// <summary>The density cell of these charges (<see cref="DensityTaxTable.Index"/>), -1 for none.</summary>
        public sbyte m_Cell;
        /// <summary>Their taxable income (the positive untaxed income charged), per charge.</summary>
        public long m_Base;
        public long m_District;
        public long m_DensityPositive;
        public long m_DensityNegative;
        public long m_Vanilla;
    }

    /// <summary>
    /// The tax engine: charges every tax payer in place of vanilla TaxSystem's update, at the payer's vanilla rate plus
    /// its density tax and its district's own rates (<see cref="TaxRateResolver"/>), kept inside the vanilla limits
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
        private EntityQuery m_EconomyParameterQuery;
        private float3 m_TaxPaidMultiplier;

        private string m_GameVersion;
        private bool m_VersionKnown;
        private bool m_StructureOk;
        private bool m_Ready;
        private bool m_WeDisabledVanilla;
        private bool m_ForeignDisabled;
        private bool m_Logged;

        // The city's side of the charge (TaxBookingSystem books it): each tick's jobs queue every charge that differs
        // from vanilla's; the next update drains the queue into the ledger under that tick's update frame.
        private NativeQueue<TaxBookingEvent> m_BookingQueue;
        private JobHandle m_BookingDeps;
        private int m_BookingFrame = -1;
        private long m_BookingSignature;

        /// <summary>The fingerprint of the rates the last tick charged at (<see cref="RateSignature"/>).</summary>
        public long RateSignatureNow { get; private set; }

        /// <summary>The engine's charges beyond vanilla over the last month, per zone (<see cref="TaxBookingSystem"/>).</summary>
        public TaxBookingLedger Ledger { get; } = new TaxBookingLedger();

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

        // Queries: copied from Game.Simulation.TaxSystem.OnCreate (TaxSystem.cs:333-350), game version 1.6.3f1.
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
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_TaxPaidMultiplier = new float3(1f, 1f, 1f);
            m_BookingQueue = new NativeQueue<TaxBookingEvent>(Allocator.Persistent);
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
            m_BookingDeps.Complete();
            if (m_BookingQueue.IsCreated) m_BookingQueue.Dispose();
            base.OnDestroy();
        }

        // Copied from Game.Simulation.TaxSystem.OnGameLoaded (TaxSystem.cs:242-258), game version 1.6.3f1.
        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            ResetBooking();
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

        // Copied from Game.Simulation.TaxSystem.OnUpdate (TaxSystem.cs:373-436), game version 1.6.3f1. Changes: the
        // mode check first; vanilla's private fields read through GetTaxParameterData(); the adjustments, limits and
        // key lookups passed to the job; the three jobs built by one helper.
        [Preserve]
        protected override void OnUpdate()
        {
            ApplyMode();
            DrainBooking();
            if (Mode != TaxEngineMode.Engine)
            {
                // Vanilla charges: nothing beyond vanilla to book.
                if (!Ledger.IsEmpty) ResetBooking();
                return;
            }

            // Runs vanilla's EnsureTaxParameterData (its rate setters clamp with the same data).
            TaxParameterData parameters = m_TaxSystem.GetTaxParameterData();
            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, TaxSystem.kUpdatesPerDay, 16);
            ResourcePrefabs prefabs = m_ResourceSystem.GetPrefabs();

            // The jobs get their own copies of the rates (the resolver's arrays), freed with them: the main thread can
            // change the table (a slider, Options) while they run.
            NativeArray<int> wages = TaxRateResolver.Wages(m_EconomyParameterQuery, Allocator.TempJob);

            TaxRateResolver rates = TaxRateResolver.Create(this, m_TaxSystem, prefabs, wages, Allocator.TempJob);

            JobHandle residential = Schedule(m_ResidentialTaxPayerGroup, IncomeSource.TaxResidential, m_TaxPaidMultiplier.x, parameters.m_JobLevelTaxLimits, updateFrame, prefabs, rates);
            JobHandle commercial = Schedule(m_CommercialTaxPayerGroup, IncomeSource.TaxCommercial, m_TaxPaidMultiplier.y, parameters.m_ResourceTaxLimits, updateFrame, prefabs, rates);
            JobHandle industrial = Schedule(m_IndustrialTaxPayerGroup, IncomeSource.TaxIndustrial, m_TaxPaidMultiplier.z, parameters.m_ResourceTaxLimits, updateFrame, prefabs, rates);
            Dependency = JobHandle.CombineDependencies(residential, commercial, industrial);
            rates.Dispose(Dependency);
            wages.Dispose(Dependency);
            m_BookingDeps = Dependency;
            m_BookingFrame = (int)updateFrame;
            m_BookingSignature = RateSignature.Of(DensityTaxState.Table.Rates.Entries(), m_TaxSystem.GetTaxRates().ToArray(), DensityTaxState.Enabled);
            RateSignatureNow = m_BookingSignature;
        }

        /// <summary>
        /// Moves the last tick's queued charges into <see cref="Ledger"/> under its update frame (replacing what that frame
        /// booked a month ago). The last tick's jobs finished long since (512 frames), so the wait costs nothing.
        /// </summary>
        private void DrainBooking()
        {
            m_BookingDeps.Complete();
            if (m_BookingFrame < 0)
            {
                m_BookingQueue.Clear();
                return;
            }
            Ledger.BeginFrame(m_BookingFrame, m_BookingSignature);
            while (m_BookingQueue.TryDequeue(out TaxBookingEvent e))
            {
                Ledger.AddSums(m_BookingFrame, (TaxZone)e.m_Zone, e.m_District, e.m_DensityPositive, e.m_DensityNegative, e.m_Vanilla);
                Ledger.AddCell(m_BookingFrame, e.m_Cell, e.m_Base, e.m_DensityPositive + e.m_DensityNegative);
            }
            m_BookingFrame = -1;
        }

        private void ResetBooking()
        {
            m_BookingDeps.Complete();
            if (m_BookingQueue.IsCreated) m_BookingQueue.Clear();
            Ledger.Clear();
            m_BookingFrame = -1;
        }

        private JobHandle Schedule(EntityQuery query, IncomeSource type, float paidMultiplier, int2 rateLimits, uint updateFrame, ResourcePrefabs prefabs, TaxRateResolver rates)
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
                m_Rates = rates,
                m_StatisticsEventQueue = m_CityStatisticsSystem.GetStatisticsEventQueue(out JobHandle deps).AsParallelWriter(),
                m_BookingQueue = m_BookingQueue.AsParallelWriter(),
            };
            // After the previous group's job: they share the booking queue (DrainBooking has completed last tick's).
            JobHandle handle = job.ScheduleParallel(query, JobHandle.CombineDependencies(Dependency, deps, m_BookingDeps));
            m_CityStatisticsSystem.AddWriter(handle);
            m_BookingDeps = handle;
            return handle;
        }
        /// <summary>
        /// Copied from Game.Simulation.TaxSystem.PayTaxJob (TaxSystem.cs:30-150), game version 1.6.3f1. Changes: the
        /// tax is charged at <see cref="TaxRateResolver.ChargedRate"/> (the payer's vanilla rate plus its density tax
        /// and its district's income-tax difference) instead of the payer's stored rate, which is left as it is; everything else
        /// (paid multiplier, statistics, office detection, zeroing the untaxed income, the average paid) is vanilla's.
        /// Added: each charge that differs from vanilla's queues its district and density parts for the city's side
        /// (<see cref="TaxBookingSystem"/>).
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
            /// <summary>The shared effective-rate computation: city values and district overrides.</summary>
            public TaxRateResolver m_Rates;
            public NativeQueue<StatisticsEvent>.ParallelWriter m_StatisticsEventQueue;
            /// <summary>Every charge that differs from vanilla's, for the city's side (<see cref="TaxBookingSystem"/>).</summary>
            public NativeQueue<TaxBookingEvent>.ParallelWriter m_BookingQueue;

            /// <summary>
            /// The rate this payer is charged (as <see cref="TaxRateResolver.ChargedRate"/>: the same Adjustments and clamp)
            /// and its rate without density tax; both the vanilla rate with no property.
            /// </summary>
            private int ChargedRate(TaxPayer taxPayer, Entity entity, bool hasProperty, Entity property, out int withoutDensity)
            {
                withoutDensity = taxPayer.m_AverageTaxRate;
                if (!hasProperty) return taxPayer.m_AverageTaxRate;
                TaxPayerKind kind = m_Type == IncomeSource.TaxResidential ? TaxPayerKind.Household
                    : m_Type == IncomeSource.TaxCommercial ? TaxPayerKind.Commercial : TaxPayerKind.Industrial;
                m_Rates.Adjustments(entity, property, kind, out int incomeDelta, out int densityTax);
                withoutDensity = EffectiveTaxRate.Compute(taxPayer.m_AverageTaxRate, 0, incomeDelta, m_RateLimits.x, m_RateLimits.y, taxPayer.m_UntaxedIncome);
                return EffectiveTaxRate.Compute(taxPayer.m_AverageTaxRate, densityTax, incomeDelta, m_RateLimits.x, m_RateLimits.y, taxPayer.m_UntaxedIncome);
            }
            private void PayTax(ref TaxPayer taxPayer, Entity entity, int chargedRate, int withoutDensityRate, DynamicBuffer<Resources> resources, IncomeSource taxType, NativeQueue<StatisticsEvent>.ParallelWriter statisticsEventQueue,
                out TaxZone zone, out int vanillaTax, out int district, out int density)
            {
                // Vanilla: int tax = GetTax(taxPayer); here at the charged rate. The stored rate is not changed.
                TaxPayer charged = taxPayer;
                charged.m_AverageTaxRate = chargedRate;
                int tax = TaxSystem.GetTax(charged);
                // The city's side: what this charge adds beyond vanilla's estimate (which pays the city at the stored rate).
                district = 0;
                density = 0;
                vanillaTax = tax;
                if (chargedRate != taxPayer.m_AverageTaxRate || withoutDensityRate != taxPayer.m_AverageTaxRate)
                {
                    TaxPayer noDensity = taxPayer;
                    noDensity.m_AverageTaxRate = withoutDensityRate;
                    vanillaTax = TaxSystem.GetTax(taxPayer);
                    DensityTaxBooking.Parts(vanillaTax, TaxSystem.GetTax(noDensity), tax, out district, out density);
                }
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
                zone = taxType == IncomeSource.TaxResidential ? TaxZone.Residential
                    : taxType == IncomeSource.TaxCommercial ? TaxZone.Commercial
                    : taxType == IncomeSource.TaxOffice ? TaxZone.Office : TaxZone.Industrial;
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
                // The chunk's sums for the city's side, per zone and density cell (slot 0: no cell; a chunk of companies
                // can hold both Industrial and Office): district, density + and -, tax at vanilla rates, taxable income.
                const int kSlots = TaxBookingLedger.Cells + 1, kFields = 5;
                var sums = new NativeArray<long>(TaxBookingLedger.Zones * kSlots * kFields, Allocator.Temp);
                bool isHousehold = m_Type == IncomeSource.TaxResidential;
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    TaxPayer taxPayer = nativeArray2[i];
                    DynamicBuffer<Resources> resources = bufferAccessor[i];
                    if (taxPayer.m_UntaxedIncome != 0)
                    {
                        Entity property = hasRenter ? renters[i].m_Property : Entity.Null;
                        int untaxed = taxPayer.m_UntaxedIncome;
                        int rate = ChargedRate(taxPayer, nativeArray[i], hasRenter && property != Entity.Null, property, out int withoutDensity);
                        PayTax(ref taxPayer, nativeArray[i], rate, withoutDensity, resources, m_Type, m_StatisticsEventQueue,
                            out TaxZone zone, out int vanillaTax, out int district, out int density);
                        nativeArray2[i] = taxPayer;
                        int cell = -1;
                        if (property != Entity.Null && !m_Rates.m_Keys.TryDensityCell(property, isHousehold, out cell)) cell = -1;
                        int s = ((int)zone * kSlots + cell + 1) * kFields;
                        sums[s] += district;
                        if (density > 0) sums[s + 1] += density;
                        else sums[s + 2] += density;
                        sums[s + 3] += vanillaTax;
                        if (untaxed > 0) sums[s + 4] += untaxed;
                    }
                }
                for (int z = 0; z < TaxBookingLedger.Zones; z++)
                {
                    for (int c = 0; c < kSlots; c++)
                    {
                        int s = (z * kSlots + c) * kFields;
                        if (sums[s] == 0 && sums[s + 1] == 0 && sums[s + 2] == 0 && sums[s + 3] == 0 && sums[s + 4] == 0) continue;
                        m_BookingQueue.Enqueue(new TaxBookingEvent
                        {
                            m_Zone = (byte)z,
                            m_Cell = (sbyte)(c - 1),
                            m_District = sums[s],
                            m_DensityPositive = sums[s + 1],
                            m_DensityNegative = sums[s + 2],
                            m_Vanilla = sums[s + 3],
                            m_Base = sums[s + 4],
                        });
                    }
                }
                sums.Dispose();
            }
        }
    }
}
