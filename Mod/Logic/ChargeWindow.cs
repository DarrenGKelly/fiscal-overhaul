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

        /// <summary>
        /// What a sum over the current records is multiplied by to stand for the whole 1/32 month: the engine counts its
        /// ticks from 1 after a load (or after it starts charging), so for the first <see cref="TaxBookingLedger.Frames"/>
        /// ticks only that many of the update-frame groups have been charged. Each group is an even share of the payers,
        /// so the sum scaled by Frames / ticks estimates the whole (issue #92: a district's total read ~29% after 30 s at
        /// 1x). 1 once a full window has run; 0 before the first tick (nothing charged yet).
        /// </summary>
        public static double WarmUpScale(int nowTick)
        {
            if (nowTick <= 0) return 0;
            return nowTick >= TaxBookingLedger.Frames ? 1 : (double)TaxBookingLedger.Frames / nowTick;
        }

        /// <summary>A record this old can be dropped: it will never be current again.</summary>
        public static bool IsExpired(int recordTick, int nowTick) => nowTick - recordTick >= 2 * TaxBookingLedger.Frames;
    }
}
