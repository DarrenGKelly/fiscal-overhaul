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
        public void Settled_OnlyWhenEveryFrameWasChargedAtTheCurrentRates()
        {
            var ledger = new TaxBookingLedger();
            Assert.False(ledger.Settled(1)); // nothing charged yet (after a load)
            for (int f = 0; f < TaxBookingLedger.Frames; f++) ledger.BeginFrame(f, 1);
            Assert.True(ledger.Settled(1));
            // A rate moves: the next frames are charged at signature 2; the window mixes until all 16 have been.
            for (int f = 0; f < TaxBookingLedger.Frames - 1; f++)
            {
                ledger.BeginFrame(f, 2);
                Assert.False(ledger.Settled(2));
                Assert.False(ledger.Settled(1));
            }
            ledger.BeginFrame(TaxBookingLedger.Frames - 1, 2);
            Assert.True(ledger.Settled(2));
            ledger.Clear();
            Assert.False(ledger.Settled(2));
        }

        [Fact]
        public void Cells_HoldTheirTaxableIncomeAndDensity()
        {
            var ledger = new TaxBookingLedger();
            int high = DensityTaxTable.Index(TaxZone.Residential, Density.High);
            ledger.AddCell(0, high, 1000, 100);
            ledger.AddCell(1, high, 500, 50);
            ledger.AddCell(1, -1, 999, 9); // no cell: ignored
            ledger.CellMonthly(high, out long taxable, out long density);
            Assert.Equal(1500 * 32, taxable);
            Assert.Equal(150 * 32, density);
            ledger.CellMonthly(DensityTaxTable.Index(TaxZone.Residential, Density.Low), out taxable, out _);
            Assert.Equal(0, taxable);
            ledger.BeginFrame(0, 5);
            ledger.CellMonthly(high, out taxable, out _);
            Assert.Equal(500 * 32, taxable);
            Assert.NotEqual(1500 * 32, taxable);
        }

        [Fact]
        public void RateSignature_ChangesWithAnyRate_NotWithOrder()
        {
            var t = new TaxRateTable();
            t.Set(TaxType.Density, 0, 2, 10);
            t.Set(TaxType.ZoneRate, 0, 0, 12, 1, 15);
            int[] city = new int[92];
            long a = RateSignature.Of(t.Entries(), city, true);
            var reversed = t.Entries();
            reversed.Reverse();
            Assert.Equal(a, RateSignature.Of(reversed, city, true));
            Assert.NotEqual(a, RateSignature.Of(t.Entries(), city, false));
            city[3] = 1;
            Assert.NotEqual(a, RateSignature.Of(t.Entries(), city, true));
            city[3] = 0;
            t.Set(TaxType.Density, 0, 2, -5);
            Assert.NotEqual(a, RateSignature.Of(t.Entries(), city, true));
        }

        [Fact]
        public void Log_HeadCellsAndOverrides()
        {
            Assert.Equal("TaxBooking (monthly, settled):", TaxBookingLog.Head(true));
            Assert.Contains("NOT settled", TaxBookingLog.Head(false));
            Assert.Equal("Residential High at +10: taxable 272960, density booked +27296 (base x rate +27296)",
                TaxBookingLog.Cell("Residential High", 10, 272960, 27296));
            var t = new TaxRateTable();
            t.Set(TaxType.Density, 0, 2, 10);              // city density: not listed
            t.Set(TaxType.Density, 0, 2, 12, 1, 3);        // district density: not listed
            Assert.Equal("none", TaxBookingLog.Overrides(t.Entries()));
            t.Set(TaxType.ResourceRate, 0, 4, 12, 1, 5);   // a district's Highly Educated offset
            Assert.Equal("ResourceRate/0/4@12:1=5", TaxBookingLog.Overrides(t.Entries()));
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
