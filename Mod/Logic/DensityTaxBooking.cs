using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// How one tax charge splits into its income-tax part (the payer's rate without density tax: the zone, education or
    /// resource rate, with its district's difference) and its density-tax part, and how the two book against the city:
    /// a positive part is tax income, a negative part a subsidy expense (vanilla's SubsidyResidential/Commercial/
    /// Industrial/Office), each part on its own rather than netted. Pure int maths, Burst-safe.
    ///
    /// Vanilla books per tax item (an education level or a resource): an item whose rate is below 0 is a subsidy
    /// (CityServiceBudgetSystem: <c>-TaxSystem.GetEstimatedTaxAmount(area, TaxResultType.Expense, …)</c>), an item
    /// above 0 is tax income, and the two never net. Treating density tax as its own item keeps that rule.
    /// income − subsidy is always the whole charge, so the booking never moves the city's balance.
    /// </summary>
    public static class DensityTaxBooking
    {
        /// <summary>Vanilla's <c>TaxSystem.GetTax</c>: <c>round(0.01 * rate * untaxedIncome)</c>, in float as vanilla.</summary>
        public static int Tax(int rate, int untaxedIncome)
            => (int)(float)Math.Round((double)(0.01f * rate * untaxedIncome));

        /// <summary>
        /// One charge at <paramref name="chargedRate"/> (what the payer pays, density included) split into the
        /// income-tax part, the tax at <paramref name="rateWithoutDensity"/>, and the density part, the rest. The two
        /// always add up to the charge, so the payer's money is unchanged whatever the split.
        /// </summary>
        public static void Split(int untaxedIncome, int chargedRate, int rateWithoutDensity, out int incomeTax, out int densityTax)
        {
            int total = Tax(chargedRate, untaxedIncome);
            incomeTax = Tax(rateWithoutDensity, untaxedIncome);
            densityTax = total - incomeTax;
        }

        /// <summary>
        /// Books the two parts: <paramref name="income"/> is the zone's tax income (the positive parts),
        /// <paramref name="subsidy"/> its subsidy expense as a positive amount (the negative parts, as vanilla's expense
        /// array holds it). <c>income − subsidy == incomeTax + densityTax</c>.
        /// </summary>
        public static void Book(int incomeTax, int densityTax, out int income, out int subsidy)
        {
            income = Math.Max(incomeTax, 0) + Math.Max(densityTax, 0);
            subsidy = Math.Max(-incomeTax, 0) + Math.Max(-densityTax, 0);
        }
    }
}
