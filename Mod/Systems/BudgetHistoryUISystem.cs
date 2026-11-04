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
    ///   history     raw  { span, samples, monthsAgo: number[], income: number[14][], expense: number[15][] }
    ///   setSpan     trigger(int months)  months &lt;= 0 = all history
    /// Expense values are positive amounts (the UI decides the sign).
    /// </summary>
    public partial class BudgetHistoryUISystem : UISystemBase
    {
        public const string kGroup = "fiscalOverhaul";
        private const int kMaxPoints = 240;
        private const int kIncomeSources = (int)IncomeSource.Count;
        private const int kExpenseSources = (int)ExpenseSource.Count;

        private CityStatisticsSystem m_StatisticsSystem;
        private GamePanelUISystem m_PanelSystem;
        private RawValueBinding m_HistoryBinding;

        private int m_SpanMonths = 12;
        private int m_LastSampleCount = -1;
        private int m_LastSpan = int.MinValue;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_StatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_PanelSystem = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            AddBinding(m_HistoryBinding = new RawValueBinding(kGroup, "history", WriteHistory));
            AddBinding(new TriggerBinding<int>(kGroup, "setSpan", months => m_SpanMonths = months));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // Only rebuild while the Economy panel is open and something changed: a new sample or a new span.
            if (!(m_PanelSystem.activePanel is EconomyPanel)) return;
            int samples = m_StatisticsSystem.sampleCount;
            if (samples == m_LastSampleCount && m_SpanMonths == m_LastSpan) return;
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
                income[i] = ReadMonthly(StatisticType.Income, i);
                if (income[i].Length > length) length = income[i].Length;
            }
            for (int i = 0; i < kExpenseSources; i++)
            {
                expense[i] = ReadMonthly(StatisticType.Expense, i);
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
                int count = samples.Length > 1 ? samples.Length - 1 : samples.Length;
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
