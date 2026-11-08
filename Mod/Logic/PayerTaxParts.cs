namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// One payer's monthly tax split the way the city books it (the tax engine's booking: the income part at the rate
    /// without density tax, the density part = (charged rate − rate without density) × base, a negative one the zone's
    /// subsidy), for the Taxation tab's estimates. Both rates are <see cref="EffectiveTaxRate.Compute"/>'s, so the
    /// density part is clamped as the charge is. Pure maths, Burst-safe: the estimate jobs build one per payer.
    /// </summary>
    public struct PayerTaxParts
    {
        /// <summary>The payer's monthly taxable base.</summary>
        public double Base;
        /// <summary>Its rate without density tax: vanilla rate + district difference, clamped.</summary>
        public int RateWithoutDensity;
        /// <summary>The rate the engine charges it: with its density tax too, clamped.</summary>
        public int ChargedRate;

        /// <summary>The income tax part of a month: base × rate without density / 100.</summary>
        public double IncomePart => Base * RateWithoutDensity / 100.0;

        /// <summary>The density tax part of a month (negative: a subsidy): base × (charged − without) / 100.</summary>
        public double DensityPart => Base * (ChargedRate - RateWithoutDensity) / 100.0;

        /// <summary>
        /// The parts of a payer charged <paramref name="vanillaRate"/> (<c>TaxPayer.m_AverageTaxRate</c>) plus
        /// <paramref name="incomeDelta"/> and <paramref name="densityTax"/> inside [<paramref name="min"/>,
        /// <paramref name="max"/>]. The base is what it paid a month (<c>m_AverageTaxPaid</c>) over the charged rate;
        /// when nothing could be paid because the density tax took the rate to 0, the base is its untaxed income at
        /// the monthly scale (<paramref name="untaxedIncome"/> × <paramref name="chargesPerMonth"/>), so the subsidy
        /// still shows. Base 0 (nothing to book) otherwise.
        /// </summary>
        public static PayerTaxParts Of(long monthlyPaid, int untaxedIncome, int vanillaRate, int densityTax, int incomeDelta, int min, int max, int chargesPerMonth)
        {
            var p = new PayerTaxParts
            {
                RateWithoutDensity = EffectiveTaxRate.Compute(vanillaRate, 0, incomeDelta, min, max, 1),
                ChargedRate = EffectiveTaxRate.Compute(vanillaRate, densityTax, incomeDelta, min, max, 1),
            };
            if (p.ChargedRate > 0) p.Base = DensityTaxTable.TaxableBase(monthlyPaid, p.ChargedRate);
            else if (p.RateWithoutDensity > p.ChargedRate && untaxedIncome > 0) p.Base = (double)untaxedIncome * chargesPerMonth;
            return p;
        }
    }
}
