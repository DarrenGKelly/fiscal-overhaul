using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// How the city books what the tax engine charges beyond vanilla. Pure int maths, Burst-safe.
    ///
    /// Vanilla pays the city its tax from CityServiceBudgetSystem's estimate (the city's rate per tax item × that item's
    /// taxable-income statistic), not from the charge: an item above 0 is the zone's tax income, an item below 0 the
    /// zone's subsidy expense, never netted. The engine charges each payer more or less than that (its district's
    /// rates, its density tax), so the city is booked the difference on top of vanilla's numbers, split per charge into
    /// the <b>district part</b> (the tax at the rate without density tax − the tax at the payer's vanilla rate) and the
    /// <b>density part</b> (the tax at the charged rate − the tax without density). The district part changes the
    /// zone's tax income; a positive density part is tax income and a negative one the zone's subsidy, as vanilla books
    /// a negative item. With both parts 0 the booking is vanilla's own numbers.
    /// </summary>
    public static class DensityTaxBooking
    {
        /// <summary>Charges per payer per in-game month: vanilla <c>TaxSystem.kUpdatesPerDay</c> (a CS2 "day" is a month).</summary>
        public const int ChargesPerMonth = 32;

        /// <summary>Vanilla's <c>TaxSystem.GetTax</c>: <c>round(0.01 * rate * untaxedIncome)</c>, in float as vanilla.</summary>
        public static int Tax(int rate, int untaxedIncome)
            => (int)(float)Math.Round((double)(0.01f * rate * untaxedIncome));

        /// <summary>
        /// One charge's two parts beyond vanilla, from the tax at the payer's vanilla rate, at its rate without density
        /// tax and at its charged rate (each <c>TaxSystem.GetTax</c>). vanilla + district + density is always the charge.
        /// </summary>
        public static void Parts(int vanillaTax, int taxWithoutDensity, int chargedTax, out int district, out int density)
        {
            district = taxWithoutDensity - vanillaTax;
            density = chargedTax - taxWithoutDensity;
        }

        /// <summary>One charge's amount at the monthly scale vanilla's budget uses (as <c>TaxPayer.m_AverageTaxPaid</c>).</summary>
        public static long Monthly(int perCharge) => (long)perCharge * ChargesPerMonth;

        /// <summary>
        /// One zone's booking: vanilla's tax income and subsidy (a positive expense, as vanilla's expense array holds it)
        /// plus the engine's monthly district part and its positive and negative density parts (each a sum of charges of
        /// that sign: <paramref name="densityNegative"/> is ≤ 0, never netted against the positive ones). The district
        /// part moves the tax income; should it take the income below 0, the rest is subsidy. A positive density part
        /// adds to the income, a negative one to the subsidy. All parts 0: vanilla's numbers, unchanged.
        /// </summary>
        public static void BookZone(int vanillaIncome, int vanillaSubsidy, long district, long densityPositive, long densityNegative,
            out int income, out int subsidy)
        {
            long inc = vanillaIncome + district;
            long sub = vanillaSubsidy;
            if (inc < 0)
            {
                sub -= inc;
                inc = 0;
            }
            inc += Math.Max(densityPositive, 0);
            sub += Math.Max(-densityNegative, 0);
            income = Clamp(inc);
            subsidy = Clamp(sub);
        }

        private static int Clamp(long v) => v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }
}
