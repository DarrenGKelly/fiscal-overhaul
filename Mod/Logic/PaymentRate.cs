namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The rate a payer's last payment (<c>TaxPayer.m_AverageTaxPaid</c>) was charged at, for the Taxation tab's
    /// estimates. The engine doesn't store it, and a payer pays again only every 1/32 month, so dividing the paid amount
    /// by the rate charged <em>now</em> pins every estimate to what was paid until the next payment: a slider moved
    /// since moves nothing. Each estimate system remembers, per payer, the paid amount it last saw and the rate charged
    /// then. While the paid amount is unchanged it is the same payment, at the remembered rate; a new amount is a new
    /// payment, at the rate charged now. Rates change only from the Economy panel, whose systems refresh while it is
    /// open, so a first sighting is taken at the current rate. Burst-safe.
    /// </summary>
    public static class PaymentRate
    {
        /// <summary>
        /// The rate <paramref name="paid"/> was charged at: the remembered <paramref name="knownRate"/> while the payer
        /// still shows the remembered <paramref name="knownPaid"/> (<paramref name="known"/>), else
        /// <paramref name="chargedNow"/>. The caller remembers (<paramref name="paid"/>, the result) for its next refresh.
        /// </summary>
        public static int Of(bool known, long knownPaid, int knownRate, long paid, int chargedNow)
            => known && knownPaid == paid ? knownRate : chargedNow;
    }
}
