namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The rate the tax engine charges a payer: the payer's own vanilla rate (<c>TaxPayer.m_AverageTaxRate</c>) plus the
    /// city's adjustments for it (density, district), kept inside the vanilla limits. Pure int maths, Burst-safe: the
    /// engine's job calls it per payer.
    /// </summary>
    public static class EffectiveTaxRate
    {
        /// <summary>
        /// <paramref name="baseRate"/> + <paramref name="densityAdjustment"/> + <paramref name="districtAdjustment"/>,
        /// clamped to [<paramref name="min"/>, <paramref name="max"/>] (the vanilla limits for the payer's kind:
        /// TaxParameterData's job-level limits for households, resource limits for companies) and never below 0 (a
        /// negative rate would pay the payer).
        ///
        /// The clamp never moves a rate past where vanilla already put it: with no adjustment, or a payer whose vanilla
        /// rate is already outside the limits, the vanilla rate stands (so all-zero adjustments are exactly vanilla).
        /// Zero or negative untaxed income (a loss, which vanilla refunds at its rate) is charged at the vanilla rate:
        /// adjustments tax income, not refunds.
        /// </summary>
        public static int Compute(int baseRate, int densityAdjustment, int districtAdjustment, int min, int max, int untaxedIncome)
        {
            int adjustment = densityAdjustment + districtAdjustment;
            if (adjustment == 0 || untaxedIncome <= 0) return baseRate;

            int lo = min < 0 ? 0 : min;
            int hi = max;
            // Never past the vanilla rate: a base outside the limits only moves back towards them.
            if (baseRate < lo) lo = baseRate;
            if (baseRate > hi) hi = baseRate;

            int rate = baseRate + adjustment;
            if (rate < lo) rate = lo;
            if (rate > hi) rate = hi;
            return rate;
        }
    }
}
