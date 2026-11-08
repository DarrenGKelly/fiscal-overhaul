using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace FiscalOverhaul
{
    /// <summary>
    /// Finds the keys a tax payer's adjustments are stored under (<see cref="Logic.TaxRateTable"/>): the density cell of
    /// its building (<see cref="DensityClassifier"/>) and the district the building stands in (CurrentDistrict, up the
    /// owner chain as DistrictBudgetSystem does). Burst-safe: lookups only. The tax engine's job holds one.
    /// </summary>
    public struct TaxKeyResolver
    {
        [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefs;
        [ReadOnly] public ComponentLookup<SpawnableBuildingData> m_Spawnables;
        [ReadOnly] public ComponentLookup<ZoneData> m_Zones;
        [ReadOnly] public ComponentLookup<ZonePropertiesData> m_ZoneProperties;
        [ReadOnly] public ComponentLookup<CurrentDistrict> m_CurrentDistricts;
        [ReadOnly] public ComponentLookup<Owner> m_Owners;

        /// <summary>Read-only lookups from <paramref name="system"/>, fresh for this update.</summary>
        public static TaxKeyResolver Create(SystemBase system)
        {
            return new TaxKeyResolver
            {
                m_PrefabRefs = system.GetComponentLookup<PrefabRef>(true),
                m_Spawnables = system.GetComponentLookup<SpawnableBuildingData>(true),
                m_Zones = system.GetComponentLookup<ZoneData>(true),
                m_ZoneProperties = system.GetComponentLookup<ZonePropertiesData>(true),
                m_CurrentDistricts = system.GetComponentLookup<CurrentDistrict>(true),
                m_Owners = system.GetComponentLookup<Owner>(true),
            };
        }

        /// <summary>The density cell (<see cref="Logic.DensityTaxTable.Index"/>) of <paramref name="property"/>, or false.</summary>
        public bool TryDensityCell(Entity property, bool isHousehold, out int cell)
        {
            return DensityClassifier.TryClassify(property, isHousehold, ref m_PrefabRefs, ref m_Spawnables, ref m_Zones, ref m_ZoneProperties, out cell);
        }

        /// <summary>The district entity <paramref name="property"/> (or its owner, up the chain) stands in; Entity.Null for none.</summary>
        public Entity District(Entity property)
        {
            Entity entity = property;
            for (int depth = 0; depth < 8 && entity != Entity.Null; depth++)
            {
                if (m_CurrentDistricts.TryGetComponent(entity, out CurrentDistrict current)) return current.m_District;
                if (!m_Owners.TryGetComponent(entity, out Owner owner)) break;
                entity = owner.m_Owner;
            }
            return Entity.Null;
        }
    }
}
