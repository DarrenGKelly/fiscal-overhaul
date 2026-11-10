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

        /// <summary>The income tax part of a month: the tax at the rate without density tax.</summary>
        public double IncomePart;

        /// <summary>
        /// The district part of a month: what its district's (or Outside's) income-tax overrides add to vanilla's tax,
        /// the tax without density − the tax at the vanilla rate; the city books it as the zone's tax income (vanilla's
        /// estimate leaves it out).
        /// </summary>
        public double DistrictPart;

        /// <summary>The density tax part of a month (negative: a subsidy): the tax at the charged rate − the tax without density.</summary>
        public double DensityPart;

        /// <summary>
        /// The parts of a payer charged <paramref name="vanillaRate"/> (<c>TaxPayer.m_AverageTaxRate</c>) plus
        /// <paramref name="incomeDelta"/> and <paramref name="densityTax"/> inside [<paramref name="min"/>,
        /// <paramref name="max"/>], from the untaxed income the tax engine last charged it
        /// (<see cref="ChargeWindow"/>: only a charge in the last 1/32 month counts), at the monthly scale
        /// (× <paramref name="chargesPerMonth"/>): the base the engine books from, so the parts are what the city books,
        /// restated at the rates now (a slider moved since the charge moves them at once). Each part is the engine's own
        /// per-charge tax (<see cref="DensityTaxBooking.Tax"/>, vanilla's rounded <c>TaxSystem.GetTax</c>) at each rate,
        /// split as <see cref="DensityTaxBooking.Parts"/> splits a charge, × <paramref name="chargesPerMonth"/>: what the
        /// booking sums, rounding included (base × rate reads 5-7% off on small incomes, issue #83). No income charged
        /// (none, a loss, or no recent charge): base 0, nothing to book.
        /// </summary>
        public static PayerTaxParts FromCharge(int chargedUntaxedIncome, int vanillaRate, int densityTax, int incomeDelta, int min, int max, int chargesPerMonth)
        {
            var p = Rates(vanillaRate, densityTax, incomeDelta, min, max);
            if (chargedUntaxedIncome <= 0) return p;
            p.Base = (double)chargedUntaxedIncome * chargesPerMonth;
            int vanillaTax = DensityTaxBooking.Tax(p.VanillaRate, chargedUntaxedIncome);
            int withoutTax = DensityTaxBooking.Tax(p.RateWithoutDensity, chargedUntaxedIncome);
            int chargedTax = DensityTaxBooking.Tax(p.ChargedRate, chargedUntaxedIncome);
            DensityTaxBooking.Parts(vanillaTax, withoutTax, chargedTax, out int district, out int density);
            p.IncomePart = (double)withoutTax * chargesPerMonth;
            p.DistrictPart = (double)district * chargesPerMonth;
            p.DensityPart = (double)density * chargesPerMonth;
            return p;
        }

        /// <summary>
        /// The parts of a payer from what it paid a month (<c>m_AverageTaxPaid</c>) at the rate it is charged now: base =
        /// paid / that rate (or, at a 0 rate, the untaxed income × <paramref name="chargesPerMonth"/>). The estimates
        /// use <see cref="FromCharge"/>: a paid amount stays on a payer the engine no longer charges.
        /// </summary>
        public static PayerTaxParts Of(long monthlyPaid, int untaxedIncome, int vanillaRate, int densityTax, int incomeDelta, int min, int max, int chargesPerMonth)
        {
            var p = Rates(vanillaRate, densityTax, incomeDelta, min, max);
            if (p.ChargedRate > 0) p.Base = DensityTaxTable.TaxableBase(monthlyPaid, p.ChargedRate);
            else if (untaxedIncome > 0) p.Base = (double)untaxedIncome * chargesPerMonth;
            p.IncomePart = p.Base * p.RateWithoutDensity / 100.0;
            p.DistrictPart = p.Base * (p.RateWithoutDensity - p.VanillaRate) / 100.0;
            p.DensityPart = p.Base * (p.ChargedRate - p.RateWithoutDensity) / 100.0;
            return p;
        }

        private static PayerTaxParts Rates(int vanillaRate, int densityTax, int incomeDelta, int min, int max) => new PayerTaxParts
        {
            VanillaRate = vanillaRate,
            RateWithoutDensity = EffectiveTaxRate.Compute(vanillaRate, 0, incomeDelta, min, max, 1),
            ChargedRate = EffectiveTaxRate.Compute(vanillaRate, densityTax, incomeDelta, min, max, 1),
        };
    }
}
