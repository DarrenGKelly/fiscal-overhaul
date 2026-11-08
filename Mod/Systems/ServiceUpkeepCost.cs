using System;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace FiscalOverhaul
{
    /// <summary>A (service prefab, district) pair; <see cref="Entity.Null"/> district = outside any district.</summary>
    public struct ServiceDistrictKey : IEquatable<ServiceDistrictKey>
    {
        public Entity Service;
        public Entity District;

        public ServiceDistrictKey(Entity service, Entity district)
        {
            Service = service;
            District = district;
        }

        public bool Equals(ServiceDistrictKey other) => Service.Equals(other.Service) && District.Equals(other.District);
        public override bool Equals(object obj) => obj is ServiceDistrictKey other && Equals(other);
        public override int GetHashCode() => (Service.GetHashCode() * 397) ^ District.GetHashCode();
    }

    /// <summary>A (service prefab, budget %) pair: the buildings of one service running at one %.</summary>
    public struct ServiceBudgetKey : IEquatable<ServiceBudgetKey>
    {
        public Entity Service;
        public int Budget;

        public ServiceBudgetKey(Entity service, int budget)
        {
            Service = service;
            Budget = budget;
        }

        public bool Equals(ServiceBudgetKey other) => Service.Equals(other.Service) && Budget == other.Budget;
        public override bool Equals(object obj) => obj is ServiceBudgetKey other && Equals(other);
        public override int GetHashCode() => (Service.GetHashCode() * 397) ^ Budget;
    }

    /// <summary>
    /// A service building's upkeep, as vanilla's CityServiceBudgetSystem building job counts it; Burst-callable, for
    /// any job over the upkeep group (CityServiceUpkeep + PrefabRef). Also the building's district and its city budget %.
    /// </summary>
    public static class ServiceUpkeepCost
    {
        /// <summary>
        /// The building's money upkeep at 100% (wages included, the CityServiceBuildingBaseUpkeepCost modifier applied,
        /// a tenth while inactive), summed the vanilla way: one rounded value per money upkeep entry. That is this
        /// building's share of the service's money <c>m_FullCost</c>.
        /// </summary>
        public static int MoneyFullCost(Entity entity, Entity prefab, bool inactive, NativeList<ServiceUpkeepData> scratch,
            ref BufferLookup<ServiceUpkeepData> serviceUpkeepDatas, ref BufferLookup<InstalledUpgrade> installedUpgrades,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ServiceUsage> serviceUsages,
            ref BufferLookup<Employee> employees, ref ComponentLookup<ResourceData> resourceDatas, ResourcePrefabs resourcePrefabs,
            EconomyParameterData economyParameters, DynamicBuffer<CityModifier> cityModifiers)
        {
            if (!serviceUpkeepDatas.HasBuffer(prefab)) return 0;
            scratch.Clear();
            CityServiceUpkeepSystem.GetUpkeepWithUsageScale(scratch, serviceUpkeepDatas, installedUpgrades, prefabRefs, serviceUsages, entity, prefab, inactive);
            int wages = CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage(employees, entity, economyParameters, inactive);
            int total = 0;
            for (int j = 0; j < scratch.Length; j++)
            {
                ServiceUpkeepData upkeep = scratch[j];
                if (upkeep.m_Upkeep.m_Resource != Resource.Money || upkeep.m_Upkeep.m_Amount <= 0) continue;
                float value = upkeep.m_Upkeep.m_Amount * EconomyUtils.GetMarketPrice(upkeep.m_Upkeep.m_Resource, resourcePrefabs, ref resourceDatas);
                CityUtils.ApplyModifier(ref value, cityModifiers, CityModifierType.CityServiceBuildingBaseUpkeepCost);
                value += wages;
                if (inactive) value *= 0.1f;
                total += UnityEngine.Mathf.RoundToInt(value);
            }
            return total;
        }

        /// <summary>The district of a building or of its owner (an upgrade, a sub-building), up the owner chain; Entity.Null for none.</summary>
        public static Entity DistrictOf(Entity entity, ref ComponentLookup<CurrentDistrict> currentDistricts, ref ComponentLookup<Owner> owners)
        {
            for (int depth = 0; depth < 8 && entity != Entity.Null; depth++)
            {
                if (currentDistricts.TryGetComponent(entity, out CurrentDistrict current)) return current.m_District;
                if (!owners.TryGetComponent(entity, out Owner owner)) break;
                entity = owner.m_Owner;
            }
            return Entity.Null;
        }

        /// <summary>The city slider's % for a service, as vanilla reads it (100 when the service has no entry).</summary>
        public static int CityBudget(DynamicBuffer<ServiceBudgetData> budgets, Entity service)
        {
            for (int i = 0; i < budgets.Length; i++)
                if (budgets[i].m_Service == service) return budgets[i].m_Budget;
            return 100;
        }

        /// <summary>The % a building of <paramref name="service"/> in <paramref name="district"/> runs at: its override, else the city's.</summary>
        public static int EffectiveBudget(NativeHashMap<ServiceDistrictKey, int> overrides, bool useOverrides, Entity service, Entity district, int cityBudget)
        {
            if (useOverrides && overrides.TryGetValue(new ServiceDistrictKey(service, district), out int budget)) return budget;
            return cityBudget;
        }
    }
}
