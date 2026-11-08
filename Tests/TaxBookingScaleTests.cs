using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxBookingScaleTests
    {
        // A city of payers spread over TaxSystem's 16 update frames, each charged once per 1/32 month.
        private static readonly int[] Untaxed = { 1000, 2500, 400, 3200, 800, 1200, 600, 5000, 900, 1100, 700, 1500, 2000, 300, 450, 3000 };

        [Fact]
        public void AddSums_IsAddPerCharge()
        {
            var a = new TaxBookingLedger();
            var b = new TaxBookingLedger();
            a.Add(4, TaxZone.Commercial, 3, 5);
            a.Add(4, TaxZone.Commercial, -1, -2);
            b.AddSums(4, TaxZone.Commercial, 2, 5, -2, 0);
            a.Monthly(TaxZone.Commercial, out long ad, out long ap, out long an);
            b.Monthly(TaxZone.Commercial, out long bd, out long bp, out long bn);
            Assert.Equal((ad, ap, an), (bd, bp, bn));
            Assert.Equal(64, bd);
            Assert.NotEqual(2, bd); // monthly, not per charge
        }

        [Fact]
        public void VanillaSums_DontMakeTheLedgerBook()
        {
            var ledger = new TaxBookingLedger();
            ledger.AddSums(0, TaxZone.Residential, 0, 0, 0, 1234);
            Assert.True(ledger.IsEmpty);
            Assert.Equal(1234 * 32, ledger.VanillaMonthly(TaxZone.Residential));
            ledger.AddSums(0, TaxZone.Residential, 0, 1, 0, 0);
            Assert.False(ledger.IsEmpty);
        }

        [Fact]
        public void VanillaMonthly_IsVanillasEstimateScale()
        {
            // Vanilla's estimate: rate x the taxable-income statistic, which sums untaxed x kUpdatesPerDay (32) over one
            // statistics sample (8192 frames = the 16 update frames), i.e. once per payer.
            const int rate = 10;
            var ledger = new TaxBookingLedger();
            long statistic = 0;
            for (int f = 0; f < Untaxed.Length; f++)
            {
                ledger.AddSums(f, TaxZone.Residential, 0, 0, 0, DensityTaxBooking.Tax(rate, Untaxed[f]));
                statistic += Untaxed[f] * 32L;
            }
            long vanillaEstimate = rate * statistic / 100;
            Assert.Equal(vanillaEstimate, ledger.VanillaMonthly(TaxZone.Residential));
            Assert.NotEqual(vanillaEstimate / 2, ledger.VanillaMonthly(TaxZone.Residential));
        }

        [Fact]
        public void DensityBooked_IsTheTaxationEstimatesScale()
        {
            // Density +10 on a 10% zone. The Taxation tab estimates sum(paid x 100 / charged) x offset / 100 with paid =
            // m_AverageTaxPaid = the tax charged x 32; the ledger books sum(density part x 32).
            const int stored = 10, offset = 10, charged = stored + offset;
            var ledger = new TaxBookingLedger();
            double taxableBase = 0;
            for (int f = 0; f < Untaxed.Length; f++)
            {
                int vanillaTax = DensityTaxBooking.Tax(stored, Untaxed[f]);
                int chargedTax = DensityTaxBooking.Tax(charged, Untaxed[f]);
                DensityTaxBooking.Parts(vanillaTax, vanillaTax, chargedTax, out int district, out int density);
                ledger.AddSums(f, TaxZone.Residential, district, density, 0, vanillaTax);
                taxableBase += DensityTaxTable.TaxableBase(chargedTax * 32L, charged);
            }
            long estimate = DensityTaxTable.EstimateFromBase(taxableBase, offset);
            ledger.Monthly(TaxZone.Residential, out _, out long booked, out _);
            Assert.Equal(estimate, booked);
            Assert.NotEqual(estimate * 16, booked);
        }

        [Fact]
        public void Log_ReportsTheScaleCheck()
        {
            string line = TaxBookingLog.Zone("Residential", 248000, 0, 248000, 0, 7300, -200, 255300, 200);
            Assert.Contains("vanilla income 248000 subsidy 0", line);
            Assert.Contains("(scale 1.000)", line);
            Assert.Contains("density +7300 / -200", line);
            Assert.Contains("result income 255300 subsidy 200", line);
            string doubled = TaxBookingLog.Zone("Office", 200, 0, 100, 0, 0, 0, 200, 0);
            Assert.Contains("(scale 2.000)", doubled);
            Assert.DoesNotContain("scale 1.000", doubled);
            Assert.Contains("(scale n/a)", TaxBookingLog.Zone("Office", 0, 0, 0, 0, 0, 0, 0, 0));
        }
    }
}
