using FiscalOverhaul.Logic;
using Game.Prefabs;
using Game.Zones;
using Unity.Entities;

namespace FiscalOverhaul
{
    /// <summary>
    /// Works out which zone group and density a property (building) counts as for density taxes.
    /// Burst-safe: plain lookups, no managed calls. Mirrors PropertyUtils.GetZoneDensity, which isn't
    /// usable from Burst because of its managed assert message.
    /// </summary>
    public static class DensityClassifier
    {
        /// <summary>
        /// Classifies <paramref name="property"/>. <paramref name="isHousehold"/> picks the tax group:
        /// households pay residential tax, so only residential-zoned buildings count for them; companies
        /// count only in commercial/industrial/office-zoned buildings. Returns false for anything else
        /// (unzoned/signature buildings, commuters, companies in mixed-use residential zones).
        /// </summary>
        public static bool TryClassify(
            Entity property,
            bool isHousehold,
            ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<SpawnableBuildingData> spawnables,
            ref ComponentLookup<ZoneData> zones,
            ref ComponentLookup<ZonePropertiesData> zoneProperties,
            out int cellIndex)
        {
            cellIndex = -1;
            if (property == Entity.Null || !prefabRefs.TryGetComponent(property, out PrefabRef prefabRef)) return false;
            if (!spawnables.TryGetComponent(prefabRef.m_Prefab, out SpawnableBuildingData spawnable)) return false;
            Entity zonePrefab = spawnable.m_ZonePrefab;
            if (!zones.TryGetComponent(zonePrefab, out ZoneData zone)) return false;
            if (!zoneProperties.TryGetComponent(zonePrefab, out ZonePropertiesData props)) return false;

            TaxZone taxZone;
            Density density;
            switch (zone.m_AreaType)
            {
                case AreaType.Residential:
                    if (!isHousehold) return false;
                    taxZone = TaxZone.Residential;
                    density = !props.m_ScaleResidentials
                        ? Density.Low
                        : props.m_ResidentialProperties < props.m_SpaceMultiplier ? Density.Medium : Density.High;
                    break;
                case AreaType.Commercial:
                    if (isHousehold) return false;
                    taxZone = TaxZone.Commercial;
                    density = props.m_SpaceMultiplier > 1f ? Density.High : Density.Low;
                    break;
                case AreaType.Industrial:
                    if (isHousehold) return false;
                    if (zone.IsOffice())
                    {
                        taxZone = TaxZone.Office;
                        density = props.m_SpaceMultiplier < 10f ? Density.Low : Density.High;
                    }
                    else
                    {
                        taxZone = TaxZone.Industrial;
                        density = Density.Low;
                    }
                    break;
                default:
                    return false;
            }

            cellIndex = DensityTaxTable.Index(taxZone, density);
            return true;
        }
    }
}
