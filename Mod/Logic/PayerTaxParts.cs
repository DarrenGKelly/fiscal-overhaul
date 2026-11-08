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
        /// <summary>Its vanilla rate (<c>TaxPayer.m_AverageTaxRate</c>): what vanilla's own estimate charges it.</summary>
        public int VanillaRate;
        /// <summary>Its rate without density tax: vanilla rate + district difference, clamped.</summary>
        public int RateWithoutDensity;
        /// <summary>The rate the engine charges it: with its density tax too, clamped.</summary>
        public int ChargedRate;

        /// <summary>The income tax part of a month: base × rate without density / 100.</summary>
        public double IncomePart => Base * RateWithoutDensity / 100.0;

        /// <summary>
        /// The district part of a month: what its district's (or Outside's) income-tax overrides add to vanilla's tax,
        /// base × (without − vanilla) / 100; the city books it as the zone's tax income (vanilla's estimate leaves it out).
        /// </summary>
        public double DistrictPart => Base * (RateWithoutDensity - VanillaRate) / 100.0;

        /// <summary>The density tax part of a month (negative: a subsidy): base × (charged − without) / 100.</summary>
        public double DensityPart => Base * (ChargedRate - RateWithoutDensity) / 100.0;

        /// <summary>
        /// The parts of a payer charged <paramref name="vanillaRate"/> (<c>TaxPayer.m_AverageTaxRate</c>) plus
        /// <paramref name="incomeDelta"/> and <paramref name="densityTax"/> inside [<paramref name="min"/>,
        /// <paramref name="max"/>], whose last payment was charged at the rate it is charged now (nothing moved since).
        /// </summary>
        public static PayerTaxParts Of(long monthlyPaid, int untaxedIncome, int vanillaRate, int densityTax, int incomeDelta, int min, int max, int chargesPerMonth)
            => Of(monthlyPaid, untaxedIncome, vanillaRate, densityTax, incomeDelta, min, max, chargesPerMonth,
                EffectiveTaxRate.Compute(vanillaRate, densityTax, incomeDelta, min, max, 1));

        /// <summary>
        /// The parts of a payer charged <paramref name="vanillaRate"/> (<c>TaxPayer.m_AverageTaxRate</c>) plus
        /// <paramref name="incomeDelta"/> and <paramref name="densityTax"/> inside [<paramref name="min"/>,
        /// <paramref name="max"/>]. The base is what it paid a month (<c>m_AverageTaxPaid</c>) over
        /// <paramref name="paidRate"/>, the rate that payment was charged at (<see cref="PaymentRate"/>), so the parts
        /// follow a rate moved since the payment: base × the rates now. A payment charged at 0 says nothing of the base:
        /// then it is the untaxed income at the monthly scale (<paramref name="untaxedIncome"/> ×
        /// <paramref name="chargesPerMonth"/>), so a payer the density tax took to 0 still shows its subsidy. Base 0
        /// (nothing to book) otherwise.
        /// </summary>
        public static PayerTaxParts Of(long monthlyPaid, int untaxedIncome, int vanillaRate, int densityTax, int incomeDelta, int min, int max, int chargesPerMonth, int paidRate)
        {
            var p = new PayerTaxParts
            {
                VanillaRate = vanillaRate,
                RateWithoutDensity = EffectiveTaxRate.Compute(vanillaRate, 0, incomeDelta, min, max, 1),
                ChargedRate = EffectiveTaxRate.Compute(vanillaRate, densityTax, incomeDelta, min, max, 1),
            };
            if (paidRate > 0) p.Base = DensityTaxTable.TaxableBase(monthlyPaid, paidRate);
            else if (untaxedIncome > 0) p.Base = (double)untaxedIncome * chargesPerMonth;
            return p;
        }
    }
}
