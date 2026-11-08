namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Whether a payer's last recorded charge (the tax engine's per-payer charge record: the untaxed income charged and
    /// the engine tick) is in the last 1/32 month. The engine charges every payer with income once per
    /// <see cref="TaxBookingLedger.Frames"/> ticks; a payer with no untaxed income at its charge is skipped (as vanilla)
    /// and its <c>m_AverageTaxPaid</c> stays at its last payment for good, so only a recent charge says what the city is
    /// booked now. Burst-safe.
    /// </summary>
    public static class ChargeWindow
    {
        /// <summary>
        /// True when <paramref name="recordTick"/> is within the last <see cref="TaxBookingLedger.Frames"/> engine
        /// ticks before <paramref name="nowTick"/> (the last tick that charged), i.e. the payer's group was charged and
        /// this payer had income.
        /// </summary>
        public static bool IsCurrent(int recordTick, int nowTick)
        {
            int age = nowTick - recordTick;
            return age >= 0 && age < TaxBookingLedger.Frames;
        }

        /// <summary>A record this old can be dropped: it will never be current again.</summary>
        public static bool IsExpired(int recordTick, int nowTick) => nowTick - recordTick >= 2 * TaxBookingLedger.Frames;
    }
}
