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
    }
}
