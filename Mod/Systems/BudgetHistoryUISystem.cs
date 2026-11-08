using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.City;
using Game.Tools;
using Unity.Entities;
using Game.Simulation;
using Game.UI;
using Game.UI.InGame;
using Unity.Collections;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Feeds the Budget tab's line chart. The game already records every income and expense source 32 times a
    /// month (CityStatisticsSystem, StatisticType.Income/Expense with the source as parameter), and saves it, so
    /// the chart has the city's whole history from the first frame. This system turns those samples into
    /// "per month" series for the time span the UI asks for, downsampled to at most <see cref="kMaxPoints"/>.
    ///
    /// Bindings (group "fiscalOverhaul"):
    ///   budgetChartEnabled  bool  <see cref="Setting.EnableBudgetChart"/>; false = the UI shows the vanilla Budget tab
    ///   history             raw   { span, samples, monthsAgo: number[], income: number[14][], expense: number[15][] }
    ///   setSpan             trigger(int months)  months &lt;= 0 = all history
    ///   districtBudgetEnabled bool  district recording on (<see cref="FeatureGates.RecordsDistrictBudget"/>): the UI shows
    ///                       the district selector, even for a city without districts
    ///   districts           raw   [{ key, name }] the city's districts by name (empty while district budgets are off)
    ///   setDistrict         trigger(string key)  "" = Entire City, "outside", or a key from districts; with a district the
    ///                       history also has { district, nowIncome: number[14], nowExpense: number[15] }
    ///   history.services    [{ name, start, values: number[] }] the Service Upkeep row split per service (prefab name,
    ///                       Services.NAME[name] in vanilla's locale) at the same points, summing to the row from start on
    ///                       (ServiceUpkeepSystem for Entire City, DistrictBudgetSystem for a district); empty without data
    /// Expense values are positive amounts (the UI decides the sign). With the chart disabled no history is built.
    /// </summary>
    public partial class BudgetHistoryUISystem : UISystemBase
    {
        public const string kGroup = "fiscalOverhaul";
        private const int kMaxPoints = 240;
        private const int kIncomeSources = (int)IncomeSource.Count;
        private const int kExpenseSources = (int)ExpenseSource.Count;

        private CityStatisticsSystem m_StatisticsSystem;
        private CityServiceBudgetSystem m_BudgetSystem;
        private GamePanelUISystem m_PanelSystem;
        private RawValueBinding m_HistoryBinding;
        private ValueBinding<bool> m_EnabledBinding;
        private ValueBinding<bool> m_DistrictsEnabledBinding;
        private DistrictBudgetSystem m_DistrictBudgetSystem;
        private ServiceUpkeepSystem m_ServiceUpkeepSystem;
        private NameSystem m_NameSystem;
        private EntityQuery m_DistrictQuery;
        private RawValueBinding m_DistrictsBinding;
        private Setting m_Setting;
        private string m_DistrictKey = DistrictKey.City;
        private string m_LastDistrictKey;
        private int m_LastDistrictSamples = -1;
        private int m_LastDistrictCount = -1;

        private int m_SpanMonths = BudgetHistoryMath.DefaultSpanMonths;
        private int m_LastSampleCount = -1;
        private int m_LastSpan = int.MinValue;
        private int m_FramesSinceRebuild;

        public void SetSettings(Setting setting) => m_Setting = setting;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_StatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_DistrictBudgetSystem = World.GetOrCreateSystemManaged<DistrictBudgetSystem>();
            m_ServiceUpkeepSystem = World.GetOrCreateSystemManaged<ServiceUpkeepSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_DistrictQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Areas.District>(), ComponentType.Exclude<Temp>());
            AddBinding(m_DistrictsEnabledBinding = new ValueBinding<bool>(kGroup, "districtBudgetEnabled", false));
            AddBinding(m_DistrictsBinding = new RawValueBinding(kGroup, "districts", WriteDistricts));
            AddBinding(new TriggerBinding<string>(kGroup, "setDistrict", key => m_DistrictKey = key ?? DistrictKey.City));
            AddBinding(m_EnabledBinding = new ValueBinding<bool>(kGroup, "budgetChartEnabled", true));
            AddBinding(m_HistoryBinding = new RawValueBinding(kGroup, "history", WriteHistory));
            AddBinding(new TriggerBinding<int>(kGroup, "setSpan", months => m_SpanMonths = months));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool enabled = m_Setting == null || m_Setting.EnableBudgetChart;
            m_EnabledBinding.Update(enabled);
            m_DistrictsEnabledBinding.Update(DistrictsEnabled());

            // Only rebuild while the chart is enabled, the Economy panel is open and something changed: a new sample
            // or a new span.
            if (!FeatureGates.BuildsPanelData(enabled, m_PanelSystem.activePanel is EconomyPanel))
            {
                m_LastSampleCount = -1; // rebuild as soon as the chart shows again
                m_LastDistrictCount = -1;
                return;
            }
            int samples = m_StatisticsSystem.sampleCount;
            // The live "Now" point changes between samples too (a new building, a budget slider, the game paused),
            // so rebuild at least every 60 frames while the panel is open.
            int districtSamples = ResolveDistrict(out _) ? DistrictSampleCount() : -1;
            int districtCount = DistrictsEnabled() ? m_DistrictQuery.CalculateEntityCount() : 0;
            if (samples == m_LastSampleCount && m_SpanMonths == m_LastSpan && m_DistrictKey == m_LastDistrictKey
                && districtSamples == m_LastDistrictSamples && districtCount == m_LastDistrictCount
                && ++m_FramesSinceRebuild < 60) return;
            m_FramesSinceRebuild = 0;
            m_LastSampleCount = samples;
            m_LastSpan = m_SpanMonths;
            m_LastDistrictKey = m_DistrictKey;
            m_LastDistrictSamples = districtSamples;
            m_LastDistrictCount = districtCount;
            m_HistoryBinding.Update();
            m_DistrictsBinding.Update();
        }

        private void WriteHistory(IJsonWriter writer)
        {
            // The statistics buffers are written by CityStatisticsSystem's jobs; finish them before reading here.
            m_StatisticsSystem.CompleteWriters();

            // Every series is aligned to the newest sample; series with less history are left-padded with 0. A new
            // city has no complete sample yet, so every series is just its live value (one point).
            var income = new double[kIncomeSources][];
            var expense = new double[kExpenseSources][];
            double[] nowIncome = null;
            double[] nowExpense = null;
            int length = 1;
            Entity district = Entity.Null;
            if (ResolveDistrict(out district))
            {
                BuildDistrictSeries(district, income, expense, out nowIncome, out nowExpense);
                for (int i = 0; i < kIncomeSources; i++) if (income[i].Length > length) length = income[i].Length;
                for (int i = 0; i < kExpenseSources; i++) if (expense[i].Length > length) length = expense[i].Length;
            }
            else
            {
                for (int i = 0; i < kIncomeSources; i++)
                {
                    income[i] = BudgetHistoryMath.WithLiveNow(ReadMonthly(StatisticType.Income, i), m_BudgetSystem.GetIncome((IncomeSource)i));
                    if (income[i].Length > length) length = income[i].Length;
                }
                for (int i = 0; i < kExpenseSources; i++)
                {
                    // Vanilla shows -GetExpense; the statistics store the cost as a positive amount, as the chart does.
                    expense[i] = BudgetHistoryMath.WithLiveNow(ReadMonthly(StatisticType.Expense, i), System.Math.Abs(m_BudgetSystem.GetExpense((ExpenseSource)i)));
                    if (expense[i].Length > length) length = expense[i].Length;
                }
            }

            int span = BudgetHistoryMath.SamplesForSpan(m_SpanMonths, length);
            int[] picked = BudgetHistoryMath.PickIndices(length, span, kMaxPoints);

            // Every view writes the same properties (BudgetHistoryPayload): a type written with a different property
            // set than its first write crashes Gameface natively in TypeEnd. Entire City (also the fallback for an
            // unknown or deleted district) writes null live sums.
            bool districtView = nowIncome != null;
            List<ServiceRow> services = BuildServiceRows(expense[ServiceUpkeepSplit.ServiceUpkeepSource], length, picked, districtView, district);
            writer.TypeBegin(BudgetHistoryPayload.TypeName);
            foreach (string property in BudgetHistoryPayload.Properties(districtView))
            {
                writer.PropertyName(property);
                if (BudgetHistoryPayload.IsNull(property, districtView))
                {
                    writer.WriteNull();
                    continue;
                }
                switch (property)
                {
                    case "span": writer.Write(m_SpanMonths); break;
                    case "samples": writer.Write(length); break;
                    case "monthsAgo":
                        writer.ArrayBegin(picked.Length);
                        foreach (int index in picked) writer.Write((float)BudgetHistoryMath.MonthsAgo(index, length));
                        writer.ArrayEnd();
                        break;
                    case "income": WriteSeriesSet(writer, income, length, picked); break;
                    case "expense": WriteSeriesSet(writer, expense, length, picked); break;
                    case "district": writer.Write(districtView ? m_DistrictKey ?? DistrictKey.City : DistrictKey.City); break;
                    case "nowIncome": WriteRounded(writer, nowIncome); break;
                    case "nowExpense": WriteRounded(writer, nowExpense); break;
                    case "services": WriteServices(writer, services); break;
                    default: writer.WriteNull(); break;
                }
            }
            writer.TypeEnd();
        }

        /// <summary>
        /// The Service Upkeep row split per service at the picked points: the row as written (WriteSeriesSet) split by
        /// each point's recorded weights. Entire City: the weights tagged with the statistic sample of the point
        /// (ServiceUpkeepSystem), the live weights for Now. A district: its weight history by position, carried forward
        /// to Now.
        /// </summary>
        private List<ServiceRow> BuildServiceRows(double[] upkeep, int length, int[] picked, bool districtView, Entity district)
        {
            int pad = length - upkeep.Length;
            var parent = new long[picked.Length];
            var weights = new float[picked.Length][];
            ServiceColumns columns;
            if (districtView)
            {
                columns = m_DistrictBudgetSystem.ServiceColumns;
                int samples = upkeep.Length - 1; // the history, then the live value
                m_DistrictBudgetSystem.TryGetServiceWeights(district, out ServiceWeightHistory history);
                int weightCount = history?.Count ?? 0;
                for (int i = 0; i < picked.Length; i++)
                {
                    int local = picked[i] - pad;
                    if (local < 0) continue;
                    parent[i] = (long)System.Math.Round(upkeep[local]);
                    int index = ServiceUpkeepSplit.DistrictWeightIndex(local, samples, weightCount);
                    if (index >= 0) weights[i] = history.At(index);
                }
            }
            else
            {
                columns = m_ServiceUpkeepSystem.Columns;
                ServiceWeightHistory history = m_ServiceUpkeepSystem.History;
                int sampleCount = m_StatisticsSystem.sampleCount;
                for (int i = 0; i < picked.Length; i++)
                {
                    int local = picked[i] - pad;
                    if (local < 0) continue;
                    parent[i] = (long)System.Math.Round(upkeep[local]);
                    bool live = local == upkeep.Length - 1;
                    if (live && (m_Setting == null || FeatureGates.RecordsServiceUpkeep(m_Setting.EnableBudgetChart)))
                        weights[i] = m_ServiceUpkeepSystem.LiveWeights();
                    else
                        weights[i] = history.ForTag(ServiceUpkeepSplit.CityTag(sampleCount, upkeep.Length, local));
                }
            }

            // Rows in vanilla's Services tab order (the city's current services), then any service no longer there.
            var order = new List<int>();
            ServiceColumns cityColumns = m_ServiceUpkeepSystem.Columns;
            foreach (int cityColumn in m_ServiceUpkeepSystem.ColumnOrder())
            {
                int column = columns.IndexOf(cityColumns[cityColumn]);
                if (column >= 0 && !order.Contains(column)) order.Add(column);
            }
            for (int c = 0; c < columns.Count; c++) if (!order.Contains(c)) order.Add(c);
            return ServiceRows.Build(parent, weights, columns.Names, order);
        }

        private static void WriteServices(IJsonWriter writer, List<ServiceRow> services)
        {
            writer.ArrayBegin(services.Count);
            foreach (ServiceRow row in services)
            {
                writer.TypeBegin(BudgetHistoryPayload.ServiceTypeName);
                foreach (string property in BudgetHistoryPayload.ServiceProperties)
                {
                    writer.PropertyName(property);
                    switch (property)
                    {
                        case "name": writer.Write(row.Name); break;
                        case "start": writer.Write(row.Start); break;
                        case "values":
                            writer.ArrayBegin(row.Values.Length);
                            foreach (long v in row.Values) writer.Write((int)v);
                            writer.ArrayEnd();
                            break;
                        default: writer.WriteNull(); break;
                    }
                }
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static void WriteRounded(IJsonWriter writer, double[] values)
        {
            writer.ArrayBegin(values.Length);
            foreach (double v in values) writer.Write((int)System.Math.Round(v));
            writer.ArrayEnd();
        }

        private bool DistrictsEnabled() => m_Setting != null && FeatureGates.RecordsDistrictBudget(m_Setting.EnableBudgetChart, m_Setting.EnableDistrictBudget);

        /// <summary>
        /// True when the selected key names a district view that exists, with its entity (Entity.Null for "outside").
        /// False for Entire City, the feature off, or an unknown / deleted key.
        /// </summary>
        private bool ResolveDistrict(out Entity entity)
        {
            entity = Entity.Null;
            if (!DistrictsEnabled() || m_DistrictKey == DistrictKey.City) return false;
            if (m_DistrictKey == DistrictKey.Outside) return true;
            if (!DistrictKey.TryParse(m_DistrictKey, out int index, out int version)) return false;
            var candidate = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(candidate) || !EntityManager.HasComponent<Game.Areas.District>(candidate) || EntityManager.HasComponent<Temp>(candidate)) return false;
            entity = candidate;
            return true;
        }

        private int DistrictSampleCount()
        {
            ResolveDistrict(out Entity entity);
            return m_DistrictBudgetSystem.TryGetHistory(entity, out DistrictHistory history) ? history.Count : 0;
        }

        private void BuildDistrictSeries(Entity district, double[][] income, double[][] expense, out double[] nowIncome, out double[] nowExpense)
        {
            var slotSeries = new double[DistrictBudgetMath.SourceCount][];
            bool has = m_DistrictBudgetSystem.TryGetHistory(district, out DistrictHistory history);
            for (int slot = 0; slot < slotSeries.Length; slot++)
                slotSeries[slot] = has ? history.Series(slot) : Array.Empty<double>();

            // The live value; an area the latest sample didn't have uses its last history sample, else 0.
            float[] liveFloats = m_DistrictBudgetSystem.LiveValues(district);
            var live = new double[DistrictBudgetMath.SourceCount];
            for (int slot = 0; slot < live.Length; slot++)
            {
                double[] series = slotSeries[slot];
                live[slot] = liveFloats != null ? liveFloats[slot] : (series.Length > 0 ? series[series.Length - 1] : 0);
            }

            double[][] incomeSlots = DistrictBudgetMath.Spread(slotSeries, true, kIncomeSources, Array.Empty<double>());
            double[][] expenseSlots = DistrictBudgetMath.Spread(slotSeries, false, kExpenseSources, Array.Empty<double>());
            nowIncome = DistrictBudgetMath.Spread(live, true, kIncomeSources, 0.0);
            nowExpense = DistrictBudgetMath.Spread(live, false, kExpenseSources, 0.0);
            for (int i = 0; i < kIncomeSources; i++) income[i] = BudgetHistoryMath.WithLiveNow(incomeSlots[i], nowIncome[i]);
            for (int i = 0; i < kExpenseSources; i++) expense[i] = BudgetHistoryMath.WithLiveNow(expenseSlots[i], nowExpense[i]);
        }

        private void WriteDistricts(IJsonWriter writer)
        {
            var choices = new List<KeyValuePair<string, string>>();
            if (DistrictsEnabled())
            {
                using (NativeArray<Entity> entities = m_DistrictQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity e in entities)
                        choices.Add(new KeyValuePair<string, string>(DistrictKey.Format(e.Index, e.Version), m_NameSystem.GetRenderedLabelName(e) ?? string.Empty));
                }
                choices.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
            }
            writer.ArrayBegin(choices.Count);
            foreach (var choice in choices)
            {
                writer.TypeBegin("FiscalOverhaul.DistrictChoice");
                writer.PropertyName("key");
                writer.Write(choice.Key);
                writer.PropertyName("name");
                writer.Write(choice.Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static void WriteSeriesSet(IJsonWriter writer, double[][] set, int length, int[] picked)
        {
            writer.ArrayBegin(set.Length);
            foreach (double[] series in set)
            {
                int pad = length - series.Length;
                writer.ArrayBegin(picked.Length);
                foreach (int index in picked)
                {
                    int local = index - pad;
                    writer.Write(local >= 0 ? (int)System.Math.Round(series[local]) : 0);
                }
                writer.ArrayEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>Per-month rate series for one statistic (empty if the game doesn't track that source).</summary>
        private double[] ReadMonthly(StatisticType type, int parameter)
        {
            NativeArray<CityStatistic> samples = m_StatisticsSystem.GetStatisticArray(type, parameter);
            try
            {
                // The newest sample is still filling up; leaving it in makes every line dip at "now".
                int count = BudgetHistoryMath.CompleteSamples(samples.Length);
                var deltas = new double[count];
                for (int i = 0; i < count; i++) deltas[i] = samples[i].m_Value;
                return BudgetHistoryMath.MonthlyRates(deltas);
            }
            finally
            {
                if (samples.IsCreated) samples.Dispose();
            }
        }
    }
}
