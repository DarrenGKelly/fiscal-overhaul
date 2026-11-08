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

        /// <summary>
        /// Spreads one SourceCount-wide sample (income slots, then expense slots) into the game's source order:
        /// result[source] = slots[slot of that source], or <paramref name="fill"/> for a city-only source.
        /// <paramref name="sourceCount"/> is IncomeSource.Count / ExpenseSource.Count.
        /// </summary>
        public static T[] Spread<T>(IReadOnlyList<T> slots, bool income, int sourceCount, T fill)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (slots.Count != SourceCount) throw new ArgumentException("Need SourceCount slots.", nameof(slots));
            var result = new T[sourceCount];
            for (int i = 0; i < sourceCount; i++) result[i] = fill;
            int[] sources = income ? DistrictIncomeSources : DistrictExpenseSources;
            int offset = income ? 0 : DistrictIncomeSources.Length;
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] < sourceCount) result[sources[i]] = slots[offset + i];
            return result;
        }

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

        /// <summary>
        /// What a payer paid in its district, restated at the city rate: paid * cityRate / districtRate, where
        /// districtRate is the rate the payer's district charges now (the city rate with any district tax policy, i.e.
        /// Low Commercial Tax), not the payer's own stored average. Exactly <paramref name="paid"/> when the two rates
        /// are equal (no policy) or the district rate is 0.
        /// </summary>
        public static double AtCityRate(double paid, double districtRate, double cityRate)
        {
            return districtRate == 0 || districtRate == cityRate ? paid : paid * cityRate / districtRate;
        }

        /// <summary>vanillaRow / sumAtCityRate, or 0 when the denominator is 0. A district's tax = its payers' paid sum * this.</summary>
        public static double TaxScale(double vanillaRow, double sumAtCityRate)
        {
            return sumAtCityRate == 0 ? 0 : vanillaRow / sumAtCityRate;
        }

        /// <summary>Tax rows (residential, commercial, industrial, office) and subsidy rows: income and expense.</summary>
        public const int TaxRowCount = 8;

        /// <summary>
        /// For a sample slot: its index among the 8 tax rows (0-3 the tax income slots, 4-7 the subsidy slots, both
        /// residential, commercial, industrial, office), or -1 for a fee or upkeep slot.
        /// </summary>
        public static int TaxRowOf(int slot)
        {
            if (slot >= 0 && slot < 4) return slot;
            int subsidyStart = DistrictIncomeSources.Length;
            if (slot >= subsidyStart && slot < subsidyStart + 4) return 4 + slot - subsidyStart;
            return -1;
        }

        /// <summary>
        /// One sample per area (districts, then "outside"). <paramref name="weights"/> holds areaCount x SourceCount
        /// weights, area-major: for a tax slot the area's payers' paid sum (a subsidy as a positive amount), for any
        /// other slot its share weight. <paramref name="vanilla"/> is the city value per slot (SourceCount wide), and
        /// <paramref name="atCityRate"/> the city-wide paid sums restated at the city rate per tax row
        /// (<see cref="TaxRowOf"/>). Tax slots get paid * <see cref="TaxScale"/>; the others
        /// <see cref="Apportion"/> the vanilla value by the slot's weights across all areas.
        /// </summary>
        public static float[][] Samples(IReadOnlyList<double> weights, int areaCount, IReadOnlyList<double> vanilla, IReadOnlyList<double> atCityRate)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (vanilla == null) throw new ArgumentNullException(nameof(vanilla));
            if (atCityRate == null) throw new ArgumentNullException(nameof(atCityRate));
            if (areaCount < 0 || weights.Count != areaCount * SourceCount)
                throw new ArgumentException("Weights must be areaCount * SourceCount.", nameof(weights));
            if (vanilla.Count != SourceCount) throw new ArgumentException("Vanilla must have SourceCount entries.", nameof(vanilla));
            if (atCityRate.Count != TaxRowCount) throw new ArgumentException("AtCityRate must have TaxRowCount entries.", nameof(atCityRate));

            var samples = new float[areaCount][];
            for (int a = 0; a < areaCount; a++) samples[a] = new float[SourceCount];
            var column = new double[areaCount];
            for (int slot = 0; slot < SourceCount; slot++)
            {
                int taxRow = TaxRowOf(slot);
                if (taxRow >= 0)
                {
                    double scale = TaxScale(vanilla[slot], atCityRate[taxRow]);
                    for (int a = 0; a < areaCount; a++) samples[a][slot] = (float)(weights[a * SourceCount + slot] * scale);
                    continue;
                }
                for (int a = 0; a < areaCount; a++) column[a] = weights[a * SourceCount + slot];
                double[] shares = Apportion(vanilla[slot], column);
                for (int a = 0; a < areaCount; a++) samples[a][slot] = (float)shares[a];
            }
            return samples;
        }
    }

    /// <summary>A capped per-district history of SourceCount-wide samples, with a flat encoding for the save.</summary>
    public class DistrictHistory
    {
        /// <summary>2: with per-service upkeep weights (ServiceUpkeepSave.WriteDistricts); 1 (without) still loads.</summary>
        public const int FormatVersion = 2;

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
