using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Pure maths for turning the game's per-sample budget statistics into chart series.
    /// No game or Unity types here, so the Tests project can compile it directly.
    /// </summary>
    public static class BudgetHistoryMath
    {
        /// <summary>The game samples city statistics 32 times per in-game month (CityStatisticsSystem.kUpdatesPerDay).</summary>
        public const int SamplesPerMonth = 32;

        /// <summary>
        /// Each statistic sample holds what was added during that 1/32 of a month: BudgetApplySystem adds the
        /// current monthly amount / 1024 at each of its 1024 ticks a month, so a sample is (rate at that time) / 32.
        /// The monthly rate at sample i is therefore that sample times <paramref name="samplesPerMonth"/>, the
        /// figure the vanilla Budget tab shows. (A sum of the trailing month instead averages the past month, which
        /// lags a growing city: about ¢40k against the panel's ¢52k in the first playtest.)
        /// </summary>
        public static double[] MonthlyRates(IReadOnlyList<double> sampleDeltas, int samplesPerMonth = SamplesPerMonth)
        {
            if (sampleDeltas == null) throw new ArgumentNullException(nameof(sampleDeltas));
            if (samplesPerMonth < 1) throw new ArgumentOutOfRangeException(nameof(samplesPerMonth));

            var result = new double[sampleDeltas.Count];
            for (int i = 0; i < sampleDeltas.Count; i++) result[i] = sampleDeltas[i] * samplesPerMonth;
            return result;
        }

        /// <summary>
        /// How many trailing samples a time span covers. <paramref name="months"/> &lt;= 0 means "all history".
        /// Always at least 1 sample when any exist.
        /// </summary>
        public static int SamplesForSpan(int months, int available, int samplesPerMonth = SamplesPerMonth)
        {
            if (available <= 0) return 0;
            if (months <= 0) return available;
            long wanted = (long)months * samplesPerMonth + 1; // +1 so a 1-month span shows both ends
            return (int)Math.Min(available, Math.Max(1, wanted));
        }

        /// <summary>
        /// Indices into a series of length <paramref name="length"/> covering its last <paramref name="span"/>
        /// samples with at most <paramref name="maxPoints"/> points. Evenly strided, and always ends on the
        /// newest sample so the chart's right edge is the current value.
        /// </summary>
        public static int[] PickIndices(int length, int span, int maxPoints)
        {
            if (maxPoints < 2) throw new ArgumentOutOfRangeException(nameof(maxPoints));
            if (length <= 0 || span <= 0) return new int[0];
            span = Math.Min(span, length);
            int start = length - span;

            if (span <= maxPoints)
            {
                var all = new int[span];
                for (int i = 0; i < span; i++) all[i] = start + i;
                return all;
            }

            var picked = new int[maxPoints];
            double step = (span - 1) / (double)(maxPoints - 1);
            for (int i = 0; i < maxPoints; i++)
            {
                picked[i] = start + (int)Math.Round(i * step);
            }
            picked[maxPoints - 1] = length - 1;
            return picked;
        }

        /// <summary>Months relative to the newest sample (0 = now, -1 = one month ago) for each index.</summary>
        public static double MonthsAgo(int index, int length, int samplesPerMonth = SamplesPerMonth)
        {
            return (index - (length - 1)) / (double)samplesPerMonth;
        }
    }
}
