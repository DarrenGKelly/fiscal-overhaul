using System.Reflection;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Triggers;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;
using static Game.Simulation.CitizenHappinessSystem;

namespace FiscalOverhaul
{
    /// <summary>
    /// Citizen happiness with the tax factor at each citizen's own rate: a copy of vanilla CitizenHappinessSystem's
    /// update whose one change is the tax bonus, taken at the residential rate the citizen's home is charged (the vanilla
    /// rate for their education level plus the home's density and district adjustments, as <see cref="TaxEngineSystem"/>
    /// charges it). Unhappy households move out (HouseholdBehaviorSystem), so a district's rates have a real effect.
    ///
    /// Vanilla CitizenHappinessSystem stays the data store: its happiness factor array (saved by vanilla, read by the
    /// city info UI through <c>GetHappinessFactor</c>) is the one this system writes, and its public static Get*Bonuses
    /// helpers are called, not copied. Only its update is replaced: while this runs, <c>CitizenHappinessSystem.Enabled</c>
    /// is false. It runs exactly while the tax engine does (<see cref="TaxEngineGuardLogic.RunsHappiness"/>), so the
    /// Simulation changes switch and the game-version guard cover it; anything else turns vanilla's update back on, live.
    /// Registered right before vanilla with the same interval (16), so it inherits its update offset.
    ///
    /// Copied from Game.Simulation.CitizenHappinessSystem, game version 1.6.0f1: CitizenHappinessJob (:74-750),
    /// HappinessFactorJob (:758-798), GetFactorIndex (:997-1000), the static GetHappinessFactor (:1008-1021),
    /// GetTriggerTypeForHappinessFactor (:1939-1999), OnCreate (:1023-1066) and OnUpdate (:1843-1937). Changes are marked
    /// CHANGED or ADDED; the debug distribution (vanilla's DebugWatchDistribution) is left out.
    /// </summary>
    public partial class TaxHappinessSystem : GameSystemBase
    {
        // Copied from CitizenHappinessSystem.FactorItem (:65-72), which is private.
        private struct FactorItem
        {
            public HappinessFactor m_Factor;

            public int4 m_Value;

            public uint m_UpdateFrame;
        }

        // Copied from CitizenHappinessSystem.CitizenHappinessJob (:74-750), game version 1.6.0f1. Changes (marked):
        // the tax bonus at the home's charged rate; the home's adjustments and HomeRate (ADDED); no debug queue.
        [BurstCompile]
        private struct CitizenHappinessJob : IJobChunk
        {
            public ComponentTypeHandle<Citizen> m_CitizenType;

            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<HouseholdMember> m_HouseholdMemberType;

            [ReadOnly]
            public ComponentTypeHandle<CrimeVictim> m_CrimeVictimType;

            [ReadOnly]
            public ComponentTypeHandle<Criminal> m_CriminalType;

            [ReadOnly]
            public ComponentTypeHandle<Game.Citizens.Student> m_StudentType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            [ReadOnly]
            public ComponentTypeHandle<HealthProblem> m_HealthProblemType;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_Properties;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> m_WaterConsumers;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<CurrentDistrict> m_CurrentDistrictData;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> m_ServiceCoverages;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_Prefabs;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> m_Garbages;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public BufferLookup<HouseholdCitizen> m_HouseholdCitizens;

            [ReadOnly]
            public ComponentLookup<Locked> m_Locked;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_CrimeProducers;

            [ReadOnly]
            public ComponentLookup<MailProducer> m_MailProducers;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableBuildings;

            [ReadOnly]
            public ComponentLookup<BuildingData> m_BuildingDatas;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingPropertyDatas;

            [ReadOnly]
            public BufferLookup<DistrictModifier> m_DistrictModifiers;

            [ReadOnly]
            public BufferLookup<CityModifier> m_CityModifiers;

            [ReadOnly]
            public BufferLookup<ServiceFee> m_ServiceFees;

            [ReadOnly]
            public ComponentLookup<HealthProblem> m_HealthProblems;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Prison> m_Prisons;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.School> m_Schools;

            [ReadOnly]
            public NativeArray<GroundPollution> m_PollutionMap;

            [ReadOnly]
            public NativeArray<AirPollution> m_AirPollutionMap;

            [ReadOnly]
            public NativeArray<NoisePollution> m_NoisePollutionMap;

            [ReadOnly]
            public CellMapData<TelecomCoverage> m_TelecomCoverage;

            [ReadOnly]
            public LocalEffectSystem.ReadData m_LocalEffectData;

            [ReadOnly]
            public NativeArray<int> m_TaxRates;

            [ReadOnly]
            public HealthcareParameterData m_HealthcareParameters;

            [ReadOnly]
            public ParkParameterData m_ParkParameters;

            [ReadOnly]
            public EducationParameterData m_EducationParameters;

            [ReadOnly]
            public TelecomParameterData m_TelecomParameters;

            [ReadOnly]
            public GarbageParameterData m_GarbageParameters;

            [ReadOnly]
            public PoliceConfigurationData m_PoliceParameters;

            [ReadOnly]
            public CitizenHappinessParameterData m_CitizenHappinessParameters;

            [ReadOnly]
            public LeisureParametersData m_LeisureParameters;

            [ReadOnly]
            public TimeSettingsData m_TimeSettings;

            [ReadOnly]
            public ServiceFeeParameterData m_FeeParameters;

            [ReadOnly]
            public TimeData m_TimeData;

            [ReadOnly]
            public Entity m_City;

            [ReadOnly]
            public RandomSeed m_RandomSeed;

            public uint m_RawUpdateFrame;

            public NativeQueue<FactorItem>.ParallelWriter m_FactorQueue;

            public uint m_SimulationFrame;

            public NativeQueue<StatisticsEvent>.ParallelWriter m_StatisticsEventQueue;

            // ADDED: the home's adjustments (the tax engine's, TaxEngineSystem.PayTaxJob.ChargedRate for a household).
            [ReadOnly] public NativeArray<int> m_DensityAdjustments;
            [ReadOnly] public NativeParallelHashMap<Entity, int4> m_DistrictAdjustments;
            public TaxKeyResolver m_Keys;
            public int2 m_RateLimits;

