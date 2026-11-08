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
    /// (zone rate + education/product offset): a view of a <see cref="TaxRateTable"/>'s <see cref="TaxType.Density"/>
    /// entries (key = <see cref="Density"/>), which holds the storage. Pure data: the game systems copy it into a
    /// native array for the tax engine. The comma-separated text form is the Options template new cities are seeded from.
    /// </summary>
    public sealed class DensityTaxTable
    {
        public const int ZoneCount = 4;
        public const int DensityCount = 3;

        /// <summary>
        /// Offsets only change revenue, not demand or happiness (those read vanilla rates inside game code),
        /// so they're capped to keep that from being a free money lever.
        /// </summary>
        public const int MaxOffset = TaxRateTable.DensityCap;

        private readonly TaxRateTable m_Rates;

        /// <summary>A table over its own, empty <see cref="TaxRateTable"/>.</summary>
        public DensityTaxTable() : this(new TaxRateTable()) { }

        /// <summary>A view of <paramref name="rates"/>' density entries: reads and writes go straight to it.</summary>
        public DensityTaxTable(TaxRateTable rates)
        {
            m_Rates = rates ?? throw new ArgumentNullException(nameof(rates));
        }

        /// <summary>The table this view reads and writes.</summary>
        public TaxRateTable Rates => m_Rates;

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

        public int Get(TaxZone zone, Density density) => m_Rates.Get(TaxType.Density, (int)zone, (int)density);

        /// <summary>Sets an offset, clamped to ±<see cref="MaxOffset"/>. Unsupported cells stay 0. Returns the stored value.</summary>
        public int Set(TaxZone zone, Density density, int offset)
        {
            if (!IsSupported(zone, density)) return 0;
            return m_Rates.Set(TaxType.Density, (int)zone, (int)density, offset);
        }

        public bool IsEmpty => !m_Rates.Any(TaxType.Density);

        /// <summary>A copy of the flat offsets array, indexed by <see cref="Index"/>.</summary>
        public int[] ToArray()
        {
            var offsets = new int[ZoneCount * DensityCount];
            for (int z = 0; z < ZoneCount; z++)
                for (int d = 0; d < DensityCount; d++)
                    offsets[z * DensityCount + d] = Get((TaxZone)z, (Density)d);
            return offsets;
        }

        /// <summary>
        /// The offsets the tax systems charge, indexed by <see cref="Index"/>: the stored ones while density taxes are
        /// enabled, all 0 while disabled. The stored offsets are never touched, so enabling again restores them.
        /// </summary>
        public int[] EffectiveOffsets(bool enabled) => enabled ? ToArray() : new int[ZoneCount * DensityCount];

        /// <summary>Whether any building pays a density offset: density taxes enabled and at least one offset set.</summary>
        public bool IsCharged(bool enabled) => enabled && !IsEmpty;

        /// <summary>
        /// Rough extra (or lost) monthly revenue from an offset, given what the buildings in that cell pay per
        /// month now and their average stored (vanilla) rate. Used for the estimate column only.
        /// <paramref name="monthlyTaxPaid"/> is TaxPayer.m_AverageTaxPaid, which TaxSystem sets from the tax it just
        /// charged, at the stored rate plus the offset this cell already pays. So the taxable base is the paid amount
        /// over that charged rate, not over the stored one (which would overstate the estimate by (rate + offset) / rate).
        /// Right after the offset changes, the paid amounts still reflect the old one until each payer's next payment
        /// (1/32 of a month).
        /// </summary>
        public static long EstimateExtraRevenue(long monthlyTaxPaid, double averageRate, int offset)
        {
            if (offset == 0 || averageRate <= 0 || monthlyTaxPaid == 0) return 0;
            double chargedRate = averageRate + offset;
            if (chargedRate <= 0) return 0; // charged nothing, so the paid amount says nothing about the base
            double taxableBase = monthlyTaxPaid * 100.0 / chargedRate;
            return (long)Math.Round(taxableBase * offset / 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Compact text form for the Options template: 12 comma-separated integers.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            int[] offsets = ToArray();
            for (int i = 0; i < offsets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(offsets[i].ToString(CultureInfo.InvariantCulture));
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
