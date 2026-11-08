using Colossal.Collections;
using Colossal.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Triggers;
using Game.Vehicles;
using Game.Zones;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;
using static Game.Simulation.HouseholdFindPropertySystem;

namespace FiscalOverhaul
{
    /// <summary>
    /// Households choose homes by the tax they would pay there: a copy of vanilla HouseholdFindPropertySystem's update
    /// whose one change is the score of a home (the current one and the candidate the pathfinder found): vanilla's
    /// <c>PropertyUtils.GetPropertyScore</c>, whose tax term (2 × the adults' average tax happiness bonus) is at the city
    /// rate, corrected to the rate that home is charged, the vanilla rate plus its density and district adjustments
    /// (<see cref="TaxPropertyScoreMath"/>, as <see cref="TaxEngineSystem"/> charges and <see cref="TaxHappinessSystem"/>
    /// rates happiness). A home with no adjustment scores exactly as vanilla.
    ///
    /// Only the update is replaced: while this runs, <c>HouseholdFindPropertySystem.Enabled</c> is false. It touches no
    /// private vanilla state (nothing outside reads vanilla's, only its debug counters). It runs exactly while the tax
    /// engine does (<see cref="TaxEngineGuardLogic.RunsHomeChoice"/>), so the Simulation changes switch and the
    /// game-version guard cover it; anything else turns vanilla's update back on, live. Registered right before vanilla
    /// with the same interval (16), so it inherits its update offset.
    ///
    /// Known limit: the candidates the pathfinder offers are pre-scored in CitizenPathfindSetup.SetupFindHome (inside
    /// PathfindSetupSystem) at the city rate; only the final comparison here (candidate against the current home) is at
    /// the home's rate.
    ///
    /// Copied from Game.Simulation.HouseholdFindPropertySystem, game version 1.6.3f1: PreparePropertyJob (:54-207),
    /// FindPropertyJob (:209-656), the constants (:848-854), OnCreate (:960-1047) and OnUpdate (:1072-1209). Changes are
    /// marked CHANGED or ADDED; the debug counters and distributions (vanilla's DebugWatchValues) are left out.
    /// </summary>
    public partial class TaxFindPropertySystem : GameSystemBase
    {
        // Copied from Game.Simulation.HouseholdFindPropertySystem.PreparePropertyJob (:54-207), game version 1.6.3f1,
        // unchanged.
        [BurstCompile]
        private struct PreparePropertyJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingProperties;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_Prefabs;

            [ReadOnly]
            public BufferLookup<Renter> m_Renters;

            [ReadOnly]
            public ComponentLookup<Abandoned> m_Abandoneds;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Park> m_Parks;

            [ReadOnly]
            public ComponentLookup<BuildingData> m_BuildingDatas;

            [ReadOnly]
            public ComponentLookup<ParkData> m_ParkDatas;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableDatas;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingPropertyData;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> m_ServiceCoverages;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_Crimes;

            [ReadOnly]
            public ComponentLookup<Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<Locked> m_Locked;

            [ReadOnly]
            public BufferLookup<CityModifier> m_CityModifiers;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> m_WaterConsumers;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> m_GarbageProducers;

            [ReadOnly]
            public ComponentLookup<MailProducer> m_MailProducers;

            [ReadOnly]
            public NativeArray<AirPollution> m_AirPollutionMap;

            [ReadOnly]
            public NativeArray<GroundPollution> m_PollutionMap;

            [ReadOnly]
            public NativeArray<NoisePollution> m_NoiseMap;

            [ReadOnly]
            public CellMapData<TelecomCoverage> m_TelecomCoverages;

            public HealthcareParameterData m_HealthcareParameters;

            public ParkParameterData m_ParkParameters;

            public EducationParameterData m_EducationParameters;

            public TelecomParameterData m_TelecomParameters;

            public GarbageParameterData m_GarbageParameters;

            public PoliceConfigurationData m_PoliceParameters;

            public CitizenHappinessParameterData m_CitizenHappinessParameterData;

            public Entity m_City;

            public NativeParallelHashMap<Entity, CachedPropertyInformation>.ParallelWriter m_PropertyData;

