using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// A fingerprint of everything the tax engine's rates come from: vanilla's 92 city rates, the density switch and the
    /// loaded city's rate table (city density rates and every district override). Equal fingerprints charge every payer
    /// the same rate, so a booking window whose frames all carry the current one is settled
    /// (<see cref="TaxBookingLedger.Settled"/>). Order-independent over the table's entries.
    /// </summary>
    public static class RateSignature
    {
        public static long Of(IEnumerable<TaxRateEntry> entries, IReadOnlyList<int> cityRates, bool densityOn)
        {
            unchecked
            {
                long h = densityOn ? 1469598103934665603L : 1099511628211L;
                if (cityRates != null)
                    for (int i = 0; i < cityRates.Count; i++) h = (h ^ cityRates[i]) * 1099511628211L + i;
                long sum = 0;
                if (entries != null)
                    foreach (TaxRateEntry e in entries)
                    {
                        long x = (((((long)e.Type * 1000003 + e.Zone) * 1000003 + e.Key) * 1000003 + e.Area) * 1000003 + e.AreaVersion) * 1000003 + e.Value;
                        x ^= x >> 29;
                        x *= -7046029254386353131L;
                        x ^= x >> 32;
                        sum += x;
                    }
                return h ^ sum;
            }
        }
    }
}
