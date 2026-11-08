using System.Globalization;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The Mod.log line TaxBookingSystem writes once per 1/32 month while the engine charges: per zone, vanilla's
    /// estimate, the engine's own measure of it (its charges at the payers' vanilla rates, on the ledger's monthly scale),
    /// the parts booked on top and the result. The scale check is <c>scale</c> = vanilla net / the engine's measure: about
    /// 1 when the ledger's ×32 per charge is vanilla's scale, as it should be.
    /// </summary>
    public static class TaxBookingLog
    {
        public static string Zone(string name, int vanillaIncome, int vanillaSubsidy, long engineAtVanilla,
            long district, long densityPositive, long densityNegative, int income, int subsidy)
        {
            long vanillaNet = (long)vanillaIncome - vanillaSubsidy;
            string scale = engineAtVanilla == 0 ? "n/a" : ((double)vanillaNet / engineAtVanilla).ToString("0.000", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture,
                "{0}: vanilla income {1} subsidy {2}, engine at vanilla rates {3} (scale {4}); booked district {5:+0;-0;0}, density {6:+0;-0;0} / {7:+0;-0;0}; result income {8} subsidy {9}",
                name, vanillaIncome, vanillaSubsidy, engineAtVanilla, scale, district, densityPositive, densityNegative, income, subsidy);
        }

        /// <summary>The line's head: whether every charge in the window was at the current rates (compare only then).</summary>
        public static string Head(bool settled) => settled ? "TaxBooking (monthly, settled):" : "TaxBooking (monthly, NOT settled: the window mixes old and new rates):";

        /// <summary>
        /// A density cell: its charged payers' monthly taxable income, the density part booked, and what its rate makes
        /// of that base (base × rate / 100: the Taxation row's estimate should be about this, clamps aside).
        /// </summary>
        public static string Cell(string name, int rate, long taxableIncome, long density)
            => string.Format(CultureInfo.InvariantCulture, "{0} at {1:+0;-0;0}: taxable {2}, density booked {3:+0;-0;0} (base x rate {4:+0;-0;0})",
                name, rate, taxableIncome, density, taxableIncome * rate / 100);

        /// <summary>The district overrides other than density (zone and education/resource rates), or "none".</summary>
        public static string Overrides(System.Collections.Generic.IEnumerable<TaxRateEntry> entries)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (entries != null)
                foreach (TaxRateEntry e in entries)
                    if (!e.IsCity && e.Type != TaxType.Density) parts.Add(e.ToString());
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}
