using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The demand view's maths (DemandTaxViewSystem): vanilla's demand systems read only city-level tax rates (vanilla
    /// TaxSystem's 92-int array), so the mod hands them a second array, the <b>view</b>, where each rate slot is the
    /// vanilla rate plus the mean adjustment (density, district) its payers are actually charged, each payer weighing 1.
    ///
    /// The array layout is vanilla TaxSystem's: slot 0 the total rate; 1..4 the Residential / Commercial / Industrial /
    /// Office offsets on it; 5..9 the five education levels' offsets on Residential; 10..50 each resource's offset on
    /// Commercial; 51..91 each resource's offset on Industrial or, for a weightless resource, Office. A slot's full rate
    /// is slot 0 + its area's offset + its own offset. The view changes only the leaf offsets (5..91), so the city-level
    /// slots 0..4 stay vanilla's.
    /// </summary>
    public static class DemandViewMath
    {
        public const int SlotCount = 92;
        public const int ResidentialFirst = 5;
        public const int CommercialFirst = 10;
        public const int IndustrialFirst = 51;

        /// <summary>The area slot (TaxAreaType: 1 Residential, 2 Commercial, 3 Industrial, 4 Office).</summary>
        public const int AreaResidential = 1;
        public const int AreaCommercial = 2;
        public const int AreaIndustrial = 3;
        public const int AreaOffice = 4;

        /// <summary>
        /// The payers' mean adjustment in whole points: <paramref name="sum"/> / <paramref name="count"/>, rounded to
        /// nearest with halves to even, as Unity.Mathematics math.round (System.Math.Round) does. No payers: 0.
        /// </summary>
        public static int MeanAdjustment(long sum, int count)
        {
            if (count <= 0) return 0;
            return (int)Math.Round((double)sum / count, MidpointRounding.ToEven);
        }

        /// <summary>
        /// One payer's adjustment as the engine charges it on income: <see cref="EffectiveTaxRate.Compute"/> at
        /// <paramref name="baseRate"/> (its stored vanilla rate) minus that rate, so the limits' clamp is included.
        /// Burst-safe.
        /// </summary>
        public static int PayerAdjustment(int baseRate, int densityAdjustment, int districtAdjustment, int min, int max)
        {
            return EffectiveTaxRate.Compute(baseRate, densityAdjustment, districtAdjustment, min, max, 1) - baseRate;
        }

        /// <summary>
        /// The area slot whose offset a leaf slot adds to, or 0 for slots 0..4 (and out-of-range slots).
        /// <paramref name="weightless"/>: an industrial-range slot whose resource weighs nothing is Office.
        /// </summary>
        public static int AreaOf(int slot, bool weightless)
        {
            if (slot >= IndustrialFirst && slot < SlotCount) return weightless ? AreaOffice : AreaIndustrial;
            if (slot >= CommercialFirst) return slot < SlotCount ? AreaCommercial : 0;
            if (slot >= ResidentialFirst) return AreaResidential;
            return 0;
        }

        /// <summary>
        /// A leaf slot's view offset: its full rate (<paramref name="vanillaFullRate"/>) moved by
        /// <paramref name="meanAdjustment"/>, clamped to [<paramref name="min"/> (never below 0), <paramref name="max"/>]
        /// but never past where vanilla put it (as <see cref="EffectiveTaxRate"/>), returned as the offset that gives that
        /// full rate. No adjustment: the vanilla offset unchanged.
        /// </summary>
        public static int ViewOffset(int vanillaOffset, int vanillaFullRate, int meanAdjustment, int min, int max)
        {
            int rate = EffectiveTaxRate.Compute(vanillaFullRate, meanAdjustment, 0, min, max, 1);
            return vanillaOffset + (rate - vanillaFullRate);
        }

        /// <summary>
        /// Writes the view into <paramref name="view"/> (length <see cref="SlotCount"/>): a copy of
        /// <paramref name="vanilla"/> with every leaf slot that has payers (<paramref name="counts"/> &gt; 0) moved by its
        /// payers' mean adjustment (<paramref name="sums"/> / counts), clamped per <see cref="ViewOffset"/> to the
        /// job-level limits (residential slots) or the resource limits (the others). <paramref name="weightless"/>
        /// marks the industrial-range slots booked as Office. Returns whether any slot differs from vanilla.
        /// </summary>
        public static bool Compose(int[] vanilla, long[] sums, int[] counts, bool[] weightless,
            int jobLevelMin, int jobLevelMax, int resourceMin, int resourceMax, int[] view)
        {
            if (vanilla == null || view == null) throw new ArgumentNullException(vanilla == null ? nameof(vanilla) : nameof(view));
            int n = Math.Min(vanilla.Length, view.Length);
            bool changed = false;
            for (int slot = 0; slot < n; slot++)
            {
                int value = vanilla[slot];
                int count = counts != null && slot < counts.Length ? counts[slot] : 0;
                int area = AreaOf(slot, weightless != null && slot < weightless.Length && weightless[slot]);
                if (area != 0 && count > 0)
                {
                    int mean = MeanAdjustment(sums != null && slot < sums.Length ? sums[slot] : 0, count);
                    int full = vanilla[0] + vanilla[area] + value;
                    bool residential = area == AreaResidential;
                    value = ViewOffset(value, full, mean,
                        residential ? jobLevelMin : resourceMin, residential ? jobLevelMax : resourceMax);
                }
                if (value != vanilla[slot]) changed = true;
                view[slot] = value;
            }
            return changed;
        }
    }
}
