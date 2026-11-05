using Colossal.UI.Binding;
using FiscalOverhaul.Logic;
using Game.City;
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
        private Setting m_Setting;

        private int m_SpanMonths = 12;
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
            AddBinding(m_EnabledBinding = new ValueBinding<bool>(kGroup, "budgetChartEnabled", true));
            AddBinding(m_HistoryBinding = new RawValueBinding(kGroup, "history", WriteHistory));
            AddBinding(new TriggerBinding<int>(kGroup, "setSpan", months => m_SpanMonths = months));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool enabled = m_Setting == null || m_Setting.EnableBudgetChart;
            m_EnabledBinding.Update(enabled);

            // Only rebuild while the chart is enabled, the Economy panel is open and something changed: a new sample
            // or a new span.
            if (!FeatureGates.BuildsPanelData(enabled, m_PanelSystem.activePanel is EconomyPanel))
            {
                m_LastSampleCount = -1; // rebuild as soon as the chart shows again
                return;
            }
            int samples = m_StatisticsSystem.sampleCount;
            // The live "Now" point changes between samples too (a new building, a budget slider, the game paused),
            // so rebuild at least every 60 frames while the panel is open.
            if (samples == m_LastSampleCount && m_SpanMonths == m_LastSpan && ++m_FramesSinceRebuild < 60) return;
            m_FramesSinceRebuild = 0;
            m_LastSampleCount = samples;
            m_LastSpan = m_SpanMonths;
            m_HistoryBinding.Update();
        }

        private void WriteHistory(IJsonWriter writer)
        {
            m_StatisticsSystem.CompleteWriters();

            // Every series is aligned to the newest sample; series with less history are left-padded with 0.
            var income = new double[kIncomeSources][];
            var expense = new double[kExpenseSources][];
            int length = 1;
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

            int span = BudgetHistoryMath.SamplesForSpan(m_SpanMonths, length);
            int[] picked = BudgetHistoryMath.PickIndices(length, span, kMaxPoints);

            writer.TypeBegin("FiscalOverhaul.BudgetHistory");
            writer.PropertyName("span");
            writer.Write(m_SpanMonths);
            writer.PropertyName("samples");
            writer.Write(length);

            writer.PropertyName("monthsAgo");
            writer.ArrayBegin(picked.Length);
            foreach (int index in picked) writer.Write((float)BudgetHistoryMath.MonthsAgo(index, length));
            writer.ArrayEnd();

            writer.PropertyName("income");
            WriteSeriesSet(writer, income, length, picked);
            writer.PropertyName("expense");
            WriteSeriesSet(writer, expense, length, picked);
            writer.TypeEnd();
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
