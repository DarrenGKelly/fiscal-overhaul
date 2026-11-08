using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Vanilla's tax limits (TaxParameterData) as the model needs them: each zone rate's, the education-level rates'
    /// (<c>m_JobLevelTaxLimits</c>) and the resource rates' (<c>m_ResourceTaxLimits</c>).
    /// </summary>
    public sealed class TaxLimits
    {
        public readonly int[] ZoneMin = new int[DensityTaxTable.ZoneCount];
        public readonly int[] ZoneMax = new int[DensityTaxTable.ZoneCount];
        public int JobLevelMin;
        public int JobLevelMax;
        public int ResourceMin;
        public int ResourceMax;

        /// <summary>Wide limits, for tests and before the game's data is known.</summary>
        public static TaxLimits Wide()
        {
            var l = new TaxLimits { JobLevelMin = -100, JobLevelMax = 100, ResourceMin = -100, ResourceMax = 100 };
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++) { l.ZoneMin[z] = -100; l.ZoneMax[z] = 100; }
            return l;
        }

        public int ItemMin(TaxZone zone) => zone == TaxZone.Residential ? JobLevelMin : ResourceMin;
        public int ItemMax(TaxZone zone) => zone == TaxZone.Residential ? JobLevelMax : ResourceMax;
    }

    /// <summary>
    /// The income-tax rate model: the city's rates (vanilla TaxSystem's 92-int array) and a district's own values on top
    /// (<see cref="TaxRateTable"/> overrides), mirroring how vanilla combines them (TaxSystem.cs, game version 1.6.0f1):
    /// <list type="bullet">
    /// <item>a zone rate is <c>rates[0] + rates[1 + zone]</c> (GetTaxRate);</item>
    /// <item>an education-level rate is the residential zone rate + <c>rates[5 + level]</c> (GetResidentialTaxRate);</item>
    /// <item>a resource rate is the zone rate + <c>rates[10 + resource]</c> (Commercial) or <c>rates[51 + resource]</c>
    /// (Industrial and Office share the offsets; GetCommercial/Industrial/OfficeTaxRate).</item>
    /// </list>
    /// A district's level/resource rate is its zone rate (its override, else the city's) plus its offset (its override,
    /// else the city's), clamped to vanilla's limits; with no override in the zone it is the city's rate exactly.
    ///
    /// The engine charges a payer in a district the vanilla rate plus the <b>difference</b> between the district's rate
    /// and the city's for what the payer earns from (<see cref="DistrictDeltas"/>, <see cref="HouseholdDelta"/>): the
    /// income systems keep blending city rates into <c>TaxPayer.m_AverageTaxRate</c>, and the difference moves it to the
    /// district's. Pure; the engine copies its results into native arrays.
    /// </summary>
    public static class TaxRateModel
    {
        public const int JobLevels = 5;
        /// <summary>Vanilla's resource count (TaxSystem's array: 10 + 41 commercial, 51 + 41 industrial/office = 92).</summary>
        public const int ResourceCount = 41;
        public const int VanillaRateCount = 92;
        /// <summary>Keys per zone in a district's delta block (≥ <see cref="ResourceCount"/>).</summary>
        public const int ZoneStride = 64;
        /// <summary>A district's delta block: <see cref="DeltaIndex"/>.</summary>
        public const int BlockSize = DensityTaxTable.ZoneCount * ZoneStride;
        public const int DensityCells = DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount;

        public static int DeltaIndex(TaxZone zone, int key) => (int)zone * ZoneStride + key;

        /// <summary>Whether a key names an education level (Residential, 0..4) or a resource (other zones, 0..40).</summary>
        public static bool IsValidKey(TaxZone zone, int key)
            => key >= 0 && key < (zone == TaxZone.Residential ? JobLevels : ResourceCount) && (int)zone >= 0 && (int)zone < DensityTaxTable.ZoneCount;

        /// <summary>The index of a level/resource offset in vanilla's rate array.</summary>
        public static int OffsetIndex(TaxZone zone, int key)
        {
            switch (zone)
            {
                case TaxZone.Residential: return 5 + key;
                case TaxZone.Commercial: return 10 + key;
                default: return 51 + key;
            }
        }

        public static int CityZoneRate(IReadOnlyList<int> rates, TaxZone zone) => rates[0] + rates[1 + (int)zone];
        public static int CityOffset(IReadOnlyList<int> rates, TaxZone zone, int key) => rates[OffsetIndex(zone, key)];
        public static int CityItemRate(IReadOnlyList<int> rates, TaxZone zone, int key) => CityZoneRate(rates, zone) + CityOffset(rates, zone, key);

        /// <summary>The district's zone rate: its override, else the city's.</summary>
        public static int DistrictZoneRate(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> rates, TaxZone zone)
            => table.TryGet(TaxType.ZoneRate, (int)zone, 0, area, areaVersion, out int v) ? v : CityZoneRate(rates, zone);

        /// <summary>
        /// The district's education-level or resource rate: (its zone rate) + (its offset override, else the city's),
        /// clamped to vanilla's limits; the city's rate exactly when the district overrides neither.
        /// </summary>
        public static int DistrictItemRate(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> rates, TaxLimits limits, TaxZone zone, int key)
        {
            bool zoneOverride = table.TryGet(TaxType.ZoneRate, (int)zone, 0, area, areaVersion, out int zoneRate);
            bool itemOverride = table.TryGet(TaxType.ResourceRate, (int)zone, key, area, areaVersion, out int offset);
            if (!zoneOverride && !itemOverride) return CityItemRate(rates, zone, key);
            if (!zoneOverride) zoneRate = CityZoneRate(rates, zone);
            if (!itemOverride) offset = CityOffset(rates, zone, key);
            return Clamp(zoneRate + offset, limits.ItemMin(zone), limits.ItemMax(zone));
        }

        /// <summary>The district's density tax rate for a cell: its override, else the city's.</summary>
        public static int DistrictDensityRate(TaxRateTable table, int area, int areaVersion, TaxZone zone, Density density)
            => table.TryGet(TaxType.Density, (int)zone, (int)density, area, areaVersion, out int v) ? v : table.Get(TaxType.Density, (int)zone, (int)density);

        /// <summary>
        /// The district's rate minus the city's for every level/resource, indexed by <see cref="DeltaIndex"/>: what the
        /// engine adds to a payer's vanilla rate. All 0 for a district with no zone/level/resource override.
        /// </summary>
        public static int[] DistrictDeltas(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> rates, TaxLimits limits)
        {
            var deltas = new int[BlockSize];
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
            {
                var zone = (TaxZone)z;
                int keys = zone == TaxZone.Residential ? JobLevels : ResourceCount;
                for (int k = 0; k < keys; k++)
                    deltas[DeltaIndex(zone, k)] = DistrictItemRate(table, area, areaVersion, rates, limits, zone, k) - CityItemRate(rates, zone, k);
            }
            return deltas;
        }

        /// <summary>The density tax rates a district's buildings pay, indexed by <see cref="DensityTaxTable.Index"/>; all 0 while density taxes are off.</summary>
        public static int[] DistrictDensityRates(TaxRateTable table, int area, int areaVersion, bool densityEnabled)
        {
            var cells = new int[DensityCells];
            if (!densityEnabled) return cells;
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                    if (DensityTaxTable.IsSupported((TaxZone)z, (Density)d))
                        cells[DensityTaxTable.Index((TaxZone)z, (Density)d)] = DistrictDensityRate(table, area, areaVersion, (TaxZone)z, (Density)d);
            return cells;
        }

        /// <summary>
        /// Sets a district's value from what its slider shows (a full rate; the density tax's own rate), clamped to
        /// vanilla's limits (density: its own range). A value equal to what the district would have without this
        /// override (the city's, or for a level/resource its zone rate plus the city's offset) removes the override, so
        /// dragging a slider back to the city's value makes it follow the city again. A level/resource rate is stored as
        /// vanilla stores it: the offset from the district's zone rate. False for a bad type, zone or key.
        /// </summary>
        public static bool SetDistrictValue(TaxRateTable table, TaxType type, TaxZone zone, int key, int area, int areaVersion, int value, IReadOnlyList<int> rates, TaxLimits limits)
        {
            if ((int)zone < 0 || (int)zone >= DensityTaxTable.ZoneCount || (area == TaxRateTable.CityArea && areaVersion == 0)) return false;
            switch (type)
            {
                case TaxType.ZoneRate:
                {
                    int v = Clamp(value, limits.ZoneMin[(int)zone], limits.ZoneMax[(int)zone]);
                    if (v == CityZoneRate(rates, zone)) table.Remove(TaxType.ZoneRate, (int)zone, 0, area, areaVersion);
                    else table.Set(TaxType.ZoneRate, (int)zone, 0, area, areaVersion, v);
                    return true;
                }
                case TaxType.ResourceRate:
                {
                    if (!IsValidKey(zone, key)) return false;
                    int v = Clamp(value, limits.ItemMin(zone), limits.ItemMax(zone));
                    table.Remove(TaxType.ResourceRate, (int)zone, key, area, areaVersion);
                    if (v == DistrictItemRate(table, area, areaVersion, rates, limits, zone, key)) return true;
                    int districtZone = DistrictZoneRate(table, area, areaVersion, rates, zone);
                    table.Set(TaxType.ResourceRate, (int)zone, key, area, areaVersion, v - districtZone);
                    return true;
                }
                case TaxType.Density:
                {
                    if (key < 0 || key >= DensityTaxTable.DensityCount || !DensityTaxTable.IsSupported(zone, (Density)key)) return false;
                    int v = Clamp(value, TaxRateTable.DensityMin, TaxRateTable.DensityMax);
                    if (v == table.Get(TaxType.Density, (int)zone, key)) table.Remove(TaxType.Density, (int)zone, key, area, areaVersion);
                    else table.Set(TaxType.Density, (int)zone, key, area, areaVersion, v);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// The weighted mean of per-worker deltas, rounded half away from zero: Σ(weight × delta) / Σweight. 0 with no
        /// weight. Burst-safe (integers only): the engine accumulates the sums over a household's workers and calls this.
        /// </summary>
        public static int WeightedDelta(long sumWeightedDelta, long sumWeight)
        {
            if (sumWeight <= 0) return 0;
            long twice = 2 * sumWeightedDelta;
            long q = twice >= 0 ? (twice + sumWeight) / (2 * sumWeight) : -((-twice + sumWeight) / (2 * sumWeight));
            return (int)q;
        }

        /// <summary>
        /// A household's income-tax difference: the mean of (district rate − city rate) at each worker's education
        /// level, weighted by that level's wage (<paramref name="wageByLevel"/>; equal weights when null or all 0). A
        /// household with no worker counts as level 0, the level vanilla taxes other income at. The managed twin of the
        /// engine's loop, for tests.
        /// </summary>
        public static int HouseholdDelta(IReadOnlyList<int> levelDeltas, IReadOnlyList<int> workerLevels, IReadOnlyList<int> wageByLevel)
        {
            long sum = 0, weight = 0;
            int count = workerLevels?.Count ?? 0;
            if (count == 0) return levelDeltas[0];
            for (int i = 0; i < count; i++)
            {
                int level = Math.Max(0, Math.Min(JobLevels - 1, workerLevels[i]));
                long w = WageWeight(wageByLevel, level);
                sum += w * levelDeltas[level];
                weight += w;
            }
            return WeightedDelta(sum, weight);
        }

        /// <summary>A level's weight: its wage when known and positive, else 1.</summary>
        public static long WageWeight(IReadOnlyList<int> wageByLevel, int level)
        {
            if (wageByLevel == null || level >= wageByLevel.Count || wageByLevel[level] <= 0) return 1;
            return wageByLevel[level];
        }

        /// <summary>
        /// THE resident rate (happiness, home choice, demand): the city's rate for one education level plus the home
        /// district's difference at that level only (<see cref="DistrictDeltas"/>) plus the home's density tax, inside
        /// the job-level limits (<see cref="EffectiveTaxRate.Compute"/>, as the engine charges a household with income).
        /// All 0 gives <paramref name="vanillaLevelRate"/> exactly. Burst-safe: <c>TaxRateResolver</c> calls it per citizen.
        /// </summary>
        public static int ResidentRate(int vanillaLevelRate, int levelDelta, int densityTax, int min, int max)
            => EffectiveTaxRate.Compute(vanillaLevelRate, densityTax, levelDelta, min, max, 1);

        /// <summary>
        /// The rate a resident of each education level pays in a home of <paramref name="densityCell"/> (-1: none) in an
        /// area (the city: <see cref="TaxRateTable.CityArea"/>, 0), the managed composition of what the resolver does
        /// in its jobs: <see cref="DistrictDeltas"/>, <see cref="DistrictDensityRates"/>, <see cref="ResidentRate"/>.
        /// </summary>
        public static int[] ResidentRates(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> rates, TaxLimits limits, bool densityEnabled, int densityCell)
        {
            int[] deltas = DistrictDeltas(table, area, areaVersion, rates, limits);
            int[] density = DistrictDensityRates(table, area, areaVersion, densityEnabled);
            int densityTax = densityCell >= 0 && densityCell < DensityCells ? density[densityCell] : 0;
            var result = new int[JobLevels];
            for (int level = 0; level < JobLevels; level++)
                result[level] = ResidentRate(CityItemRate(rates, TaxZone.Residential, level), deltas[DeltaIndex(TaxZone.Residential, level)], densityTax, limits.JobLevelMin, limits.JobLevelMax);
            return result;
        }

        /// <summary>
        /// The rate the engine charges a household (the managed twin of <c>TaxRateResolver.ChargedRate</c>): its stored
        /// vanilla rate plus the wage-weighted district difference over its workers' levels
        /// (<see cref="HouseholdDelta"/>) plus its home's density tax, clamped by <see cref="EffectiveTaxRate.Compute"/>.
        /// </summary>
        public static int HouseholdChargedRate(TaxRateTable table, int area, int areaVersion, IReadOnlyList<int> rates, TaxLimits limits, bool densityEnabled, int densityCell, int storedRate, IReadOnlyList<int> workerLevels, IReadOnlyList<int> wageByLevel, int untaxedIncome)
        {
            int[] deltas = DistrictDeltas(table, area, areaVersion, rates, limits);
            var levelDeltas = new int[JobLevels];
            for (int level = 0; level < JobLevels; level++) levelDeltas[level] = deltas[DeltaIndex(TaxZone.Residential, level)];
            int[] density = DistrictDensityRates(table, area, areaVersion, densityEnabled);
            int densityTax = densityCell >= 0 && densityCell < DensityCells ? density[densityCell] : 0;
            int incomeDelta = area == TaxRateTable.CityArea && areaVersion == 0 ? 0 : HouseholdDelta(levelDeltas, workerLevels, wageByLevel);
            return EffectiveTaxRate.Compute(storedRate, densityTax, incomeDelta, limits.JobLevelMin, limits.JobLevelMax, untaxedIncome);
        }
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