            private int CalculateFree(Entity property)
            {
                Entity prefab = m_Prefabs[property].m_Prefab;
                int num = 0;
                if (m_BuildingDatas.HasComponent(prefab) && (m_Abandoneds.HasComponent(property) || (m_Parks.HasComponent(property) && m_ParkDatas[prefab].m_AllowHomeless)))
                {
                    num = BuildingUtils.GetShelterHomelessCapacity(prefab, ref m_BuildingDatas, ref m_BuildingPropertyData) - m_Renters[property].Length;
                }
                else if (m_BuildingProperties.HasComponent(prefab))
                {
                    BuildingPropertyData buildingPropertyData = m_BuildingProperties[prefab];
                    DynamicBuffer<Renter> dynamicBuffer = m_Renters[property];
                    num = buildingPropertyData.CountProperties(AreaType.Residential);
                    for (int i = 0; i < dynamicBuffer.Length; i++)
                    {
                        Entity renter = dynamicBuffer[i].m_Renter;
                        if (m_Households.HasComponent(renter))
                        {
                            num--;
                        }
                    }
                }
                return num;
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    int num = CalculateFree(entity);
                    if (num > 0)
                    {
                        Entity prefab = m_Prefabs[entity].m_Prefab;
                        Building buildingData = m_Buildings[entity];
                        Entity healthcareServicePrefab = m_HealthcareParameters.m_HealthcareServicePrefab;
                        Entity parkServicePrefab = m_ParkParameters.m_ParkServicePrefab;
                        Entity educationServicePrefab = m_EducationParameters.m_EducationServicePrefab;
                        Entity telecomServicePrefab = m_TelecomParameters.m_TelecomServicePrefab;
                        Entity garbageServicePrefab = m_GarbageParameters.m_GarbageServicePrefab;
                        Entity policeServicePrefab = m_PoliceParameters.m_PoliceServicePrefab;
                        DynamicBuffer<CityModifier> cityModifiers = m_CityModifiers[m_City];
                        GenericApartmentQuality genericApartmentQuality = PropertyUtils.GetGenericApartmentQuality(entity, prefab, ref buildingData, ref m_BuildingProperties, ref m_BuildingDatas, ref m_SpawnableDatas, ref m_Crimes, ref m_ServiceCoverages, ref m_Locked, ref m_ElectricityConsumers, ref m_WaterConsumers, ref m_GarbageProducers, ref m_MailProducers, ref m_Transforms, ref m_Abandoneds, m_PollutionMap, m_AirPollutionMap, m_NoiseMap, m_TelecomCoverages, cityModifiers, healthcareServicePrefab, parkServicePrefab, educationServicePrefab, telecomServicePrefab, garbageServicePrefab, policeServicePrefab, m_CitizenHappinessParameterData, m_GarbageParameters);
                        m_PropertyData.TryAdd(entity, new CachedPropertyInformation
                        {
                            free = num,
                            quality = genericApartmentQuality
                        });
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        // Copied from Game.Simulation.HouseholdFindPropertySystem.FindPropertyJob (:209-656), game version 1.6.3f1.
        // Changes: the two PropertyUtils.GetPropertyScore calls (:533, :602) are Score (ADDED), vanilla's score with
        // its tax term at the home's charged rate; the debug counters are left out.
        [BurstCompile]
        private struct FindPropertyJob : IJob
        {
            public NativeList<Entity> m_HomelessHouseholdEntities;

            public NativeList<Entity> m_MovedInHouseholdEntities;

            public NativeParallelHashMap<Entity, CachedPropertyInformation> m_CachedPropertyInfo;

            [ReadOnly]
            public ComponentLookup<BuildingData> m_BuildingDatas;

            [ReadOnly]
            public ComponentLookup<PropertyOnMarket> m_PropertiesOnMarket;

            [ReadOnly]
            public BufferLookup<PathInformations> m_PathInformationBuffers;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_PrefabRefs;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<Worker> m_Workers;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.Student> m_Students;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingProperties;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_PropertyRenters;

            [ReadOnly]
            public BufferLookup<ResourceAvailability> m_Availabilities;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> m_ServiceCoverages;

            [ReadOnly]
            public ComponentLookup<HomelessHousehold> m_HomelessHouseholds;

            [ReadOnly]
            public ComponentLookup<Citizen> m_Citizens;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_Crimes;

            [ReadOnly]
            public ComponentLookup<Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<Locked> m_Lockeds;

            [ReadOnly]
            public BufferLookup<CityModifier> m_CityModifiers;

            [ReadOnly]
            public ComponentLookup<HealthProblem> m_HealthProblems;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Park> m_Parks;

            [ReadOnly]
            public ComponentLookup<Abandoned> m_Abandoneds;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> m_OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> m_WaterConsumers;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> m_GarbageProducers;

            [ReadOnly]
            public ComponentLookup<MailProducer> m_MailProducers;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> m_CurrentBuildings;

            [ReadOnly]
            public ComponentLookup<CurrentTransport> m_CurrentTransports;

            [ReadOnly]
            public ComponentLookup<PathInformation> m_PathInformations;

            [ReadOnly]
            public BufferLookup<HouseholdCitizen> m_CitizenBuffers;

            public ComponentLookup<PropertySeeker> m_PropertySeekers;

            [ReadOnly]
            public NativeArray<AirPollution> m_AirPollutionMap;

            [ReadOnly]
            public NativeArray<GroundPollution> m_PollutionMap;

            [ReadOnly]
            public NativeArray<NoisePollution> m_NoiseMap;

            [ReadOnly]
            public CellMapData<TelecomCoverage> m_TelecomCoverages;

            [ReadOnly]
            public NativeArray<int> m_TaxRates;

            [ReadOnly]
            public CountResidentialPropertySystem.ResidentialPropertyData m_ResidentialPropertyData;

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
            public CitizenHappinessParameterData m_CitizenHappinessParameterData;

            [ReadOnly]
            public EconomyParameterData m_EconomyParameters;

            [ReadOnly]
            public TripPriorityParametersData m_TripPriorityParameters;

            [ReadOnly]
            public uint m_SimulationFrame;

            public EntityCommandBuffer m_CommandBuffer;

            [ReadOnly]
            public Entity m_City;

            public NativeQueue<SetupQueueItem>.ParallelWriter m_PathfindQueue;

            public NativeQueue<RentAction>.ParallelWriter m_RentActionQueue;




                // ADDED: the home's adjustments (the tax engine's, TaxEngineSystem.PayTaxJob.ChargedRate for a household),
            // the job-level limits, and counters for Mod.log (scores, scores the tax term moved, their summed delta).
            [ReadOnly] public NativeArray<int> m_DensityAdjustments;
            [ReadOnly] public NativeParallelHashMap<Entity, int4> m_DistrictAdjustments;
            public TaxKeyResolver m_Keys;
            public int2 m_RateLimits;
            public NativeArray<int> m_Stats;

            /// <summary>
            /// ADDED: vanilla's PropertyUtils.GetPropertyScore for <paramref name="property"/>, its tax term at the rate the
            /// property charges a household (TaxPropertyScoreMath). A shelter (park or abandoned building) houses the
            /// household as homeless, who pays the vanilla rate, so its score is vanilla's; so is any home with no adjustment.
            /// </summary>
            private float Score(Entity property, Entity householdEntity, DynamicBuffer<HouseholdCitizen> citizens)
            {
                float score = PropertyUtils.GetPropertyScore(property, householdEntity, citizens, ref m_PrefabRefs, ref m_BuildingProperties, ref m_Buildings, ref m_BuildingDatas, ref m_Households, ref m_Citizens, ref m_Students, ref m_Workers, ref m_SpawnableDatas, ref m_Crimes, ref m_ServiceCoverages, ref m_Lockeds, ref m_ElectricityConsumers, ref m_WaterConsumers, ref m_GarbageProducers, ref m_MailProducers, ref m_Transforms, ref m_Abandoneds, ref m_Parks, ref m_Availabilities, m_TaxRates, m_PollutionMap, m_AirPollutionMap, m_NoiseMap, m_TelecomCoverages, m_CityModifiers[m_City], m_HealthcareParameters.m_HealthcareServicePrefab, m_ParkParameters.m_ParkServicePrefab, m_EducationParameters.m_EducationServicePrefab, m_TelecomParameters.m_TelecomServicePrefab, m_GarbageParameters.m_GarbageServicePrefab, m_PoliceParameters.m_PoliceServicePrefab, m_CitizenHappinessParameterData, m_GarbageParameters);
                m_Stats[0]++;
                if (float.IsNegativeInfinity(score) || BuildingUtils.IsHomelessShelterBuilding(property, ref m_Parks, ref m_Abandoneds)) return score;

                int density = 0;
                if (m_Keys.TryDensityCell(property, true, out int cell)) density = m_DensityAdjustments[cell];
                int district = 0;
                if (!m_DistrictAdjustments.IsEmpty)
                {
                    Entity districtEntity = m_Keys.District(property);
                    if (districtEntity != Entity.Null && m_DistrictAdjustments.TryGetValue(districtEntity, out int4 adjustments))
                        district = adjustments[(int)TaxZone.Residential];
                }
                if (density == 0 && district == 0) return score;

                // The adults' tax bonuses (PropertyUtils.cs:512-525), at the city rate and at this home's rate.
                DynamicBuffer<CityModifier> cityModifiers = m_CityModifiers[m_City];
                float2 modifier = CityUtils.GetModifier(cityModifiers, CityModifierType.TaxHappiness);
                var term = new TaxPropertyScoreMath();
                for (int i = 0; i < citizens.Length; i++)
                {
                    Citizen citizen = m_Citizens[citizens[i].m_Citizen];
                    if (citizen.GetAge() == CitizenAge.Child) continue;
                    int educationLevel = citizen.GetEducationLevel();
                    int vanillaBonus = CitizenHappinessSystem.GetTaxBonuses(educationLevel, m_TaxRates, cityModifiers, in m_CitizenHappinessParameterData).y;
                    int vanillaRate = TaxSystem.GetResidentialTaxRate(educationLevel, m_TaxRates);
                    int homeRate = TaxHappinessMath.ResidentRate(vanillaRate, density, district, m_RateLimits.x, m_RateLimits.y);
                    int homeBonus = homeRate == vanillaRate
                        ? vanillaBonus
                        : TaxHappinessMath.Bonus(homeRate, educationLevel, m_CitizenHappinessParameterData.m_TaxUneducatedMultiplier, m_CitizenHappinessParameterData.m_TaxPoorlyEducatedMultiplier, m_CitizenHappinessParameterData.m_TaxEducatedMultiplier, m_CitizenHappinessParameterData.m_TaxWellEducatedMultiplier, m_CitizenHappinessParameterData.m_TaxHighlyEducatedMultiplier, modifier.x, modifier.y);
                    term.AddAdult(vanillaBonus, homeBonus);
                }
                int delta = term.ScoreDelta();
                if (delta != 0)
                {
                    m_Stats[1]++;
                    m_Stats[2] += delta;
                }
                return term.Adjust(score);
            }

        private void StartHomeFinding(Entity household, Entity commuteCitizen, Entity targetLocation, Entity oldHome, float minimumScore, bool targetIsOrigin, DynamicBuffer<HouseholdCitizen> citizens)
            {
                m_CommandBuffer.AddComponent(household, new PathInformation
                {
                    m_State = PathFlags.Pending
                });
                Household household2 = m_Households[household];
                PathfindWeights weights = default;
                if (m_Citizens.TryGetComponent(commuteCitizen, out var componentData))
                {
                    weights = CitizenUtils.GetPathfindWeights(componentData, household2, citizens.Length);
                }
                else
                {
                    for (int i = 0; i < citizens.Length; i++)
                    {
                        weights.m_Value += CitizenUtils.GetPathfindWeights(componentData, household2, citizens.Length).m_Value;
                    }
                    weights.m_Value *= 1f / (float)citizens.Length;
                }
                PathfindParameters parameters = new PathfindParameters
                {
                    m_MaxSpeed = 111.111115f,
                    m_WalkSpeed = 1.6666667f,
                    m_Weights = weights,
                    m_Methods = (PathMethod.Pedestrian | PathMethod.PublicTransportDay),
                    m_MaxCost = m_TripPriorityParameters.GetMaxCost(m_TripPriorityParameters.m_PriorityGoingHome),
                    m_PathfindFlags = (PathfindFlags.Simplified | PathfindFlags.IgnorePath)
                };
                SetupQueueTarget a = new SetupQueueTarget
                {
                    m_Type = SetupTargetType.CurrentLocation,
                    m_Methods = PathMethod.Pedestrian,
                    m_Entity = targetLocation
                };
                SetupQueueTarget b = new SetupQueueTarget
                {
                    m_Type = SetupTargetType.FindHome,
                    m_Methods = PathMethod.Pedestrian,
                    m_Entity = household,
                    m_Entity2 = oldHome,
                    m_Value2 = minimumScore
                };
                if (m_OwnedVehicles.TryGetBuffer(household, out var bufferData) && bufferData.Length != 0)
                {
                    parameters.m_Methods |= (PathMethod)(targetIsOrigin ? 8194 : 8198);
                    parameters.m_ParkingSize = float.MinValue;
                    parameters.m_IgnoredRules |= RuleFlags.ForbidCombustionEngines | RuleFlags.ForbidHeavyTraffic | RuleFlags.ForbidSlowTraffic | RuleFlags.AvoidBicycles;
                    a.m_Methods |= PathMethod.Road | PathMethod.MediumRoad;
                    a.m_RoadTypes |= RoadTypes.Car;
                    b.m_Methods |= PathMethod.Road | PathMethod.MediumRoad;
                    b.m_RoadTypes |= RoadTypes.Car;
                }
                if (targetIsOrigin)
                {
                    parameters.m_MaxSpeed.y = 277.77777f;
                    parameters.m_Methods |= PathMethod.Taxi | PathMethod.PublicTransportNight;
                    parameters.m_TaxiIgnoredRules = VehicleUtils.GetIgnoredPathfindRulesTaxiDefaults();
                }
                else
                {
                    CommonUtils.Swap(ref a, ref b);
                }
                parameters.m_MaxResultCount = 10;
                parameters.m_PathfindFlags |= (PathfindFlags)(targetIsOrigin ? 256 : 128);
                m_CommandBuffer.AddBuffer<PathInformations>(household).Add(new PathInformations
                {
                    m_State = PathFlags.Pending
                });
                SetupQueueItem value = new SetupQueueItem(household, parameters, a, b);
                m_PathfindQueue.Enqueue(value);
            }

            private Entity GetFirstWorkplaceOrSchool(DynamicBuffer<HouseholdCitizen> citizens, ref Entity citizen)
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    citizen = citizens[i].m_Citizen;
                    if (m_Workers.HasComponent(citizen))
                    {
                        return m_Workers[citizen].m_Workplace;
                    }
                    if (m_Students.HasComponent(citizen))
                    {
                        return m_Students[citizen].m_School;
                    }
                }
                return Entity.Null;
            }

            private Entity GetCurrentLocation(DynamicBuffer<HouseholdCitizen> citizens)
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    if (m_CurrentBuildings.TryGetComponent(citizens[i].m_Citizen, out var componentData))
                    {
                        return componentData.m_CurrentBuilding;
                    }
                    if (m_CurrentTransports.TryGetComponent(citizens[i].m_Citizen, out var componentData2))
                    {
                        return componentData2.m_CurrentTransport;
                    }
                }
                return Entity.Null;
            }

