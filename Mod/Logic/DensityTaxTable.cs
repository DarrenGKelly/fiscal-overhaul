using System;
using System.Globalization;
using System.Text;

namespace FiscalOverhaul.Logic
{
    /// <summary>Zone groups taxes are set for. Values match the vanilla TaxAreaType order minus "City" (index 0).</summary>
    public enum TaxZone
    {
        Residential = 0,
        Commercial = 1,
        Industrial = 2,
        Office = 3,
    }

    /// <summary>Matches Game.Prefabs.ZoneDensity's order.</summary>
    public enum Density
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// The density tax offsets, in percentage points added on top of the vanilla rate a building already pays
    /// (zone rate + education/product offset). Pure data: the game systems copy it into a native array.
    /// </summary>
    public sealed class DensityTaxTable
    {
        public const int ZoneCount = 4;
        public const int DensityCount = 3;

        /// <summary>
        /// Offsets only change revenue, not demand or happiness (those read vanilla rates inside game code),
        /// so they're capped to keep that from being a free money lever.
        /// </summary>
        public const int MaxOffset = 5;

        private readonly int[] m_Offsets = new int[ZoneCount * DensityCount];

        /// <summary>Densities the game actually produces for each zone (PropertyUtils.GetZoneDensity).</summary>
        public static bool IsSupported(TaxZone zone, Density density)
        {
            switch (zone)
            {
                case TaxZone.Residential: return true;
                case TaxZone.Commercial: return density != Density.Medium;
                case TaxZone.Office: return density != Density.Medium;
                case TaxZone.Industrial: return density == Density.Low;
                default: return false;
            }
        }

        public static int Index(TaxZone zone, Density density) => (int)zone * DensityCount + (int)density;

        public int Get(TaxZone zone, Density density) => m_Offsets[Index(zone, density)];

        /// <summary>Sets an offset, clamped to ±<see cref="MaxOffset"/>. Unsupported cells stay 0. Returns the stored value.</summary>
        public int Set(TaxZone zone, Density density, int offset)
        {
            if (!IsSupported(zone, density)) return 0;
            int clamped = Math.Max(-MaxOffset, Math.Min(MaxOffset, offset));
            m_Offsets[Index(zone, density)] = clamped;
            return clamped;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (int o in m_Offsets) if (o != 0) return false;
                return true;
            }
        }

        /// <summary>A copy of the flat offsets array, indexed by <see cref="Index"/>.</summary>
        public int[] ToArray() => (int[])m_Offsets.Clone();

        /// <summary>
        /// The offsets the tax systems charge, indexed by <see cref="Index"/>: the stored ones while density taxes are
        /// enabled, all 0 while disabled. The stored offsets are never touched, so enabling again restores them.
        /// </summary>
        public int[] EffectiveOffsets(bool enabled) => enabled ? ToArray() : new int[m_Offsets.Length];

        /// <summary>Whether any building pays a density offset: density taxes enabled and at least one offset set.</summary>
        public bool IsCharged(bool enabled) => enabled && !IsEmpty;

        /// <summary>
        /// The rate actually charged: base + offset, never below 0 (a negative rate would pay citizens).
        /// Returns the delta applied so it can be undone exactly.
        /// </summary>
        public static int AppliedDelta(int baseRate, int offset)
        {
            int result = baseRate + offset;
            if (result < 0) result = 0;
            return result - baseRate;
        }

        /// <summary>
        /// Rough extra (or lost) monthly revenue from an offset, given what the buildings in that cell pay per
        /// month now and at what average rate. Used for the estimate column only.
        /// </summary>
        public static long EstimateExtraRevenue(long monthlyTaxPaid, double averageRate, int offset)
        {
            if (offset == 0 || averageRate <= 0 || monthlyTaxPaid == 0) return 0;
            double taxableBase = monthlyTaxPaid * 100.0 / averageRate;
            return (long)Math.Round(taxableBase * offset / 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Compact text form for the settings file: 12 comma-separated integers.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Offsets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(m_Offsets[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Parses <see cref="Serialize"/>'s output. Bad or missing input gives an all-zero table, never an exception.</summary>
        public static DensityTaxTable Parse(string text)
        {
            var table = new DensityTaxTable();
            if (string.IsNullOrWhiteSpace(text)) return table;

            string[] parts = text.Split(',');
            if (parts.Length != ZoneCount * DensityCount) return table;

            for (int z = 0; z < ZoneCount; z++)
            {
                for (int d = 0; d < DensityCount; d++)
                {
                    if (int.TryParse(parts[z * DensityCount + d].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    {
                        table.Set((TaxZone)z, (Density)d, value);
                    }
                }
            }
            return table;
        }
    }
}
