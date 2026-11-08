using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// One district's Taxation tab estimates (<see cref="DistrictTaxEstimate.Compute"/>): what each row brings in a
    /// month at the district's own rates. Every total is the sum of the rounded rows below it, so the rows always add up.
    /// </summary>
    public sealed class DistrictTaxEstimates
    {
        /// <summary>Per education level / resource row, indexed by <see cref="TaxRateModel.DeltaIndex"/>.</summary>
        public readonly long[] Items = new long[TaxRateModel.BlockSize];
        /// <summary>Per density row, indexed by <see cref="DensityTaxTable.Index"/>.</summary>
        public readonly long[] Density = new long[TaxRateModel.DensityCells];
        /// <summary>Per zone (<see cref="TaxZone"/>): its level/resource rows plus its density rows (the zone header).</summary>
        public readonly long[] Zones = new long[DensityTaxTable.ZoneCount];
        /// <summary>Every zone's total: ESTIMATED TOTAL.</summary>
        public long Total;
    }

    /// <summary>
    /// The district view's estimates on the Taxation tab, the district twin of vanilla's (rate × taxable income per
    /// level/resource). Each of the district's payers gives its monthly taxable base: what it paid
    /// (<c>TaxPayer.m_AverageTaxPaid</c>) over the rate it was charged (<see cref="DensityTaxTable.TaxableBase"/>, the
    /// resolver's rate). The base is booked to the payer's level/resource row (a household's split over its workers'
    /// education levels by wage, as <see cref="TaxRateModel.HouseholdDelta"/> weighs them; none: level 0) and, whole, to
    /// its building's density row. A row's estimate is its base times the district's effective rate for that row
    /// (<see cref="DistrictItemRates"/>, <see cref="TaxRateModel.DistrictDensityRates"/>: its overrides, else the
    /// city's). With no override a payer's rows give base × (its rate + its density tax) = what it pays at city rates.
    /// </summary>
    public static class DistrictTaxEstimate
    {
        /// <summary>
        /// A worker's part of a household's base: base × weight / totalWeight (0 with no weight). Burst-safe: the
        /// Taxation tab's job calls it per worker.
        /// </summary>
        public static double Share(double taxableBase, long weight, long totalWeight)
            => totalWeight <= 0 ? 0 : taxableBase * weight / totalWeight;

        /// <summary>A row's estimate: base × rate / 100, rounded half away from zero (0 for no base).</summary>
        public static long Estimate(double taxableBase, int rate)
        {
            if (taxableBase <= 0) return 0;
            return (long)Math.Round(taxableBase * rate / 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Books a household's base (the managed twin of the job's loop, for tests): split over its workers' levels by
        /// <see cref="TaxRateModel.WageWeight"/> (no worker: all to level 0, the level vanilla taxes other income at),
        /// and whole to <paramref name="densityCell"/> (-1: none).
        /// </summary>
        public static void AddHousehold(double[] itemBases, double[] densityBases, double taxableBase, int densityCell, IReadOnlyList<int> workerLevels, IReadOnlyList<int> wageByLevel)
        {
            if (taxableBase <= 0) return;
            int count = workerLevels?.Count ?? 0;
            long total = 0;
            for (int i = 0; i < count; i++) total += TaxRateModel.WageWeight(wageByLevel, ClampLevel(workerLevels[i]));
            if (total == 0) itemBases[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)] += taxableBase;
            for (int i = 0; i < count; i++)
            {
                int level = ClampLevel(workerLevels[i]);
                itemBases[TaxRateModel.DeltaIndex(TaxZone.Residential, level)] += Share(taxableBase, TaxRateModel.WageWeight(wageByLevel, level), total);
            }
            AddDensity(densityBases, densityCell, taxableBase);
        }

        /// <summary>Books a company's base to its (zone, resource) row and its density row (-1: none).</summary>
        public static void AddCompany(double[] itemBases, double[] densityBases, double taxableBase, int densityCell, TaxZone zone, int resourceKey)
        {
            if (taxableBase <= 0) return;
            if (TaxRateModel.IsValidKey(zone, resourceKey)) itemBases[TaxRateModel.DeltaIndex(zone, resourceKey)] += taxableBase;
            AddDensity(densityBases, densityCell, taxableBase);
        }

        /// <summary>
        /// The district's full rate for every level/resource row (<see cref="TaxRateModel.DistrictItemRate"/>: its
        /// overrides, else the city's), indexed by <see cref="TaxRateModel.DeltaIndex"/>.
        /// </summary>
        public static int[] DistrictItemRates(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> cityRates, TaxLimits limits)
        {
            var rates = new int[TaxRateModel.BlockSize];
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
            {
                var zone = (TaxZone)z;
                int keys = zone == TaxZone.Residential ? TaxRateModel.JobLevels : TaxRateModel.ResourceCount;
                for (int k = 0; k < keys; k++)
                    rates[TaxRateModel.DeltaIndex(zone, k)] = TaxRateModel.DistrictItemRate(table, area, areaVersion, cityRates, limits, zone, k);
            }
            return rates;
        }

        /// <summary>
        /// The estimates from the summed bases and the district's rates: each row <see cref="Estimate"/>, each zone the
        /// sum of its rows (level/resource and density), the total the sum of the zones.
        /// </summary>
        public static DistrictTaxEstimates Compute(IReadOnlyList<double> itemBases, IReadOnlyList<double> densityBases, IReadOnlyList<int> itemRates, IReadOnlyList<int> densityRates)
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
                    long e = Estimate(itemBases[i], itemRates[i]);
                    result.Items[i] = e;
                    sum += e;
                }
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                {
                    if (!DensityTaxTable.IsSupported(zone, (Density)d)) continue;
                    int i = DensityTaxTable.Index(zone, (Density)d);
                    long e = Estimate(densityBases[i], densityRates[i]);
                    result.Density[i] = e;
                    sum += e;
                }
                result.Zones[z] = sum;
                result.Total += sum;
            }
            return result;
        }

        private static void AddDensity(double[] densityBases, int cell, double taxableBase)
        {
            if (cell >= 0 && cell < densityBases.Length) densityBases[cell] += taxableBase;
        }

        private static int ClampLevel(int level) => Math.Max(0, Math.Min(TaxRateModel.JobLevels - 1, level));
    }
}
