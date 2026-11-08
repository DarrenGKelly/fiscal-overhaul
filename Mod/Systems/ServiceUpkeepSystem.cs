using System.Collections.Generic;
using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Game.City;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Records the city's Service Upkeep split per service, once per vanilla statistics sample (1/32 month), and keeps
    /// it in the save (<see cref="IDefaultSerializable"/>, like <see cref="DistrictBudgetSystem"/>): vanilla records the
    /// upkeep as one ExpenseSource and keeps no per-service series. A sample is each service's weight: vanilla's own
    /// per-service estimate (<see cref="CityServiceBudgetSystem.GetEstimatedServiceBudget"/>, whose sum is the
    /// ServiceUpkeep expense), tagged with <see cref="CityStatisticsSystem.sampleCount"/> so it lines up with the
    /// statistic sample it describes. <see cref="BudgetHistoryUISystem"/> splits the recorded upkeep by these weights
    /// (<see cref="ServiceUpkeepSplit.SplitSeries"/>), so the parts always sum to the row. Records only while the
    /// budget chart is on (<see cref="FeatureGates.RecordsServiceUpkeep"/>), panel open or not.
    /// </summary>
    public partial class ServiceUpkeepSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        /// <summary>CityStatisticsSystem's interval (32 samples a month); halfway between its samples.</summary>
        public const int kUpdateInterval = 8192;
        public const int kUpdateOffset = 4096;

        private Setting m_Setting;
        private CityServiceBudgetSystem m_BudgetSystem;
        private CityStatisticsSystem m_StatisticsSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ServiceQuery;

        private readonly ServiceColumns m_Columns = new ServiceColumns();
        private ServiceWeightHistory m_History = new ServiceWeightHistory();

        public void SetSettings(Setting setting) => m_Setting = setting;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kUpdateInterval;

        public override int GetUpdateOffset(SystemUpdatePhase phase) => kUpdateOffset;

        /// <summary>The service names, by column.</summary>
        public ServiceColumns Columns => m_Columns;

        public ServiceWeightHistory History => m_History;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_StatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // The city service prefabs, as CityServiceBudgetSystem's service query (the services with a budget).
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<CollectedCityServiceBudgetData>());
        }

        /// <summary>
        /// The service prefabs with their names, in the order of vanilla's Services tab (UIObjectData priority, then
        /// name), each name's column added to <paramref name="columns"/>.
        /// </summary>
        internal static List<KeyValuePair<Entity, int>> ServiceColumnsOf(EntityManager entityManager, PrefabSystem prefabSystem, EntityQuery query, ServiceColumns columns)
        {
            var services = new List<(Entity entity, string name, int priority)>();
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in entities)
                {
                    int priority = entityManager.HasComponent<UIObjectData>(e) ? entityManager.GetComponentData<UIObjectData>(e).m_Priority : int.MaxValue;
                    services.Add((e, prefabSystem.GetPrefabName(e), priority));
                }
            }
            services.Sort((a, b) => a.priority != b.priority ? a.priority.CompareTo(b.priority) : string.CompareOrdinal(a.name, b.name));
            var result = new List<KeyValuePair<Entity, int>>(services.Count);
            foreach (var s in services) result.Add(new KeyValuePair<Entity, int>(s.entity, columns.GetOrAdd(s.name)));
            return result;
        }

        /// <summary>Every service's vanilla upkeep estimate now, by column (the live weights).</summary>
        public float[] LiveWeights()
        {
            List<KeyValuePair<Entity, int>> services = ServiceColumnsOf(EntityManager, m_PrefabSystem, m_ServiceQuery, m_Columns);
            var weights = new float[m_Columns.Count];
            foreach (KeyValuePair<Entity, int> service in services)
            {
                m_BudgetSystem.GetEstimatedServiceBudget(service.Key, out int upkeep);
                weights[service.Value] = System.Math.Max(0, upkeep);
            }
            return weights;
        }

        /// <summary>Services in the order the rows show them: the tab order of the current services, then any others.</summary>
        public List<int> ColumnOrder()
        {
            var order = new List<int>();
            foreach (KeyValuePair<Entity, int> service in ServiceColumnsOf(EntityManager, m_PrefabSystem, m_ServiceQuery, m_Columns))
                if (!order.Contains(service.Value)) order.Add(service.Value);
            for (int c = 0; c < m_Columns.Count; c++) if (!order.Contains(c)) order.Add(c);
            return order;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Setting == null || !FeatureGates.RecordsServiceUpkeep(m_Setting.EnableBudgetChart)) return;
            if (m_ServiceQuery.IsEmptyIgnoreFilter) return;
            m_History.Add(m_StatisticsSystem.sampleCount, LiveWeights());
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            ServiceUpkeepSave.WriteCity(new SaveWriter<TWriter>(writer), m_Columns, m_History);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            ServiceUpkeepSave.ReadCity(new SaveReader<TReader>(reader), m_Columns, out m_History);
        }

        public void SetDefaults(Context context)
        {
            m_Columns.Clear();
            m_History = new ServiceWeightHistory();
        }
    }

    /// <summary>The game's IReader as an <see cref="ISaveReader"/>. A class, so a struct reader advances in one place.</summary>
    internal sealed class SaveReader<TReader> : ISaveReader where TReader : IReader
    {
        public TReader Reader;

        public SaveReader(TReader reader) => Reader = reader;

        public int ReadInt()
        {
            Reader.Read(out int value);
            return value;
        }

        public float ReadFloat()
        {
            Reader.Read(out float value);
            return value;
        }

        public string ReadString()
        {
            Reader.Read(out string value);
            return value;
        }

        public Entity ReadEntity()
        {
            Reader.Read(out Entity value);
            return value;
        }
    }

    /// <summary>The game's IWriter as an <see cref="ISaveWriter"/>.</summary>
    internal sealed class SaveWriter<TWriter> : ISaveWriter where TWriter : IWriter
    {
        public TWriter Writer;

        public SaveWriter(TWriter writer) => Writer = writer;

        public void Write(int value) => Writer.Write(value);

        public void Write(float value) => Writer.Write(value);

        public void Write(string value) => Writer.Write(value);

        public void WriteEntity(Entity value) => Writer.Write(value);
    }
}
