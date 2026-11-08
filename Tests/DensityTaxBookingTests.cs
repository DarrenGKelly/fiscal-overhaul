using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DensityTaxBookingTests
    {
        private const int Income = 1000;

        // One payer's charge: vanilla rate, rate without density, charged rate.
        private static void Charge(int vanillaRate, int withoutDensity, int charged, out int district, out int density)
            => DensityTaxBooking.Parts(DensityTaxBooking.Tax(vanillaRate, Income), DensityTaxBooking.Tax(withoutDensity, Income),
                DensityTaxBooking.Tax(charged, Income), out district, out density);

        [Fact]
        public void Parts_AddUpToTheCharge()
        {
            Charge(10, 12, 15, out int district, out int density);
            Assert.Equal(20, district);
            Assert.Equal(30, density);
            Assert.Equal(DensityTaxBooking.Tax(15, Income), DensityTaxBooking.Tax(10, Income) + district + density);
            Assert.NotEqual(50, density); // the district part is not counted as density
        }

        [Fact]
        public void Monthly_IsThirtyTwoCharges()
        {
            Assert.Equal(3200, DensityTaxBooking.Monthly(100));
            Assert.Equal(-320, DensityTaxBooking.Monthly(-10));
            Assert.NotEqual(100, DensityTaxBooking.Monthly(100));
        }

        [Fact]
        public void PositiveDensity_RaisesTaxIncomeByItsAmount_NoSubsidy()
        {
            DensityTaxBooking.BookZone(5000, 0, 0, 1300, 0, out int income, out int subsidy);
            Assert.Equal(6300, income);
            Assert.Equal(0, subsidy);
            Assert.NotEqual(5000, income);
        }

        [Fact]
        public void NegativeDensity_IsASubsidy_NotNetted()
        {
            DensityTaxBooking.BookZone(5000, 0, 0, 0, -400, out int income, out int subsidy);
            Assert.Equal(5000, income);
            Assert.Equal(400, subsidy);
            Assert.NotEqual(4600, income); // not netted off the tax income
        }

        [Fact]
        public void BothSignsOfDensity_BookSeparately()
        {
            DensityTaxBooking.BookZone(5000, 100, 0, 300, -200, out int income, out int subsidy);
            Assert.Equal(5300, income);
            Assert.Equal(300, subsidy);
            Assert.NotEqual(5100, income);
        }

        [Fact]
        public void DistrictPart_MovesTaxIncome()
        {
            DensityTaxBooking.BookZone(5000, 0, -700, 0, 0, out int income, out int subsidy);
            Assert.Equal(4300, income);
            Assert.Equal(0, subsidy); // a lower district rate is less tax, not a subsidy
            DensityTaxBooking.BookZone(5000, 0, 900, 0, 0, out income, out _);
            Assert.Equal(5900, income);
            // Below 0, the rest is subsidy.
            DensityTaxBooking.BookZone(500, 0, -700, 0, 0, out income, out subsidy);
            Assert.Equal(0, income);
            Assert.Equal(200, subsidy);
        }

        [Theory]
        [InlineData(5000, 0)]
        [InlineData(0, 250)]
        [InlineData(1234, 99)]
        public void AllZero_IsVanilla(int vanillaIncome, int vanillaSubsidy)
        {
            DensityTaxBooking.BookZone(vanillaIncome, vanillaSubsidy, 0, 0, 0, out int income, out int subsidy);
            Assert.Equal(vanillaIncome, income);
            Assert.Equal(vanillaSubsidy, subsidy);
            DensityTaxBooking.BookZone(vanillaIncome, vanillaSubsidy, 0, 1, 0, out income, out _);
            Assert.NotEqual(vanillaIncome, income);
        }

        [Theory]
        [InlineData(10, 10, 13)]
        [InlineData(10, 10, 6)]
        [InlineData(10, 14, 20)]
        [InlineData(10, 8, 3)]
        public void Balance_IsWhatThePayerIsCharged(int vanillaRate, int withoutDensity, int charged)
        {
            // City: vanilla's income for this payer plus the booking; the balance is exactly what it was charged.
            int vanilla = DensityTaxBooking.Tax(vanillaRate, Income);
            Charge(vanillaRate, withoutDensity, charged, out int district, out int density);
            DensityTaxBooking.BookZone(vanilla, 0, district, density > 0 ? density : 0, density < 0 ? density : 0, out int income, out int subsidy);
            Assert.Equal(DensityTaxBooking.Tax(charged, Income), income - subsidy);
            Assert.NotEqual(vanilla, income - subsidy);
        }

        [Fact]
        public void Tax_RoundsAsVanilla()
        {
            Assert.Equal(93, DensityTaxBooking.Tax(12, 777)); // 93.24
            Assert.Equal(-16, DensityTaxBooking.Tax(-2, 777)); // -15.54
            Assert.NotEqual(93, DensityTaxBooking.Tax(13, 777));
        }
    }

    public class TaxBookingLedgerTests
    {
        [Fact]
        public void Empty_BooksNothing()
        {
            var ledger = new TaxBookingLedger();
            Assert.True(ledger.IsEmpty);
            ledger.Add(0, TaxZone.Residential, 0, 0);
            Assert.True(ledger.IsEmpty); // all-zero charges stay vanilla
            ledger.Add(0, TaxZone.Residential, 0, 1);
            Assert.False(ledger.IsEmpty);
        }

        [Fact]
        public void Accumulates_PerZone_AtTheMonthlyScale()
        {
            var ledger = new TaxBookingLedger();
            ledger.Add(3, TaxZone.Residential, 5, 10);
            ledger.Add(7, TaxZone.Residential, 0, 20);
            ledger.Add(7, TaxZone.Office, 0, -4);
            ledger.Monthly(TaxZone.Residential, out long district, out long pos, out long neg);
            Assert.Equal(160, district);
            Assert.Equal(960, pos);
            Assert.Equal(0, neg);
            ledger.Monthly(TaxZone.Office, out _, out pos, out neg);
            Assert.Equal(0, pos);
            Assert.Equal(-128, neg);
            ledger.Monthly(TaxZone.Commercial, out district, out pos, out neg);
            Assert.Equal(0, district + pos + neg);
            ledger.Monthly(TaxZone.Residential, out _, out pos, out _);
            Assert.NotEqual(30, pos); // the monthly scale, not the per-charge one
        }

        [Fact]
        public void BeginFrame_ReplacesOnlyThatFrame()
        {
            var ledger = new TaxBookingLedger();
            ledger.Add(2, TaxZone.Commercial, 0, 10);
            ledger.Add(5, TaxZone.Commercial, 0, 1);
            ledger.BeginFrame(2);
            ledger.Add(2, TaxZone.Commercial, 0, 3);
            ledger.Monthly(TaxZone.Commercial, out _, out long pos, out _);
            Assert.Equal((3 + 1) * 32, pos);
            Assert.NotEqual((10 + 3 + 1) * 32, pos);
            // Frames wrap at 16.
            ledger.BeginFrame(18);
            ledger.Monthly(TaxZone.Commercial, out _, out pos, out _);
            Assert.Equal(32, pos);
        }

        [Fact]
        public void Clear_ForgetsEverything()
        {
            var ledger = new TaxBookingLedger();
            ledger.Add(1, TaxZone.Industrial, -3, -2);
            Assert.False(ledger.IsEmpty);
            ledger.Clear();
            Assert.True(ledger.IsEmpty);
        }

        [Fact]
        public void DensityTenOnOneZone_RaisesItsIncomeByTheChargedAmount()
        {
            // 16 frames of payers at vanilla 10%, density +10 (charged 20%), 1000 untaxed each.
            var ledger = new TaxBookingLedger();
            for (int f = 0; f < TaxBookingLedger.Frames; f++)
            {
                DensityTaxBooking.Parts(DensityTaxBooking.Tax(10, 1000), DensityTaxBooking.Tax(10, 1000), DensityTaxBooking.Tax(20, 1000), out int d, out int dens);
                ledger.Add(f, TaxZone.Residential, d, dens);
            }
            ledger.Monthly(TaxZone.Residential, out long district, out long pos, out long neg);
            int vanillaIncome = 16 * 100 * 32;
            DensityTaxBooking.BookZone(vanillaIncome, 0, district, pos, neg, out int income, out int subsidy);
            Assert.Equal(16 * 200 * 32, income);
            Assert.Equal(0, subsidy);
            Assert.NotEqual(vanillaIncome, income);
        }
    }
}
