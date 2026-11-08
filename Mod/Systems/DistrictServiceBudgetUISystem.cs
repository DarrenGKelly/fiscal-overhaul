using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// The per-district service budget trigger (group "fiscalOverhaul"); the Services tab UI comes in a later slice.
    ///   setDistrictServiceBudget  trigger(string serviceKey, string districtKey, int budget)
    ///     serviceKey:  the service prefab's name, or its entity as "index:version"
    ///     districtKey: "outside" or "index:version" of a district (<see cref="DistrictKey"/>); "" (Entire City) is the
    ///                  vanilla slider's job and is ignored
    ///     budget:      50-150 (clamped); 0 clears the override, so the district follows the city slider again
    /// Stored whether or not the feature is on; it only applies while it is (<see cref="FeatureGates.AppliesDistrictServiceBudgets"/>).
    /// </summary>
    public partial class DistrictServiceBudgetUISystem : UISystemBase
    {
        private DistrictServiceBudgetSystem m_Store;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Store = World.GetOrCreateSystemManaged<DistrictServiceBudgetSystem>();
            AddBinding(new TriggerBinding<string, string, int>(BudgetHistoryUISystem.kGroup, "setDistrictServiceBudget", SetBudget));
        }

        private void SetBudget(string serviceKey, string districtKey, int budget)
        {
            string service = ServiceName(serviceKey);
            if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(districtKey)) return;
            if (districtKey == DistrictKey.Outside)
            {
                m_Store.Set(service, true, Entity.Null, budget);
                return;
            }
            if (!DistrictKey.TryParse(districtKey, out int index, out int version)) return;
            m_Store.Set(service, false, new Entity { Index = index, Version = version }, budget);
        }

        /// <summary>A service key as the store keeps it: a prefab name; an "index:version" entity key is turned into its prefab's name.</summary>
        private string ServiceName(string serviceKey)
        {
            if (string.IsNullOrEmpty(serviceKey)) return null;
            if (!DistrictKey.TryParse(serviceKey, out int index, out int version)) return serviceKey;
            var entity = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Game.Simulation.CollectedCityServiceBudgetData>(entity)) return null;
            return m_Store.ServiceKey(entity);
        }
    }
}
