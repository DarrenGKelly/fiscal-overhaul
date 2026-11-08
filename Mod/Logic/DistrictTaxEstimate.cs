using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// One district's Taxation tab estimates (<see cref="DistrictTaxEstimate.Compute"/>): what each row brings in a
    /// month. Every total is the sum of the rounded rows below it, so the rows always add up.
    /// </summary>
    public sealed class DistrictTaxEstimates
    {
        /// <summary>Per education level / resource row, indexed by <see cref="TaxRateModel.DeltaIndex"/>.</summary>
        public readonly long[] Items = new long[TaxRateModel.BlockSize];
        /// <summary>Per density row, indexed by <see cref="DensityTaxTable.Index"/> (negative: a subsidy).</summary>
        public readonly long[] Density = new long[TaxRateModel.DensityCells];
        /// <summary>Per zone (<see cref="TaxZone"/>): its level/resource rows plus its density rows (the zone header).</summary>
        public readonly long[] Zones = new long[DensityTaxTable.ZoneCount];
        /// <summary>Every zone's total: ESTIMATED TOTAL.</summary>
        public long Total;
    }

    /// <summary>
    /// The district view's estimates on the Taxation tab, booked as the city books the engine's charges: each of the
    /// district's payers gives its <see cref="PayerTaxParts"/> (its own rates: the district's overrides, else the
    /// city's, clamped as charged). Its income part goes to its level/resource row (a household's split over its
    /// workers' education levels by wage, as <see cref="TaxRateModel.HouseholdDelta"/> weighs them; none: level 0), its
    /// density part to its building's density row. With no override a payer's rows add up to what it pays at city rates.
    /// </summary>
    public static class DistrictTaxEstimate
    {
        /// <summary>
        /// A worker's part of a household's amount: amount × weight / totalWeight (0 with no weight). Burst-safe: the
        /// Taxation tab's job calls it per worker.
        /// </summary>
        public static double Share(double amount, long weight, long totalWeight)
            => totalWeight <= 0 ? 0 : amount * weight / totalWeight;

        /// <summary>A row's amount rounded half away from zero, as the tab shows it.</summary>
        public static long Round(double amount) => (long)Math.Round(amount, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Books a household (the managed twin of the job's loop, for tests): its income part split over its workers'
        /// levels by <see cref="TaxRateModel.WageWeight"/> (no worker: all to level 0, the level vanilla taxes other
        /// income at), its density part to <paramref name="densityCell"/> (-1: none).
        /// </summary>
        public static void AddHousehold(double[] items, double[] density, PayerTaxParts parts, int densityCell, IReadOnlyList<int> workerLevels, IReadOnlyList<int> wageByLevel)
        {
            if (parts.Base <= 0) return;
            double income = parts.IncomePart;
            int count = workerLevels?.Count ?? 0;
            long total = 0;
            for (int i = 0; i < count; i++) total += TaxRateModel.WageWeight(wageByLevel, ClampLevel(workerLevels[i]));
            if (total == 0) items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)] += income;
            for (int i = 0; i < count; i++)
            {
                int level = ClampLevel(workerLevels[i]);
                items[TaxRateModel.DeltaIndex(TaxZone.Residential, level)] += Share(income, TaxRateModel.WageWeight(wageByLevel, level), total);
            }
            AddDensity(density, densityCell, parts.DensityPart);
        }

        /// <summary>Books a company: its income part to its (zone, resource) row, its density part to its density row (-1: none).</summary>
        public static void AddCompany(double[] items, double[] density, PayerTaxParts parts, int densityCell, TaxZone zone, int resourceKey)
        {
            if (parts.Base <= 0) return;
            if (TaxRateModel.IsValidKey(zone, resourceKey)) items[TaxRateModel.DeltaIndex(zone, resourceKey)] += parts.IncomePart;
            AddDensity(density, densityCell, parts.DensityPart);
        }

        /// <summary>The estimates from the summed amounts: each row rounded, each zone the sum of its rows (level/resource and density), the total the sum of the zones.</summary>
        public static DistrictTaxEstimates Compute(IReadOnlyList<double> items, IReadOnlyList<double> density)
        {
            var result = new DistrictTaxEstimates();
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
            {
                var zone = (TaxZone)z;
                int keys = zone == TaxZone.Residential ? TaxRateModel.JobLevels : TaxRateModel.ResourceCount;
                long sum = 0;
                for (int k = 0; k < keys; k++)
                {
                    int i = TaxRateModel.DeltaIndex(zone, k);
                    long e = Round(items[i]);
                    result.Items[i] = e;
                    sum += e;
                }
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                {
                    if (!DensityTaxTable.IsSupported(zone, (Density)d)) continue;
                    int i = DensityTaxTable.Index(zone, (Density)d);
                    long e = Round(density[i]);
                    result.Density[i] = e;
                    sum += e;
                }
                result.Zones[z] = sum;
                result.Total += sum;
            }
            return result;
        }

        private static void AddDensity(double[] density, int cell, double amount)
        {
            if (cell >= 0 && cell < density.Length) density[cell] += amount;
        }

        private static int ClampLevel(int level) => Math.Max(0, Math.Min(TaxRateModel.JobLevels - 1, level));
    }
}
