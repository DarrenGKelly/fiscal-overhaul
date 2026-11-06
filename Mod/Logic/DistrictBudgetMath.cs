using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Pure maths for the per-district budget view. No game or Unity types here, so the Tests project can compile it.
    /// </summary>
    public static class DistrictBudgetMath
    {
        /// <summary>Income source indices a district can have (Game.City.IncomeSource values).</summary>
        public static readonly int[] DistrictIncomeSources =
        {
            0,  // TaxResidential
            1,  // TaxCommercial
            2,  // TaxIndustrial
            11, // TaxOffice
            4,  // FeeElectricity
            13, // FeeWater
            3,  // FeeHealthcare
            6,  // FeeEducation
            12, // FeeGarbage
            9,  // FeeParking
            10, // FeePublicTransport
        };

        /// <summary>Expense source indices a district can have (Game.City.ExpenseSource values).</summary>
        public static readonly int[] DistrictExpenseSources =
        {
            0, // SubsidyResidential
            6, // SubsidyCommercial
            7, // SubsidyIndustrial
            8, // SubsidyOffice
            5, // ServiceUpkeep
        };

        /// <summary>Slots in one sample: the income list, then the expense list.</summary>
        public const int SourceCount = 16;

        /// <summary>True for every source a district cannot have (loan interest, exports, imports, ...).</summary>
        public static bool IsCityOnly(bool income, int source)
        {
            return Array.IndexOf(income ? DistrictIncomeSources : DistrictExpenseSources, source) < 0;
        }

        /// <summary>
        /// Splits a city value by weights: cityValue * w / sum(w) per slot. Negative weights count as 0;
        /// a zero total gives all zeros. (The last slot is "outside districts".)
        /// </summary>
        public static double[] Apportion(double cityValue, IReadOnlyList<double> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            var result = new double[weights.Count];
            double sum = 0;
            for (int i = 0; i < weights.Count; i++) sum += Math.Max(0, weights[i]);
            if (sum <= 0) return result;
            for (int i = 0; i < weights.Count; i++) result[i] = cityValue * Math.Max(0, weights[i]) / sum;
            return result;
        }

        /// <summary>What a payer paid at its own (policy-adjusted) rate, restated at the city rate.</summary>
        public static double AtCityRate(double paid, double payerRate, double cityRate)
        {
            return payerRate == 0 ? paid : paid * cityRate / payerRate;
        }

        /// <summary>vanillaRow / sumAtCityRate, or 0 when the denominator is 0. A district's tax = its payers' paid sum * this.</summary>
        public static double TaxScale(double vanillaRow, double sumAtCityRate)
        {
            return sumAtCityRate == 0 ? 0 : vanillaRow / sumAtCityRate;
        }
    }

    /// <summary>A capped per-district history of SourceCount-wide samples, with a flat encoding for the save.</summary>
    public class DistrictHistory
    {
        public const int FormatVersion = 1;

        /// <summary>36 months of samples (provisional).</summary>
        public const int MaxSamples = 36 * BudgetHistoryMath.SamplesPerMonth;

        private readonly List<float[]> _samples = new List<float[]>();

        public int Count => _samples.Count;

        public void Add(float[] sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (sample.Length != DistrictBudgetMath.SourceCount)
                throw new ArgumentException("Sample must have SourceCount entries.", nameof(sample));
            _samples.Add((float[])sample.Clone());
            if (_samples.Count > MaxSamples) _samples.RemoveRange(0, _samples.Count - MaxSamples);
        }

        /// <summary>One slot across all samples, oldest first.</summary>
        public double[] Series(int slot)
        {
            if (slot < 0 || slot >= DistrictBudgetMath.SourceCount) throw new ArgumentOutOfRangeException(nameof(slot));
            var result = new double[_samples.Count];
            for (int i = 0; i < result.Length; i++) result[i] = _samples[i][slot];
            return result;
        }

        /// <summary>Count x SourceCount floats, oldest sample first.</summary>
        public float[] ToFlat()
        {
            var flat = new float[_samples.Count * DistrictBudgetMath.SourceCount];
            for (int i = 0; i < _samples.Count; i++)
                Array.Copy(_samples[i], 0, flat, i * DistrictBudgetMath.SourceCount, DistrictBudgetMath.SourceCount);
            return flat;
        }

        public static DistrictHistory FromFlat(int count, float[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (count < 0 || data.Length != (long)count * DistrictBudgetMath.SourceCount)
                throw new ArgumentException("Data length must be count * SourceCount.", nameof(data));
            var history = new DistrictHistory();
            for (int i = 0; i < count; i++)
            {
                var sample = new float[DistrictBudgetMath.SourceCount];
                Array.Copy(data, i * DistrictBudgetMath.SourceCount, sample, 0, DistrictBudgetMath.SourceCount);
                history.Add(sample);
            }
            return history;
        }
    }
}
