using System.Collections.Generic;
using Colossal.Entities;
using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.City;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.UI;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// The Services tab table's data beyond vanilla's serviceBudget bindings (group "fiscalOverhaul"), built only while
    /// the Economy panel is open (<see cref="FeatureGates.BuildsServiceTable"/>; the table is always the Services tab),
    /// the district fee shares only while the district view is offered (<see cref="FeatureGates.BuildsDistrictServiceFees"/>):
    ///   serviceFeeDefaults   raw  [{ resource, fee }]: vanilla's default fee per PlayerResource
    ///                             (ServiceFeeParameterData m_Default, what resetService sets): a fee slider's reset target
    ///   districtServiceFees  raw  [{ service, district, resource, income }] (<see cref="DistrictServiceFees"/>): each
    ///                             service's fee income (vanilla serviceDetails' incomeInternal, the same estimate)
    ///                             split over the areas by usage, with the Budget chart's district weights
    ///                             (<see cref="DistrictBudgetSystem.LiveValues"/>); [] while district budgets aren't recorded
    /// </summary>
    public partial class ServiceTableUISystem : UISystemBase
    {
        private const int kRefreshFrames = 30;

        private Setting m_Setting;
        private GamePanelUISystem m_PanelSystem;
        private ServiceFeeSystem m_ServiceFeeSystem;
        private CitySystem m_CitySystem;
        private DistrictBudgetSystem m_DistrictBudgetSystem;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_FeeParameterQuery;
        private RawValueBinding m_DefaultsBinding;
        private RawValueBinding m_FeesBinding;
        private bool m_LastShows;
        private int m_Frames = kRefreshFrames;

        public void SetSettings(Setting setting) => m_Setting = setting;

        private bool Shows => m_Setting != null && FeatureGates.ShowsDistrictServiceBudgets(m_Setting.EnableSimulationChanges, m_Setting.EnableDistrictServiceBudget);
        private bool m_LastOpen;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            m_ServiceFeeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_DistrictBudgetSystem = World.GetOrCreateSystemManaged<DistrictBudgetSystem>();
            // As vanilla ServiceBudgetUISystem's service query.
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<UIObjectData>(), ComponentType.ReadOnly<ServiceData>());
            m_FeeParameterQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceFeeParameterData>());
            AddBinding(m_DefaultsBinding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "serviceFeeDefaults", WriteDefaults));
            AddBinding(m_FeesBinding = new RawValueBinding(BudgetHistoryUISystem.kGroup, "districtServiceFees", WriteFees));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool open = m_PanelSystem.activePanel is EconomyPanel;
            bool shows = Shows;
            // Switched off: send the empty fee list once (the table is city-only now).
            if (m_LastShows && !shows) m_FeesBinding.Update();
            m_LastShows = shows;
            if (!FeatureGates.BuildsServiceTable(open))
            {
                m_LastOpen = false;
                m_Frames = kRefreshFrames; // at once when the panel opens again
                return;
            }
            if (!m_LastOpen) m_DefaultsBinding.Update();
            m_LastOpen = true;
            if (++m_Frames < kRefreshFrames) return;
            m_Frames = 0;
            m_DefaultsBinding.Update();
            if (FeatureGates.BuildsDistrictServiceFees(m_Setting != null && m_Setting.EnableSimulationChanges, m_Setting != null && m_Setting.EnableDistrictServiceBudget, open))
                m_FeesBinding.Update();
        }

        private void WriteDefaults(IJsonWriter writer)
        {
            var defaults = new List<KeyValuePair<int, float>>();
            if (!m_FeeParameterQuery.IsEmptyIgnoreFilter)
            {
                ServiceFeeParameterData parameters = m_FeeParameterQuery.GetSingleton<ServiceFeeParameterData>();
                for (int r = 0; r < (int)PlayerResource.Count; r++)
                {
                    FeeParameters fee = parameters.GetFeeParameters((PlayerResource)r);
                    if (fee.m_Max > 0) defaults.Add(new KeyValuePair<int, float>(r, fee.m_Default));
                }
            }
            writer.ArrayBegin(defaults.Count);
            foreach (KeyValuePair<int, float> pair in defaults)
            {
                writer.TypeBegin("FiscalOverhaul.ServiceFeeDefault");
                writer.PropertyName("resource");
                writer.Write(pair.Key);
                writer.PropertyName("fee");
                writer.Write(pair.Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private void WriteFees(IJsonWriter writer)
        {
            List<DistrictServiceFees.Row> rows = Shows ? BuildRows() : new List<DistrictServiceFees.Row>();
            writer.ArrayBegin(rows.Count);
            foreach (DistrictServiceFees.Row row in rows)
            {
                // Every property every time: Gameface fixes a type's properties at its first write.
                writer.TypeBegin("FiscalOverhaul.DistrictServiceFee");
                writer.PropertyName("service");
                writer.Write(row.Service);
                writer.PropertyName("district");
                writer.Write(row.District);
                writer.PropertyName("resource");
                writer.Write(row.Resource);
                writer.PropertyName("income");
                writer.Write(row.Income);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private List<DistrictServiceFees.Row> BuildRows()
        {
            var areas = new List<KeyValuePair<string, float[]>>();
            foreach (Entity area in m_DistrictBudgetSystem.Areas)
            {
                float[] live = m_DistrictBudgetSystem.LiveValues(area);
                if (live == null) continue;
                string key = area == Entity.Null ? DistrictKey.Outside : DistrictKey.Format(area.Index, area.Version);
                areas.Add(new KeyValuePair<string, float[]>(key, live));
            }
            var fees = new List<DistrictServiceFees.Fee>();
            if (areas.Count > 0 && m_CitySystem.City != Entity.Null && EntityManager.HasBuffer<ServiceFee>(m_CitySystem.City))
            {
                DynamicBuffer<ServiceFee> cityFees = EntityManager.GetBuffer<ServiceFee>(m_CitySystem.City, true);
                NativeArray<Entity> services = m_ServiceQuery.ToEntityArray(Allocator.Temp);
                foreach (Entity service in services)
                {
                    if (!EntityManager.TryGetBuffer(service, true, out DynamicBuffer<CollectedCityServiceFeeData> collected)) continue;
                    string serviceKey = DistrictKey.Format(service.Index, service.Version);
                    foreach (CollectedCityServiceFeeData item in collected)
                    {
                        var resource = (PlayerResource)item.m_PlayerResource;
                        // Vanilla's incomeInternal (ServiceBudgetUISystem.WriteServiceFees).
                        int income = ServiceFeeSystem.TryGetFee(resource, cityFees, out float fee)
                            ? m_ServiceFeeSystem.GetServiceFeeIncomeEstimate(resource, fee)
                            : m_ServiceFeeSystem.GetServiceFees(resource).x;
                        fees.Add(new DistrictServiceFees.Fee
                        {
                            Service = serviceKey,
                            Resource = (int)resource,
                            IncomeSource = (int)EconomyUtils.GetIncomeSource(resource),
                            Income = income,
                        });
                    }
                }
                services.Dispose();
            }
            return DistrictServiceFees.Build(fees, areas);
        }
    }
}