            public void Execute()
            {
                int num = 0;
                for (int i = 0; i < m_HomelessHouseholdEntities.Length; i++)
                {
                    Entity householdEntity = m_HomelessHouseholdEntities[i];
                    if (ProcessFindHome(householdEntity))
                    {
                        num++;
                    }
                    if (num >= kMaxProcessHomelessHouseholdPerUpdate)
                    {
                        break;
                    }
                }
                num = 0;
                for (int j = 0; j < m_MovedInHouseholdEntities.Length; j++)
                {
                    Entity householdEntity2 = m_MovedInHouseholdEntities[j];
                    if (ProcessFindHome(householdEntity2))
                    {
                        num++;
                    }
                    if (num >= kMaxProcessNormalHouseholdPerUpdate)
                    {
                        break;
                    }
                }
            }

            private bool ProcessFindHome(Entity householdEntity)
            {
                DynamicBuffer<HouseholdCitizen> dynamicBuffer = m_CitizenBuffers[householdEntity];
                if (dynamicBuffer.Length == 0)
                {
                    return false;
                }
                PropertySeeker propertySeeker = m_PropertySeekers[householdEntity];
                int householdIncome = EconomyUtils.GetHouseholdIncome(dynamicBuffer, ref m_Workers, ref m_Citizens, ref m_HealthProblems, ref m_EconomyParameters, m_TaxRates);
                if (m_PathInformationBuffers.TryGetBuffer(householdEntity, out var bufferData))
                {
                    ProcessPathInformations(householdEntity, bufferData, propertySeeker, dynamicBuffer, householdIncome);
                    return false;
                }
                Entity householdHomeBuilding = BuildingUtils.GetHouseholdHomeBuilding(householdEntity, ref m_PropertyRenters, ref m_HomelessHouseholds);
                // CHANGED: vanilla PropertyUtils.GetPropertyScore (:533), its tax term at the current home's rate.
                float bestPropertyScore = ((householdHomeBuilding != Entity.Null) ? Score(householdHomeBuilding, householdEntity, dynamicBuffer) : float.NegativeInfinity);
                if (householdHomeBuilding == Entity.Null && propertySeeker.m_LastPropertySeekFrame + kFindPropertyCoolDown > m_SimulationFrame)
                {
                    if (m_PathInformations[householdEntity].m_State != PathFlags.Pending && math.csum(m_ResidentialPropertyData.m_FreeProperties) < m_EconomyParameters.m_MinFreeResidentialProperties)
                    {
                        CitizenUtils.HouseholdMoveAway(m_CommandBuffer, householdEntity);
                    }
                    return false;
                }
                Entity citizen = Entity.Null;
                Entity firstWorkplaceOrSchool = GetFirstWorkplaceOrSchool(dynamicBuffer, ref citizen);
                bool flag = firstWorkplaceOrSchool == Entity.Null;
                Entity entity = (flag ? GetCurrentLocation(dynamicBuffer) : firstWorkplaceOrSchool);
                if (householdHomeBuilding == Entity.Null && entity == Entity.Null)
                {
                    CitizenUtils.HouseholdMoveAway(m_CommandBuffer, householdEntity);
                    return false;
                }
                propertySeeker.m_TargetProperty = firstWorkplaceOrSchool;
                propertySeeker.m_BestProperty = householdHomeBuilding;
                propertySeeker.m_BestPropertyScore = bestPropertyScore;
                propertySeeker.m_LastPropertySeekFrame = m_SimulationFrame;
                m_PropertySeekers[householdEntity] = propertySeeker;
                if (m_HomelessHouseholds.HasComponent(householdEntity))
                {
                }
                else
                {
                }
                StartHomeFinding(householdEntity, citizen, entity, householdHomeBuilding, propertySeeker.m_BestPropertyScore, flag, dynamicBuffer);
                return true;
            }