            /// <summary>ADDED: the residential rate <paramref name="property"/> is charged at <paramref name="vanillaRate"/>.</summary>
            private int HomeRate(Entity property, int vanillaRate)
            {
                int density = 0;
                if (m_Keys.TryDensityCell(property, true, out int cell)) density = m_DensityAdjustments[cell];
                int district = 0;
                if (!m_DistrictAdjustments.IsEmpty)
                {
                    Entity districtEntity = m_Keys.District(property);
                    if (districtEntity != Entity.Null && m_DistrictAdjustments.TryGetValue(districtEntity, out int4 adjustments))
                        district = adjustments[(int)TaxZone.Residential];
                }
                return TaxHappinessMath.ResidentRate(vanillaRate, density, district, m_RateLimits.x, m_RateLimits.y);
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Entity healthcareServicePrefab = m_HealthcareParameters.m_HealthcareServicePrefab;
                Entity parkServicePrefab = m_ParkParameters.m_ParkServicePrefab;
                Entity educationServicePrefab = m_EducationParameters.m_EducationServicePrefab;
                Entity telecomServicePrefab = m_TelecomParameters.m_TelecomServicePrefab;
                Entity garbageServicePrefab = m_GarbageParameters.m_GarbageServicePrefab;
                Entity policeServicePrefab = m_PoliceParameters.m_PoliceServicePrefab;
                bool isLocked = m_Locked.HasEnabledComponent(healthcareServicePrefab);
                bool isLocked2 = m_Locked.HasEnabledComponent(parkServicePrefab);
                bool isLocked3 = m_Locked.HasEnabledComponent(educationServicePrefab);
                bool isLocked4 = m_Locked.HasEnabledComponent(telecomServicePrefab);
                bool isLocked5 = m_Locked.HasEnabledComponent(garbageServicePrefab);
                bool isLocked6 = m_Locked.HasEnabledComponent(policeServicePrefab);
                chunk.GetNativeArray(m_EntityType);
                NativeArray<Citizen> nativeArray = chunk.GetNativeArray(ref m_CitizenType);
                NativeArray<HouseholdMember> nativeArray2 = chunk.GetNativeArray(ref m_HouseholdMemberType);
                NativeArray<CrimeVictim> nativeArray3 = chunk.GetNativeArray(ref m_CrimeVictimType);
                NativeArray<Criminal> nativeArray4 = chunk.GetNativeArray(ref m_CriminalType);
                NativeArray<Game.Citizens.Student> nativeArray5 = chunk.GetNativeArray(ref m_StudentType);
                NativeArray<CurrentBuilding> nativeArray6 = chunk.GetNativeArray(ref m_CurrentBuildingType);
                NativeArray<HealthProblem> nativeArray7 = chunk.GetNativeArray(ref m_HealthProblemType);
                EnabledMask enabledMask = chunk.GetEnabledMask(ref m_CrimeVictimType);
                DynamicBuffer<CityModifier> cityModifiers = m_CityModifiers[m_City];
                DynamicBuffer<ServiceFee> fees = m_ServiceFees[m_City];
                float relativeFee = ServiceFeeSystem.GetFee(PlayerResource.Electricity, fees) / m_FeeParameters.m_ElectricityFee.m_Default;
                float relativeFee2 = ServiceFeeSystem.GetFee(PlayerResource.Water, fees) / m_FeeParameters.m_WaterFee.m_Default;
                int4 value = default;
                int4 value2 = default;
                int4 value3 = default;
                int4 value4 = default;
                int4 value5 = default;
                int4 value6 = default;
                int4 value7 = default;
                int4 value8 = default;
                int4 value9 = default;
                int4 value10 = default;
                int4 value11 = default;
                int4 value12 = default;
                int4 value13 = default;
                int4 value14 = default;
                int4 value15 = default;
                int4 value16 = default;
                int4 value17 = default;
                int4 value18 = default;
                int4 value19 = default;
                int4 value20 = default;
                int4 value21 = default;
                int4 value22 = default;
                int4 value23 = default;
                int4 value24 = default;
                int4 value25 = default;
                int4 value26 = default;
                Unity.Mathematics.Random random = m_RandomSeed.GetRandom(unfilteredChunkIndex);
                int num = 0;
                int num2 = 0;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (CollectionUtils.TryGet(nativeArray7, i, out var value27) && CitizenUtils.IsDead(value27))
                    {
                        continue;
                    }
                    Entity household = nativeArray2[i].m_Household;
                    Citizen citizen = nativeArray[i];
                    Household household2 = m_Households[household];
                    if ((household2.m_Flags & HouseholdFlags.MovedIn) == 0 && (citizen.m_State & CitizenFlags.Tourist) == 0)
                    {
                        continue;
                    }
                    Entity entity = Entity.Null;
                    Entity entity2 = Entity.Null;
                    bool flag = false;
                    bool flag2 = false;
                    float3 float5 = default;
                    if (m_Properties.TryGetComponent(household, out var componentData))
                    {
                        flag = true;
                        entity = componentData.m_Property;
                        if (m_CurrentDistrictData.HasComponent(entity))
                        {
                            entity2 = m_CurrentDistrictData[entity].m_District;
                        }
                    }
                    DynamicBuffer<HouseholdCitizen> householdCitizens = m_HouseholdCitizens[household];
                    int num3 = 0;
                    int length = householdCitizens.Length;
                    if (citizen.GetAge() == CitizenAge.Child)
                    {
                        num3 += length;
                    }
                    int2 int5;
                    if ((citizen.m_State & CitizenFlags.Tourist) != 0)
                    {
                        int5 = default;
                    }
                    else
                    {
                        int rent = (flag ? componentData.m_Rent : 0);
                        int5 = new int2(0, GetWealthWellbeing(household2.m_Income, rent, length, in m_CitizenHappinessParameters));
                    }
                    value22.x += int5.x + int5.y;
                    value22.y++;
                    value22.z += int5.x;
                    value22.w += int5.y;
                    int2 int6 = 0;
                    if (CollectionUtils.TryGet(nativeArray4, i, out var value28) && (value28.m_Flags & CriminalFlags.Prisoner) != 0 && CollectionUtils.TryGet(nativeArray6, i, out var value29) && m_Prisons.TryGetComponent(value29.m_CurrentBuilding, out var componentData2))
                    {
                        int6 += new int2(componentData2.m_PrisonerHealth, componentData2.m_PrisonerWellbeing);
                    }
                    if (CollectionUtils.TryGet(nativeArray5, i, out var value30) && m_Schools.TryGetComponent(value30.m_School, out var componentData3))
                    {
                        int6 += new int2(componentData3.m_StudentHealth, componentData3.m_StudentWellbeing);
                    }
                    value21 += new int4(int6.x + int6.y, 1, int6.x, int6.y);
                    int2 int7 = new int2(0, 0);
                    int2 int8 = default;
                    int2 int9 = default;
                    int2 int10 = default;
                    int2 int11 = default;
                    int2 int12 = default;
                    int2 int13 = default;
                    int2 int14 = default;
                    int2 int15 = default;
                    int2 int16 = default;
                    int2 int17 = default;
                    int2 int18 = default;
                    int2 int19 = default;
                    int2 int20 = default;
                    int2 int21 = default;
                    int2 int22 = default;
                    int2 int23 = default;
                    int2 int24 = default;
                    int2 int25 = default;
                    int2 int26 = default;
                    int2 int27 = default;
                    CrimeVictim crimeVictim = default;
                    if (enabledMask[i])
                    {
                        crimeVictim = nativeArray3[i];
                    }
                    if (flag && m_Prefabs.TryGetComponent(entity, out var componentData4))
                    {
                        Entity prefab = componentData4.m_Prefab;
                        Entity entity3 = entity;
                        Entity entity4 = Entity.Null;
                        float curvePosition = 0f;
                        if (m_Buildings.HasComponent(entity3))
                        {
                            Building building = m_Buildings[entity3];
                            entity4 = building.m_RoadEdge;
                            curvePosition = building.m_CurvePosition;
                        }
                        bool hasElectricityConsumer = m_ElectricityConsumers.TryGetComponent(entity3, out var componentData5);
                        bool hasWaterConsumers = m_WaterConsumers.TryGetComponent(entity3, out var componentData6);
                        int8 = GetElectricitySupplyBonuses(hasElectricityConsumer, componentData5, in m_CitizenHappinessParameters);
                        value5.x += int8.x + int8.y;
                        value5.z += int8.x;
                        value5.w += int8.y;
                        value5.y++;
                        int9 = GetElectricityFeeBonuses(hasElectricityConsumer, componentData5, relativeFee, in m_CitizenHappinessParameters);
                        value6.x += int9.x + int9.y;
                        value6.z += int9.x;
                        value6.w += int9.y;
                        value6.y++;
                        int14 = GetWaterSupplyBonuses(hasWaterConsumers, componentData6, in m_CitizenHappinessParameters);
                        value10.x += int14.x + int14.y;
                        value10.z += int14.x;
                        value10.w += int14.y;
                        value10.y++;
                        int15 = GetWaterFeeBonuses(hasWaterConsumers, componentData6, relativeFee2, in m_CitizenHappinessParameters);
                        value11.x += int15.x + int15.y;
                        value11.z += int15.x;
                        value11.w += int15.y;
                        value11.y++;
                        int16 = GetWaterPollutionBonuses(hasWaterConsumers, componentData6, cityModifiers, in m_CitizenHappinessParameters);
                        value12.x += int16.x + int16.y;
                        value12.z += int16.x;
                        value12.w += int16.y;
                        value12.y++;
                        int17 = GetSewageBonuses(hasWaterConsumers, componentData6, in m_CitizenHappinessParameters);
                        value13.x += int17.x + int17.y;
                        value13.z += int17.x;
                        value13.w += int17.y;
                        value13.y++;
                        if (m_ServiceCoverages.HasBuffer(entity4))
                        {
                            DynamicBuffer<Game.Net.ServiceCoverage> serviceCoverage = m_ServiceCoverages[entity4];
                            int10 = GetHealthcareBonuses(curvePosition, serviceCoverage, isLocked, in m_CitizenHappinessParameters);
                            value7.x += int10.x + int10.y;
                            value7.z += int10.x;
                            value7.w += int10.y;
                            value7.y++;
                            int20 = GetEntertainmentBonuses(curvePosition, serviceCoverage, cityModifiers, isLocked2, in m_CitizenHappinessParameters);
                            value15.x += int20.x + int20.y;
                            value15.z += int20.x;
                            value15.w += int20.y;
                            value15.y++;
                            int21 = GetEducationBonuses(curvePosition, serviceCoverage, isLocked3, in m_CitizenHappinessParameters, num3);
                            value16.x += int21.x + int21.y;
                            value16.z += int21.x;
                            value16.w += int21.y;
                            value16.y++;
                            int24 = GetWellfareBonuses(curvePosition, serviceCoverage, in m_CitizenHappinessParameters, citizen.Happiness);
                            value18.x += int24.x + int24.y;
                            value18.z += int24.x;
                            value18.w += int24.y;
                            value18.y++;
                        }
                        flag2 = m_Transforms.TryGetComponent(entity3, out var componentData7);
                        float5 = (flag2 ? componentData7.m_Position : default(float3));
                        int11 = GetGroundPollutionBonuses(flag2, float5, m_PollutionMap, cityModifiers, in m_CitizenHappinessParameters);
                        value8.x += int11.x + int11.y;
                        value8.z += int11.x;
                        value8.w += int11.y;
                        value8.y++;
                        int12 = GetAirPollutionBonuses(flag2, float5, m_AirPollutionMap, cityModifiers, in m_CitizenHappinessParameters);
                        value3.x += int12.x + int12.y;
                        value3.z += int12.x;
                        value3.w += int12.y;
                        value3.y++;
                        int13 = GetNoiseBonuses(flag2, float5, m_NoisePollutionMap, in m_CitizenHappinessParameters);
                        value9.x += int13.x + int13.y;
                        value9.z += int13.x;
                        value9.w += int13.y;
                        value9.y++;
                        int18 = GetGarbageBonuses(entity3, ref m_Garbages, isLocked5, in m_GarbageParameters);
                        value14.x += int18.x + int18.y;
                        value14.z += int18.x;
                        value14.w += int18.y;
                        value14.y++;
                        int19 = GetCrimeBonuses(crimeVictim, entity3, ref m_CrimeProducers, isLocked6, in m_CitizenHappinessParameters);
                        value.x += int19.x + int19.y;
                        value.z += int19.x;
                        value.w += int19.y;
                        value.y++;
                        int22 = GetMailBonuses(entity3, ref m_MailProducers, isLocked4, in m_CitizenHappinessParameters);
                        value17.x += int22.x + int22.y;
                        value17.z += int22.x;
                        value17.w += int22.y;
                        value17.y++;
                        int23 = GetTelecomBonuses(flag2, float5, m_TelecomCoverage, isLocked4, in m_CitizenHappinessParameters);
                        value2.x += int23.x + int23.y;
                        value2.z += int23.x;
                        value2.w += int23.y;
                        value2.y++;
                        int27 = GetUnemploymentBonuses(ref citizen, in m_CitizenHappinessParameters);
                        value26.x += int27.x + int27.y;
                        value26.z += int27.x;
                        value26.w += int27.y;
                        value26.y++;
                        value25.y++;
                        if ((citizen.m_State & CitizenFlags.Homeless) == 0 && m_SpawnableBuildings.HasComponent(prefab) && m_BuildingDatas.HasComponent(prefab) && m_BuildingPropertyDatas.HasComponent(prefab))
                        {
                            SpawnableBuildingData spawnableBuildingData = m_SpawnableBuildings[prefab];
                            BuildingData buildingData = m_BuildingDatas[prefab];
                            BuildingPropertyData buildingPropertyData = m_BuildingPropertyDatas[prefab];
                            float num4 = buildingPropertyData.m_SpaceMultiplier * (float)buildingData.m_LotSize.x * (float)buildingData.m_LotSize.y / (float)(length * buildingPropertyData.m_ResidentialProperties);
                            int7.y = Mathf.RoundToInt(GetApartmentWellbeing(num4, spawnableBuildingData.m_Level));
                            value4.x += int7.x + int7.y;
                            value4.z += int7.x;
                            value4.w += int7.y;
                            value4.y++;
                        }
                        else
                        {
                            int7.y = Mathf.RoundToInt(GetApartmentWellbeing(0.01f, 1));
                            value4.x += int7.y;
                            value4.w += int7.y;
                            value4.y++;
                            int26 = GetHomelessBonuses(in m_CitizenHappinessParameters);
                            value25.x += int26.x + int26.y;
                            value25.z += int26.x;
                            value25.w += int26.y;
                        }
                    }
                    bool flag3 = (citizen.m_State & CitizenFlags.Tourist) != 0;
                    if (random.NextInt(0, 100) < (flag3 ? m_LeisureParameters.m_ChanceTouristDecreaseLeisureCounter : m_LeisureParameters.m_ChanceCitizenDecreaseLeisureCounter))
                    {
                        citizen.m_LeisureCounter = (byte)math.min(255, math.max(0, citizen.m_LeisureCounter - m_LeisureParameters.m_AmountLeisureCounterDecrease));
                    }
                    citizen.m_PenaltyCounter = (byte)math.max(0, citizen.m_PenaltyCounter - 1);
                    int2 leisureBonuses = GetLeisureBonuses(citizen);
                    value19.x += leisureBonuses.x + leisureBonuses.y;
                    value19.z += leisureBonuses.x;
                    value19.w += leisureBonuses.y;
                    value19.y++;
                    if (!flag3)
                    {
                        // CHANGED (the one change): the tax bonus is at the rate the citizen's home is charged, the vanilla
                        // rate for their education level plus the home's density and district adjustments. With no
                        // adjustment (or no home) it is vanilla's own call, so all-zero adjustments are exactly vanilla.
                        int educationLevel = citizen.GetEducationLevel();
                        int25 = GetTaxBonuses(educationLevel, m_TaxRates, cityModifiers, in m_CitizenHappinessParameters);
                        if (flag)
                        {
                            int vanillaRate = TaxSystem.GetResidentialTaxRate(educationLevel, m_TaxRates);
                            int homeRate = HomeRate(entity, vanillaRate);
                            if (homeRate != vanillaRate)
                            {
                                float2 modifier = CityUtils.GetModifier(cityModifiers, CityModifierType.TaxHappiness);
                                int25 = new int2(0, TaxHappinessMath.Bonus(homeRate, educationLevel, m_CitizenHappinessParameters.m_TaxUneducatedMultiplier, m_CitizenHappinessParameters.m_TaxPoorlyEducatedMultiplier, m_CitizenHappinessParameters.m_TaxEducatedMultiplier, m_CitizenHappinessParameters.m_TaxWellEducatedMultiplier, m_CitizenHappinessParameters.m_TaxHighlyEducatedMultiplier, modifier.x, modifier.y));
                            }
                        }
                    }
                    value20.x += int25.x + int25.y;
                    value20.z += int25.x;
                    value20.w += int25.y;
                    value20.y++;
                    int2 sicknessBonuses = GetSicknessBonuses(nativeArray7.Length != 0, ref citizen, in m_CitizenHappinessParameters);
                    value7.x += sicknessBonuses.x + sicknessBonuses.y;
                    value7.z += sicknessBonuses.x;
                    value7.w += sicknessBonuses.y;
                    value7.y++;
                    int2 deathPenalty = GetDeathPenalty(householdCitizens, ref m_HealthProblems, in m_CitizenHappinessParameters);
                    value24.x += deathPenalty.x + deathPenalty.y;
                    value24.z += deathPenalty.x;
                    value24.w += deathPenalty.y;
                    value24.y++;
                    int num5 = ((citizen.m_PenaltyCounter > 0) ? m_CitizenHappinessParameters.m_PenaltyEffect : 0);
                    value23.x += num5;
                    value23.w += num5;
                    value23.y++;
                    int num6 = math.max(0, 50 + num5 + deathPenalty.y + int5.y + int8.y + int9.y + int14.y + int15.y + int17.y + int10.y + leisureBonuses.y + int6.y + int16.y + int13.y + int18.y + int19.y + int20.y + int22.y + int21.y + int23.y + int7.y + int24.y + int25.y + int26.y + int27.y);
                    int num7 = 50 + int10.x + sicknessBonuses.x + deathPenalty.x + int6.x + int11.x + int12.x + int8.x + int14.x + int17.x + int16.x + int18.x + int7.x + int24.x + int26.x + int27.x;
                    num7 += math.select(0, 1, (citizen.m_State & CitizenFlags.BicycleUser) != 0);
                    float wellbeing = num6;
                    float health = num7;
                    GetLocalEffectBonuses(ref wellbeing, ref health, ref m_LocalEffectData, flag2, float5);
                    if (m_DistrictModifiers.HasBuffer(entity2))
                    {
                        DynamicBuffer<DistrictModifier> modifiers = m_DistrictModifiers[entity2];
                        AreaUtils.ApplyModifier(ref wellbeing, modifiers, DistrictModifierType.Wellbeing);
                    }
                    num6 = Mathf.RoundToInt(wellbeing);
                    num7 = Mathf.RoundToInt(health);
                    int num8 = ((random.NextInt(100) > 50 + citizen.m_WellBeing - num6) ? 1 : (-1));
                    citizen.m_WellBeing = (byte)math.max(0, math.min(100, citizen.m_WellBeing + num8));
                    num8 = ((random.NextInt(100) > 50 + citizen.m_Health - num7) ? 1 : (-1));
                    int maxHealth = GetMaxHealth(citizen.GetAgeInDays(m_SimulationFrame, m_TimeData) / (float)m_TimeSettings.m_DaysPerYear);
                    citizen.m_Health = (byte)math.max(0, math.min(maxHealth, citizen.m_Health + num8));
                    if (citizen.m_WellBeing < m_CitizenHappinessParameters.m_LowWellbeing)
                    {
                        num++;
                    }
                    if (citizen.m_Health < m_CitizenHappinessParameters.m_LowHealth)
                    {
                        num2++;
                    }
                    nativeArray[i] = citizen;
                }
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Telecom,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value2
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Crime,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.AirPollution,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value3
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Apartment,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value4
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Electricity,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value5
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.ElectricityFee,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value6
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Healthcare,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value7
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.GroundPollution,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value8
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.NoisePollution,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value9
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Water,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value10
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.WaterFee,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value11
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.WaterPollution,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value12
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Sewage,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value13
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Garbage,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value14
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Entertainment,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value15
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Education,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value16
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Mail,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value17
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Welfare,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value18
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Leisure,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value19
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Tax,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value20
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Buildings,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value21
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Consumption,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value22
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.TrafficPenalty,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value23
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.DeathPenalty,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value24
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Homelessness,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value25
                });
                m_FactorQueue.Enqueue(new FactorItem
                {
                    m_Factor = HappinessFactor.Unemployment,
                    m_UpdateFrame = m_RawUpdateFrame,
                    m_Value = value26
                });
                m_StatisticsEventQueue.Enqueue(new StatisticsEvent
                {
                    m_Statistic = StatisticType.WellbeingLevel,
                    m_Change = num
                });
                m_StatisticsEventQueue.Enqueue(new StatisticsEvent
                {
                    m_Statistic = StatisticType.HealthLevel,
                    m_Change = num2
                });
            }
            }

        // Copied from CitizenHappinessSystem.HappinessFactorJob (:758-798), game version 1.6.0f1. Unchanged; the
        // factor array it writes is vanilla's.
        [BurstCompile]
        private struct HappinessFactorJob : IJob
        {
            public NativeArray<int4> m_HappinessFactors;

            public NativeQueue<FactorItem> m_FactorQueue;

            public NativeQueue<TriggerAction> m_TriggerActionQueue;

            public uint m_RawUpdateFrame;

            public Entity m_ParameterEntity;

            [ReadOnly]
            public BufferLookup<HappinessFactorParameterData> m_Parameters;

            [ReadOnly]
            public ComponentLookup<Locked> m_Locked;

            public void Execute()
            {
                for (int i = 0; i < 26; i++)
                {
                    m_HappinessFactors[GetFactorIndex((HappinessFactor)i, m_RawUpdateFrame)] = default;
                }
                FactorItem item;
                while (m_FactorQueue.TryDequeue(out item))
                {
                    if (item.m_UpdateFrame != m_RawUpdateFrame)
                    {
                        UnityEngine.Debug.LogWarning("Different updateframe in HappinessFactorJob than in its queue");
                    }
                    m_HappinessFactors[GetFactorIndex(item.m_Factor, item.m_UpdateFrame)] += item.m_Value;
                }
                DynamicBuffer<HappinessFactorParameterData> parameters = m_Parameters[m_ParameterEntity];
                for (int j = 0; j < 26; j++)
                {
                    m_TriggerActionQueue.Enqueue(new TriggerAction(GetTriggerTypeForHappinessFactor((HappinessFactor)j), Entity.Null, GetHappinessFactor((HappinessFactor)j, m_HappinessFactors, parameters, ref m_Locked).x));
                }
            }
        }

        // Copied from CitizenHappinessSystem (:997-1000, :1008-1021, :1939-1999), game version 1.6.0f1: private there.
        private static int GetFactorIndex(HappinessFactor factor, uint updateFrame)
        {
            return (int)factor + (int)(26 * updateFrame);
        }

        private static float3 GetHappinessFactor(HappinessFactor factor, NativeArray<int4> happinessFactors, DynamicBuffer<HappinessFactorParameterData> parameters, ref ComponentLookup<Locked> locked)
        {
            int4 int5 = 0;
            for (uint num = 0u; num < 16; num++)
            {
                int5 += happinessFactors[GetFactorIndex(factor, num)];
            }
            Entity lockedEntity = parameters[(int)factor].m_LockedEntity;
            if (lockedEntity != Entity.Null && locked.HasEnabledComponent(lockedEntity))
            {
                return 0;
            }
            return ((int5.y > 0) ? new float3((float)int5.x / (2f * (float)int5.y), int5.z / int5.y, int5.w / int5.y) : default(float3)) - parameters[(int)factor].m_BaseLevel;
        }

        private static TriggerType GetTriggerTypeForHappinessFactor(HappinessFactor factor)
        {
            switch (factor)
            {
            case HappinessFactor.Telecom:
                return TriggerType.TelecomHappinessFactor;
            case HappinessFactor.Crime:
                return TriggerType.CrimeHappinessFactor;
            case HappinessFactor.AirPollution:
                return TriggerType.AirPollutionHappinessFactor;
            case HappinessFactor.Apartment:
                return TriggerType.ApartmentHappinessFactor;
            case HappinessFactor.Electricity:
                return TriggerType.ElectricityHappinessFactor;
            case HappinessFactor.Healthcare:
                return TriggerType.HealthcareHappinessFactor;
            case HappinessFactor.GroundPollution:
                return TriggerType.GroundPollutionHappinessFactor;
            case HappinessFactor.NoisePollution:
                return TriggerType.NoisePollutionHappinessFactor;
            case HappinessFactor.Water:
                return TriggerType.WaterHappinessFactor;
            case HappinessFactor.WaterPollution:
                return TriggerType.WaterPollutionHappinessFactor;
            case HappinessFactor.Sewage:
                return TriggerType.SewageHappinessFactor;
            case HappinessFactor.Garbage:
                return TriggerType.GarbageHappinessFactor;
            case HappinessFactor.Entertainment:
                return TriggerType.EntertainmentHappinessFactor;
            case HappinessFactor.Education:
                return TriggerType.EducationHappinessFactor;
            case HappinessFactor.Mail:
                return TriggerType.MailHappinessFactor;
            case HappinessFactor.Welfare:
                return TriggerType.WelfareHappinessFactor;
            case HappinessFactor.Leisure:
                return TriggerType.LeisureHappinessFactor;
            case HappinessFactor.Tax:
                return TriggerType.TaxHappinessFactor;
            case HappinessFactor.Buildings:
                return TriggerType.BuildingsHappinessFactor;
            case HappinessFactor.Consumption:
                return TriggerType.WealthHappinessFactor;
            case HappinessFactor.TrafficPenalty:
                return TriggerType.TrafficPenaltyHappinessFactor;
            case HappinessFactor.DeathPenalty:
                return TriggerType.DeathPenaltyHappinessFactor;
            case HappinessFactor.Homelessness:
                return TriggerType.HomelessnessHappinessFactor;
            case HappinessFactor.ElectricityFee:
                return TriggerType.ElectricityFeeHappinessFactor;
            case HappinessFactor.WaterFee:
                return TriggerType.WaterFeeHappinessFactor;
            case HappinessFactor.Unemployment:
                return TriggerType.CitizenUnemployedHappinessFactor;
            default:
                UnityEngine.Debug.LogError($"Unknown trigger type for happiness factor: {factor}");
                return TriggerType.NewNotification;
            }
        }

        private NativeQueue<FactorItem> m_FactorQueue;
        private EntityQuery m_CitizenQuery;
        private EntityQuery m_HappinessFactorParameterQuery;
        private SimulationSystem m_SimulationSystem;
        private GroundPollutionSystem m_GroundPollutionSystem;
        private AirPollutionSystem m_AirPollutionSystem;
        private NoisePollutionSystem m_NoisePollutionSystem;
        private TelecomCoverageSystem m_TelecomCoverageSystem;
        private LocalEffectSystem m_LocalEffectSystem;
        private CitySystem m_CitySystem;
        private TriggerSystem m_TriggerSystem;
        private TaxSystem m_TaxSystem;
        private CityStatisticsSystem m_CityStatisticsSystem;
        private EntityQuery m_HealthcareParameterQuery;
        private EntityQuery m_ParkParameterQuery;
        private EntityQuery m_EducationParameterQuery;
        private EntityQuery m_TelecomParameterQuery;
        private EntityQuery m_GarbageParameterQuery;
        private EntityQuery m_PoliceParameterQuery;
        private EntityQuery m_CitizenHappinessParameterQuery;
        private EntityQuery m_TimeSettingQuery;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_LeisureParameterQuery;
        private EntityQuery m_FeeParameterQuery;

        // ADDED: vanilla's system, whose factor array this writes, and the tax engine, whose decision this follows.
        private CitizenHappinessSystem m_Vanilla;
        private TaxEngineSystem m_TaxEngine;
        private NativeArray<int4> m_HappinessFactors;
        private FieldInfo m_LastDepsField;
        private bool m_StructureOk;
        private bool m_Ready;
        private bool m_WeDisabledVanilla;
        private bool m_ForeignDisabled;
        private bool m_Logged;

        /// <summary>Whether this system computes happiness right now (vanilla CitizenHappinessSystem's update is off).</summary>
        public bool Running { get; private set; }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Same as CitizenHappinessSystem, so this inherits its update offset.
            return 16;
        }

        // Copied from Game.Simulation.CitizenHappinessSystem.OnCreate (:1023-1066) and __AssignQueries (:2050-2063),
        // game version 1.6.0f1. Changes: the factor array is vanilla's (read by reflection), not a new one; no debug data;
        // the guard's structure check (ADDED).
        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_GroundPollutionSystem = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            m_AirPollutionSystem = World.GetOrCreateSystemManaged<AirPollutionSystem>();
            m_NoisePollutionSystem = World.GetOrCreateSystemManaged<NoisePollutionSystem>();
            m_TelecomCoverageSystem = World.GetOrCreateSystemManaged<TelecomCoverageSystem>();
            m_LocalEffectSystem = World.GetOrCreateSystemManaged<LocalEffectSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TriggerSystem = World.GetOrCreateSystemManaged<TriggerSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_FactorQueue = new NativeQueue<FactorItem>(Allocator.Persistent);
            m_HealthcareParameterQuery = GetEntityQuery(ComponentType.ReadOnly<HealthcareParameterData>());
            m_ParkParameterQuery = GetEntityQuery(ComponentType.ReadOnly<ParkParameterData>());
            m_EducationParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EducationParameterData>());
            m_TelecomParameterQuery = GetEntityQuery(ComponentType.ReadOnly<TelecomParameterData>());
            m_GarbageParameterQuery = GetEntityQuery(ComponentType.ReadOnly<GarbageParameterData>());
            m_PoliceParameterQuery = GetEntityQuery(ComponentType.ReadOnly<PoliceConfigurationData>());
            m_CitizenHappinessParameterQuery = GetEntityQuery(ComponentType.ReadOnly<CitizenHappinessParameterData>());
            m_CitizenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[3]
                {
                    ComponentType.ReadWrite<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                    ComponentType.ReadOnly<UpdateFrame>()
                },
                None = new ComponentType[2]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });
            m_TimeSettingQuery = GetEntityQuery(ComponentType.ReadOnly<TimeSettingsData>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_HappinessFactorParameterQuery = GetEntityQuery(ComponentType.ReadOnly<HappinessFactorParameterData>());
            m_LeisureParameterQuery = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<LeisureParametersData>() }, Options = EntityQueryOptions.IncludeSystems });
            m_FeeParameterQuery = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<ServiceFeeParameterData>() }, Options = EntityQueryOptions.IncludeSystems });
            RequireForUpdate(m_CitizenQuery);
            RequireForUpdate<ServiceFeeParameterData>();
            // ADDED: the tax engine's rate limits (TaxSystem.GetTaxParameterData).
            RequireForUpdate<TaxParameterData>();

            // ADDED: vanilla's factor array and its last job handle, which this writes; the structure check.
            m_Vanilla = World.GetOrCreateSystemManaged<CitizenHappinessSystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_StructureOk = TaxEngineGuard.HappinessStructureOk(m_Vanilla, out m_HappinessFactors, out m_LastDepsField, out string problem);
            Mod.log.Info($"TaxHappiness: CitizenHappinessSystem structure {(m_StructureOk ? "ok" : "MISMATCH: " + problem)}");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            // Leave vanilla computing happiness.
            if (m_WeDisabledVanilla && m_Vanilla != null) m_Vanilla.Enabled = true;
            m_WeDisabledVanilla = false;
            Dependency.Complete();
            m_FactorQueue.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // Decided only once a city is loading, after every mod's OnLoad (as the tax engine).
            m_Ready = true;
            ApplyMode();
        }

        /// <summary>
        /// Follows the tax engine's decision (<see cref="TaxEngineGuardLogic.RunsHappiness"/>) and turns vanilla
        /// CitizenHappinessSystem's update off or back on to match. Called every update and whenever Options change
        /// (after the engine's own ApplyMode).
        /// </summary>
        public void ApplyMode()
        {
            if (!m_Ready || m_Vanilla == null || m_TaxEngine == null) return;
            if (!m_WeDisabledVanilla && !m_Vanilla.Enabled && !m_ForeignDisabled)
            {
                m_ForeignDisabled = true;
                Mod.log.Warn("TaxHappiness: vanilla CitizenHappinessSystem was already disabled (another mod?); tax happiness stays vanilla and leaves it alone");
            }
            bool running = TaxEngineGuardLogic.RunsHappiness(m_TaxEngine.Mode, m_StructureOk, m_ForeignDisabled);

            if (!m_ForeignDisabled)
            {
                if (running && !m_WeDisabledVanilla)
                {
                    m_Vanilla.Enabled = false;
                    m_WeDisabledVanilla = true;
                }
                else if (!running && m_WeDisabledVanilla)
                {
                    m_Vanilla.Enabled = true;
                    m_WeDisabledVanilla = false;
                }
            }

            if (running != Running || !m_Logged)
            {
                Mod.log.Info($"TaxHappiness: {(running ? "running" : "vanilla")} (tax engine {m_TaxEngine.Mode}, structure ok {m_StructureOk}, foreign disabled {m_ForeignDisabled}); vanilla CitizenHappinessSystem {(m_Vanilla.Enabled ? "enabled" : "disabled")}");
                m_Logged = true;
            }
            Running = running;
        }

        // Copied from Game.Simulation.CitizenHappinessSystem.OnUpdate (:1843-1937), game version 1.6.0f1. Changes: the
        // mode check first; type handles and lookups from SystemBase instead of the generated TypeHandle; no debug
        // queue; the home adjustments, rate limits and key lookups passed to the job; vanilla's m_LastDeps set to this
        // update's handle (so its GetHappinessFactor waits for this job).
        [Preserve]
        protected override void OnUpdate()
        {
            ApplyMode();
            if (!Running) return;

            uint updateFrameWithInterval = SimulationUtils.GetUpdateFrameWithInterval(m_SimulationSystem.frameIndex, (uint)GetUpdateInterval(SystemUpdatePhase.GameSimulation), 16);
            m_CitizenQuery.ResetFilter();
            m_CitizenQuery.AddSharedComponentFilter(new UpdateFrame(updateFrameWithInterval));

            // ADDED: the jobs get their own copies of the adjustments, freed with them (as the tax engine).
            var density = new NativeArray<int>(DensityTaxState.Snapshot(), Allocator.TempJob);
            NativeParallelHashMap<Entity, int4> districts = TaxEngineSystem.BuildDistrictAdjustments();

            JobHandle jobHandle = new CitizenHappinessJob
            {
                m_CitizenType = GetComponentTypeHandle<Citizen>(false),
                m_EntityType = GetEntityTypeHandle(),
                m_HouseholdMemberType = GetComponentTypeHandle<HouseholdMember>(true),
                m_CrimeVictimType = GetComponentTypeHandle<CrimeVictim>(true),
                m_CriminalType = GetComponentTypeHandle<Criminal>(true),
                m_StudentType = GetComponentTypeHandle<Game.Citizens.Student>(true),
                m_CurrentBuildingType = GetComponentTypeHandle<CurrentBuilding>(true),
                m_HealthProblemType = GetComponentTypeHandle<HealthProblem>(true),
                m_Households = GetComponentLookup<Household>(true),
                m_HealthProblems = GetComponentLookup<HealthProblem>(true),
                m_Buildings = GetComponentLookup<Building>(true),
                m_ElectricityConsumers = GetComponentLookup<ElectricityConsumer>(true),
                m_Properties = GetComponentLookup<PropertyRenter>(true),
                m_ServiceCoverages = GetBufferLookup<Game.Net.ServiceCoverage>(true),
                m_Transforms = GetComponentLookup<Game.Objects.Transform>(true),
                m_CurrentDistrictData = GetComponentLookup<CurrentDistrict>(true),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_WaterConsumers = GetComponentLookup<WaterConsumer>(true),
                m_Garbages = GetComponentLookup<GarbageProducer>(true),
                m_HouseholdCitizens = GetBufferLookup<HouseholdCitizen>(true),
                m_Locked = GetComponentLookup<Locked>(true),
                m_CrimeProducers = GetComponentLookup<CrimeProducer>(true),
                m_MailProducers = GetComponentLookup<MailProducer>(true),
                m_BuildingDatas = GetComponentLookup<BuildingData>(true),
                m_BuildingPropertyDatas = GetComponentLookup<BuildingPropertyData>(true),
                m_SpawnableBuildings = GetComponentLookup<SpawnableBuildingData>(true),
                m_DistrictModifiers = GetBufferLookup<DistrictModifier>(true),
                m_CityModifiers = GetBufferLookup<CityModifier>(true),
                m_ServiceFees = GetBufferLookup<ServiceFee>(true),
                m_Prisons = GetComponentLookup<Game.Buildings.Prison>(true),
                m_Schools = GetComponentLookup<Game.Buildings.School>(true),
                m_PollutionMap = m_GroundPollutionSystem.GetMap(readOnly: true, out var dependencies),
                m_AirPollutionMap = m_AirPollutionSystem.GetMap(readOnly: true, out var dependencies2),
                m_NoisePollutionMap = m_NoisePollutionSystem.GetMap(readOnly: true, out var dependencies3),
                m_TelecomCoverage = m_TelecomCoverageSystem.GetData(readOnly: true, out var dependencies4),
                m_LocalEffectData = m_LocalEffectSystem.GetReadData(out var dependencies5),
                m_HealthcareParameters = m_HealthcareParameterQuery.GetSingleton<HealthcareParameterData>(),
                m_ParkParameters = m_ParkParameterQuery.GetSingleton<ParkParameterData>(),
                m_EducationParameters = m_EducationParameterQuery.GetSingleton<EducationParameterData>(),
                m_TelecomParameters = m_TelecomParameterQuery.GetSingleton<TelecomParameterData>(),
                m_GarbageParameters = m_GarbageParameterQuery.GetSingleton<GarbageParameterData>(),
                m_PoliceParameters = m_PoliceParameterQuery.GetSingleton<PoliceConfigurationData>(),
                m_CitizenHappinessParameters = m_CitizenHappinessParameterQuery.GetSingleton<CitizenHappinessParameterData>(),
                m_LeisureParameters = m_LeisureParameterQuery.GetSingleton<LeisureParametersData>(),
                m_SimulationFrame = m_SimulationSystem.frameIndex,
                m_TimeSettings = m_TimeSettingQuery.GetSingleton<TimeSettingsData>(),
                m_FeeParameters = m_FeeParameterQuery.GetSingleton<ServiceFeeParameterData>(),
                m_TimeData = m_TimeDataQuery.GetSingleton<TimeData>(),
                m_TaxRates = m_TaxSystem.GetTaxRates(),
                m_RawUpdateFrame = updateFrameWithInterval,
                m_City = m_CitySystem.City,
                m_RandomSeed = RandomSeed.Next(),
                m_FactorQueue = m_FactorQueue.AsParallelWriter(),
                m_StatisticsEventQueue = m_CityStatisticsSystem.GetStatisticsEventQueue(out var deps2).AsParallelWriter(),
                // ADDED
                m_DensityAdjustments = density,
                m_DistrictAdjustments = districts,
                m_Keys = TaxKeyResolver.Create(this),
                m_RateLimits = m_TaxSystem.GetTaxParameterData().m_JobLevelTaxLimits,
            }.ScheduleParallel(m_CitizenQuery, JobHandle.CombineDependencies(dependencies5, dependencies4, JobHandle.CombineDependencies(dependencies2, dependencies3, JobHandle.CombineDependencies(Dependency, dependencies, deps2))));
            // ADDED: after vanilla's last write to the factor array (the update before the switch to this system).
            jobHandle = JobHandle.CombineDependencies(jobHandle, (JobHandle)m_LastDepsField.GetValue(m_Vanilla));
            m_GroundPollutionSystem.AddReader(jobHandle);
            m_AirPollutionSystem.AddReader(jobHandle);
            m_NoisePollutionSystem.AddReader(jobHandle);
            m_TelecomCoverageSystem.AddReader(jobHandle);
            m_LocalEffectSystem.AddLocalEffectReader(jobHandle);
            m_TaxSystem.AddReader(jobHandle);
            m_CityStatisticsSystem.AddWriter(jobHandle);
            density.Dispose(jobHandle);
            districts.Dispose(jobHandle);
            HappinessFactorJob jobData = new HappinessFactorJob
            {
                m_FactorQueue = m_FactorQueue,
                m_HappinessFactors = m_HappinessFactors,
                m_RawUpdateFrame = updateFrameWithInterval,
                m_TriggerActionQueue = m_TriggerSystem.CreateActionBuffer(),
                m_ParameterEntity = m_HappinessFactorParameterQuery.GetSingletonEntity(),
                m_Parameters = GetBufferLookup<HappinessFactorParameterData>(true),
                m_Locked = GetComponentLookup<Locked>(true)
            };
            Dependency = jobData.Schedule(jobHandle);
            // CHANGED: vanilla's m_LastDeps = Dependency; here vanilla's field, so its GetHappinessFactor (the city info
            // UI) completes this job before reading the factors.
            m_LastDepsField.SetValue(m_Vanilla, Dependency);
            m_TriggerSystem.AddActionBufferWriter(Dependency);
        }
    }
}