            private void ProcessPathInformations(Entity householdEntity, DynamicBuffer<PathInformations> pathInformations, PropertySeeker propertySeeker, DynamicBuffer<HouseholdCitizen> citizens, int income)
            {
                int num = 0;
                PathInformations pathInformations2 = pathInformations[num];
                if ((pathInformations2.m_State & PathFlags.Pending) != 0)
                {
                    return;
                }
                m_CommandBuffer.RemoveComponent<PathInformations>(householdEntity);
                bool flag = propertySeeker.m_TargetProperty != Entity.Null;
                Entity entity = (flag ? pathInformations2.m_Origin : pathInformations2.m_Destination);
                bool flag2 = false;
                while (!m_CachedPropertyInfo.ContainsKey(entity) || m_CachedPropertyInfo[entity].free <= 0)
                {
                    num++;
                    if (pathInformations.Length > num)
                    {
                        pathInformations2 = pathInformations[num];
                        entity = (flag ? pathInformations2.m_Origin : pathInformations2.m_Destination);
                        continue;
                    }
                    entity = Entity.Null;
                    flag2 = true;
                    break;
                }
                if (flag2 && pathInformations.Length != 0 && pathInformations[0].m_Destination != Entity.Null)
                {
                    return;
                }
                float num2 = float.NegativeInfinity;
                if (entity != Entity.Null && m_CachedPropertyInfo.ContainsKey(entity) && m_CachedPropertyInfo[entity].free > 0)
                {
                    // CHANGED: vanilla PropertyUtils.GetPropertyScore (:602), its tax term at this home's rate.
                    num2 = Score(entity, householdEntity, citizens);
                }
                if (num2 < propertySeeker.m_BestPropertyScore)
                {
                    entity = propertySeeker.m_BestProperty;
                }
                bool flag3 = (m_Households[householdEntity].m_Flags & HouseholdFlags.MovedIn) != 0;
                bool flag4 = entity != Entity.Null && BuildingUtils.IsHomelessShelterBuilding(entity, ref m_Parks, ref m_Abandoneds);
                bool flag5 = CitizenUtils.IsHouseholdNeedSupport(citizens, ref m_Citizens, ref m_Students);
                bool flag6 = m_PropertiesOnMarket.HasComponent(entity) && (flag5 || m_PropertiesOnMarket[entity].m_AskingRent < income);
                bool flag7 = !m_PropertyRenters.HasComponent(householdEntity) || !m_PropertyRenters[householdEntity].m_Property.Equals(entity);
                Entity householdHomeBuilding = BuildingUtils.GetHouseholdHomeBuilding(householdEntity, ref m_PropertyRenters, ref m_HomelessHouseholds);
                if (householdHomeBuilding != Entity.Null && householdHomeBuilding == entity)
                {
                    if (!m_HomelessHouseholds.HasComponent(householdEntity) && !flag5 && income < m_PropertyRenters[householdEntity].m_Rent)
                    {
                        CitizenUtils.HouseholdMoveAway(m_CommandBuffer, householdEntity, MoveAwayReason.NoMoney);
                    }
                    else
                    {
                        m_CommandBuffer.SetComponentEnabled<PropertySeeker>(householdEntity, value: false);
                    }
                }
                else if ((flag6 & flag7) || (flag3 & flag4))
                {
                    m_RentActionQueue.Enqueue(new RentAction
                    {
                        m_Property = entity,
                        m_Renter = householdEntity
                    });
                    if (m_CachedPropertyInfo.ContainsKey(entity))
                    {
                        CachedPropertyInformation value = m_CachedPropertyInfo[entity];
                        value.free--;
                        m_CachedPropertyInfo[entity] = value;
                    }
                    m_CommandBuffer.SetComponentEnabled<PropertySeeker>(householdEntity, value: false);
                }
                else if (entity == Entity.Null && (!m_HomelessHouseholds.HasComponent(householdEntity) || m_HomelessHouseholds[householdEntity].m_TempHome == Entity.Null))
                {
                    CitizenUtils.HouseholdMoveAway(m_CommandBuffer, householdEntity);
                }
                else
                {
                    propertySeeker.m_BestProperty = default;
                    propertySeeker.m_BestPropertyScore = float.NegativeInfinity;
                    m_PropertySeekers[householdEntity] = propertySeeker;
                }
            }
        }

        // Copied from HouseholdFindPropertySystem (:848-854), game version 1.6.3f1: the update interval is
        // GetUpdateInterval below; the per-update caps and the cool-down are vanilla's public statics, read from there.

        private EntityQuery m_HouseholdQuery;
        private EntityQuery m_HomelessHouseholdQuery;
        private EntityQuery m_FreePropertyQuery;
        private EntityQuery m_EconomyParameterQuery;
        private EntityQuery m_DemandParameterQuery;
        private EntityQuery m_TripPriorityParametersQuery;
        private EndFrameBarrier m_EndFrameBarrier;
        private PathfindSetupSystem m_PathfindSetupSystem;
        private TaxSystem m_TaxSystem;
        private TriggerSystem m_TriggerSystem;
        private GroundPollutionSystem m_GroundPollutionSystem;
        private AirPollutionSystem m_AirPollutionSystem;
        private NoisePollutionSystem m_NoisePollutionSystem;
        private TelecomCoverageSystem m_TelecomCoverageSystem;
        private CitySystem m_CitySystem;
        private CityStatisticsSystem m_CityStatisticsSystem;
        private SimulationSystem m_SimulationSystem;
        private PropertyProcessingSystem m_PropertyProcessingSystem;
        private CountResidentialPropertySystem m_CountResidentialPropertySystem;
        private EntityQuery m_HealthcareParameterQuery;
        private EntityQuery m_ParkParameterQuery;
        private EntityQuery m_EducationParameterQuery;
        private EntityQuery m_TelecomParameterQuery;
        private EntityQuery m_GarbageParameterQuery;
        private EntityQuery m_PoliceParameterQuery;
        private EntityQuery m_CitizenHappinessParameterQuery;

        // ADDED: vanilla's system, whose update this replaces, and the tax engine, whose decision this follows; the
        // Mod.log counters (FindPropertyJob.m_Stats) and the handle of the last job that wrote them.
        private const int kStatsLogEveryUpdates = 512;
        private HouseholdFindPropertySystem m_Vanilla;
        private TaxEngineSystem m_TaxEngine;
        private NativeArray<int> m_Stats;
        private JobHandle m_StatsHandle;
        private int m_UpdatesSinceLog;
        private bool m_Ready;
        private bool m_WeDisabledVanilla;
        private bool m_ForeignDisabled;
        private bool m_Logged;

        /// <summary>Whether this system runs home finding right now (vanilla HouseholdFindPropertySystem's update is off).</summary>
        public bool Running { get; private set; }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Same as HouseholdFindPropertySystem (UPDATE_INTERVAL, :848), so this inherits its update offset.
            return 16;
        }

        // Copied from Game.Simulation.HouseholdFindPropertySystem.OnCreate (:960-1047), game version 1.6.3f1. Changes:
        // no debug data; vanilla's system, the tax engine and the stats array (ADDED).
        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_PathfindSetupSystem = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_GroundPollutionSystem = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            m_AirPollutionSystem = World.GetOrCreateSystemManaged<AirPollutionSystem>();
            m_NoisePollutionSystem = World.GetOrCreateSystemManaged<NoisePollutionSystem>();
            m_TelecomCoverageSystem = World.GetOrCreateSystemManaged<TelecomCoverageSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_TriggerSystem = World.GetOrCreateSystemManaged<TriggerSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PropertyProcessingSystem = World.GetOrCreateSystemManaged<PropertyProcessingSystem>();
            m_CountResidentialPropertySystem = World.GetOrCreateSystemManaged<CountResidentialPropertySystem>();
            m_HomelessHouseholdQuery = GetEntityQuery(ComponentType.ReadWrite<HomelessHousehold>(), ComponentType.ReadWrite<PropertySeeker>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<MovingAway>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<CurrentBuilding>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_HouseholdQuery = GetEntityQuery(ComponentType.ReadWrite<Household>(), ComponentType.ReadWrite<PropertySeeker>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<HomelessHousehold>(), ComponentType.Exclude<MovingAway>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<CurrentBuilding>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_DemandParameterQuery = GetEntityQuery(ComponentType.ReadOnly<DemandParameterData>());
            m_HealthcareParameterQuery = GetEntityQuery(ComponentType.ReadOnly<HealthcareParameterData>());
            m_ParkParameterQuery = GetEntityQuery(ComponentType.ReadOnly<ParkParameterData>());
            m_EducationParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EducationParameterData>());
            m_TelecomParameterQuery = GetEntityQuery(ComponentType.ReadOnly<TelecomParameterData>());
            m_GarbageParameterQuery = GetEntityQuery(ComponentType.ReadOnly<GarbageParameterData>());
            m_PoliceParameterQuery = GetEntityQuery(ComponentType.ReadOnly<PoliceConfigurationData>());
            m_CitizenHappinessParameterQuery = GetEntityQuery(ComponentType.ReadOnly<CitizenHappinessParameterData>());
            EntityQueryDesc entityQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[1] { ComponentType.ReadOnly<Building>() },
                Any = new ComponentType[2]
                {
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.ReadOnly<Game.Buildings.Park>()
                },
                None = new ComponentType[3]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Temp>()
                }
            };
            EntityQueryDesc entityQueryDesc2 = new EntityQueryDesc
            {
                All = new ComponentType[3]
                {
                    ComponentType.ReadOnly<PropertyOnMarket>(),
                    ComponentType.ReadOnly<ResidentialProperty>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new ComponentType[5]
                {
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Condemned>()
                }
            };
            m_FreePropertyQuery = GetEntityQuery(entityQueryDesc, entityQueryDesc2);
            m_TripPriorityParametersQuery = GetEntityQuery(ComponentType.ReadOnly<TripPriorityParametersData>());
            RequireForUpdate(m_EconomyParameterQuery);
            RequireForUpdate(m_HealthcareParameterQuery);
            RequireForUpdate(m_ParkParameterQuery);
            RequireForUpdate(m_EducationParameterQuery);
            RequireForUpdate(m_TelecomParameterQuery);
            RequireForUpdate(m_HouseholdQuery);
            RequireForUpdate(m_DemandParameterQuery);
            RequireForUpdate(m_TripPriorityParametersQuery);
            // ADDED: the tax engine's rate limits (TaxSystem.GetTaxParameterData).
            RequireForUpdate<TaxParameterData>();

            // ADDED
            m_Vanilla = World.GetOrCreateSystemManaged<HouseholdFindPropertySystem>();
            m_TaxEngine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_Stats = new NativeArray<int>(3, Allocator.Persistent);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            // Leave vanilla finding homes.
            if (m_WeDisabledVanilla && m_Vanilla != null) m_Vanilla.Enabled = true;
            m_WeDisabledVanilla = false;
            Dependency.Complete();
            m_StatsHandle.Complete();
            m_Stats.Dispose();
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
        /// Follows the tax engine's decision (<see cref="TaxEngineGuardLogic.RunsHomeChoice"/>) and turns vanilla
        /// HouseholdFindPropertySystem's update off or back on to match. Called every update and whenever Options change
        /// (after the engine's own ApplyMode).
        /// </summary>
        public void ApplyMode()
        {
            if (!m_Ready || m_Vanilla == null || m_TaxEngine == null) return;
            if (!m_WeDisabledVanilla && !m_Vanilla.Enabled && !m_ForeignDisabled)
            {
                m_ForeignDisabled = true;
                Mod.log.Warn("TaxHomeChoice: vanilla HouseholdFindPropertySystem was already disabled (another mod?); home choice stays vanilla and leaves it alone");
            }
            bool running = TaxEngineGuardLogic.RunsHomeChoice(m_TaxEngine.Mode, m_ForeignDisabled);

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
                Mod.log.Info($"TaxHomeChoice: {(running ? "running" : "vanilla")} (tax engine {m_TaxEngine.Mode}, foreign disabled {m_ForeignDisabled}); vanilla HouseholdFindPropertySystem {(m_Vanilla.Enabled ? "enabled" : "disabled")}");
                m_Logged = true;
            }
            Running = running;
        }

        /// <summary>ADDED: every ~512 updates, logs how many home scores the tax term moved since the last line, and resets.</summary>
        private void LogStats()
        {
            if (++m_UpdatesSinceLog < kStatsLogEveryUpdates) return;
            m_UpdatesSinceLog = 0;
            m_StatsHandle.Complete();
            if (m_Stats[0] > 0)
            {
                Mod.log.Info($"TaxHomeChoice: {m_Stats[1]} of {m_Stats[0]} home scores moved by the home's tax rate (summed delta {m_Stats[2]})");
            }
            m_Stats[0] = 0;
            m_Stats[1] = 0;
            m_Stats[2] = 0;
        }

        // Copied from Game.Simulation.HouseholdFindPropertySystem.OnUpdate (:1072-1209), game version 1.6.3f1. Changes:
        // the mode check first; type handles and lookups from SystemBase instead of the generated TypeHandle; no debug
        // counters or SumDebugCountersJob; the home adjustments, rate limits, key lookups and stats passed to the job.
        [Preserve]
        protected override void OnUpdate()
        {
            ApplyMode();
            if (!Running) return;
            LogStats();

            NativeParallelHashMap<Entity, CachedPropertyInformation> cachedPropertyInfo = new NativeParallelHashMap<Entity, CachedPropertyInformation>(m_FreePropertyQuery.CalculateEntityCount(), Allocator.TempJob);
            NativeArray<GroundPollution> map = m_GroundPollutionSystem.GetMap(readOnly: true, out var dependencies);
            NativeArray<AirPollution> map2 = m_AirPollutionSystem.GetMap(readOnly: true, out var dependencies2);
            NativeArray<NoisePollution> map3 = m_NoisePollutionSystem.GetMap(readOnly: true, out var dependencies3);
            CellMapData<TelecomCoverage> data = m_TelecomCoverageSystem.GetData(readOnly: true, out var dependencies4);
            PreparePropertyJob jobData = new PreparePropertyJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_BuildingProperties = GetComponentLookup<BuildingPropertyData>(false),
                m_Prefabs = GetComponentLookup<PrefabRef>(true),
                m_BuildingDatas = GetComponentLookup<BuildingData>(true),
                m_ParkDatas = GetComponentLookup<ParkData>(true),
                m_Renters = GetBufferLookup<Renter>(true),
                m_Households = GetComponentLookup<Household>(true),
                m_Abandoneds = GetComponentLookup<Abandoned>(true),
                m_Parks = GetComponentLookup<Game.Buildings.Park>(true),
                m_SpawnableDatas = GetComponentLookup<SpawnableBuildingData>(true),
                m_BuildingPropertyData = GetComponentLookup<BuildingPropertyData>(true),
                m_Buildings = GetComponentLookup<Building>(true),
                m_ServiceCoverages = GetBufferLookup<Game.Net.ServiceCoverage>(true),
                m_Crimes = GetComponentLookup<CrimeProducer>(true),
                m_Locked = GetComponentLookup<Locked>(true),
                m_Transforms = GetComponentLookup<Transform>(true),
                m_CityModifiers = GetBufferLookup<CityModifier>(true),
                m_ElectricityConsumers = GetComponentLookup<ElectricityConsumer>(true),
                m_WaterConsumers = GetComponentLookup<WaterConsumer>(true),
                m_GarbageProducers = GetComponentLookup<GarbageProducer>(true),
                m_MailProducers = GetComponentLookup<MailProducer>(true),
                m_PollutionMap = map,
                m_AirPollutionMap = map2,
                m_NoiseMap = map3,
                m_TelecomCoverages = data,
                m_HealthcareParameters = m_HealthcareParameterQuery.GetSingleton<HealthcareParameterData>(),
                m_ParkParameters = m_ParkParameterQuery.GetSingleton<ParkParameterData>(),
                m_EducationParameters = m_EducationParameterQuery.GetSingleton<EducationParameterData>(),
                m_TelecomParameters = m_TelecomParameterQuery.GetSingleton<TelecomParameterData>(),
                m_GarbageParameters = m_GarbageParameterQuery.GetSingleton<GarbageParameterData>(),
                m_PoliceParameters = m_PoliceParameterQuery.GetSingleton<PoliceConfigurationData>(),
                m_CitizenHappinessParameterData = m_CitizenHappinessParameterQuery.GetSingleton<CitizenHappinessParameterData>(),
                m_City = m_CitySystem.City,
                m_PropertyData = cachedPropertyInfo.AsParallelWriter()
            };
            // ADDED: the job gets its own copies of the adjustments, freed with it (as the tax engine).
            var density = new NativeArray<int>(DensityTaxState.Snapshot(), Allocator.TempJob);
            NativeParallelHashMap<Entity, int4> districts = TaxEngineSystem.BuildDistrictAdjustments();
            FindPropertyJob jobData2 = new FindPropertyJob
            {
                m_HomelessHouseholdEntities = m_HomelessHouseholdQuery.ToEntityListAsync(World.UpdateAllocator.ToAllocator, out var outJobHandle),
                m_MovedInHouseholdEntities = m_HouseholdQuery.ToEntityListAsync(World.UpdateAllocator.ToAllocator, out var outJobHandle2),
                m_CachedPropertyInfo = cachedPropertyInfo,
                m_BuildingDatas = GetComponentLookup<BuildingData>(true),
                m_PropertiesOnMarket = GetComponentLookup<PropertyOnMarket>(true),
                m_Availabilities = GetBufferLookup<ResourceAvailability>(true),
                m_SpawnableDatas = GetComponentLookup<SpawnableBuildingData>(true),
                m_BuildingProperties = GetComponentLookup<BuildingPropertyData>(true),
                m_Buildings = GetComponentLookup<Building>(true),
                m_PathInformationBuffers = GetBufferLookup<PathInformations>(true),
                m_PrefabRefs = GetComponentLookup<PrefabRef>(true),
                m_ServiceCoverages = GetBufferLookup<Game.Net.ServiceCoverage>(true),
                m_Workers = GetComponentLookup<Worker>(true),
                m_Students = GetComponentLookup<Game.Citizens.Student>(true),
                m_PropertyRenters = GetComponentLookup<PropertyRenter>(true),
                m_HomelessHouseholds = GetComponentLookup<HomelessHousehold>(true),
                m_Citizens = GetComponentLookup<Citizen>(true),
                m_Crimes = GetComponentLookup<CrimeProducer>(true),
                m_Lockeds = GetComponentLookup<Locked>(true),
                m_Transforms = GetComponentLookup<Transform>(true),
                m_CityModifiers = GetBufferLookup<CityModifier>(true),
                m_HealthProblems = GetComponentLookup<HealthProblem>(true),
                m_Abandoneds = GetComponentLookup<Abandoned>(true),
                m_Parks = GetComponentLookup<Game.Buildings.Park>(true),
                m_OwnedVehicles = GetBufferLookup<OwnedVehicle>(true),
                m_ElectricityConsumers = GetComponentLookup<ElectricityConsumer>(true),
                m_WaterConsumers = GetComponentLookup<WaterConsumer>(true),
                m_GarbageProducers = GetComponentLookup<GarbageProducer>(true),
                m_MailProducers = GetComponentLookup<MailProducer>(true),
                m_Households = GetComponentLookup<Household>(true),
                m_CurrentBuildings = GetComponentLookup<CurrentBuilding>(true),
                m_CurrentTransports = GetComponentLookup<CurrentTransport>(true),
                m_PathInformations = GetComponentLookup<PathInformation>(true),
                m_CitizenBuffers = GetBufferLookup<HouseholdCitizen>(true),
                m_PropertySeekers = GetComponentLookup<PropertySeeker>(false),
                m_PollutionMap = map,
                m_AirPollutionMap = map2,
                m_NoiseMap = map3,
                m_TelecomCoverages = data,
                m_ResidentialPropertyData = m_CountResidentialPropertySystem.GetResidentialPropertyData(),
                m_HealthcareParameters = m_HealthcareParameterQuery.GetSingleton<HealthcareParameterData>(),
                m_ParkParameters = m_ParkParameterQuery.GetSingleton<ParkParameterData>(),
                m_EducationParameters = m_EducationParameterQuery.GetSingleton<EducationParameterData>(),
                m_TelecomParameters = m_TelecomParameterQuery.GetSingleton<TelecomParameterData>(),
                m_GarbageParameters = m_GarbageParameterQuery.GetSingleton<GarbageParameterData>(),
                m_PoliceParameters = m_PoliceParameterQuery.GetSingleton<PoliceConfigurationData>(),
                m_CitizenHappinessParameterData = m_CitizenHappinessParameterQuery.GetSingleton<CitizenHappinessParameterData>(),
                m_TaxRates = m_TaxSystem.GetTaxRates(),
                m_EconomyParameters = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>(),
                m_TripPriorityParameters = m_TripPriorityParametersQuery.GetSingleton<TripPriorityParametersData>(),
                m_SimulationFrame = m_SimulationSystem.frameIndex,
                m_RentActionQueue = m_PropertyProcessingSystem.GetRentActionQueue(out var deps).AsParallelWriter(),
                m_City = m_CitySystem.City,
                m_PathfindQueue = m_PathfindSetupSystem.GetQueue(this, 80, 16).AsParallelWriter(),
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer(),
                // ADDED
                m_DensityAdjustments = density,
                m_DistrictAdjustments = districts,
                m_Keys = TaxKeyResolver.Create(this),
                m_RateLimits = m_TaxSystem.GetTaxParameterData().m_JobLevelTaxLimits,
                m_Stats = m_Stats,
            };
            JobHandle job = JobChunkExtensions.ScheduleParallel(jobData, m_FreePropertyQuery, JobUtils.CombineDependencies(Dependency, dependencies, dependencies3, dependencies2, dependencies4, deps));
            // CHANGED: also after the last job that wrote the stats (ADDED).
            Dependency = IJobExtensions.Schedule(jobData2, JobHandle.CombineDependencies(job, JobHandle.CombineDependencies(outJobHandle2, outJobHandle, m_StatsHandle)));
            m_StatsHandle = Dependency;
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
            m_PathfindSetupSystem.AddQueueWriter(Dependency);
            m_AirPollutionSystem.AddReader(Dependency);
            m_NoisePollutionSystem.AddReader(Dependency);
            m_GroundPollutionSystem.AddReader(Dependency);
            m_TelecomCoverageSystem.AddReader(Dependency);
            m_TriggerSystem.AddActionBufferWriter(Dependency);
            m_CityStatisticsSystem.AddWriter(Dependency);
            m_TaxSystem.AddReader(Dependency);
            cachedPropertyInfo.Dispose(Dependency);
            // ADDED
            density.Dispose(Dependency);
            districts.Dispose(Dependency);
        }
    }
}
