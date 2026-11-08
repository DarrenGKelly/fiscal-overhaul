using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>The Taxation tab's estimates booked as charged: <see cref="PayerTaxParts"/> and <see cref="DistrictTaxEstimate"/>.</summary>
    public class DistrictTaxEstimateTests
    {
        private const int Min = 0, Max = 30, Charges = 32;

        private static double[] Items() => new double[TaxRateModel.BlockSize];
        private static double[] Cells() => new double[TaxRateModel.DensityCells];

        private static int ResLow => DensityTaxTable.Index(TaxZone.Residential, Density.Low);
        private static int ComHigh => DensityTaxTable.Index(TaxZone.Commercial, Density.High);

        /// <summary>A payer that paid <paramref name="paid"/> a month at its vanilla rate plus the adjustments.</summary>
        private static PayerTaxParts Payer(long paid, int vanilla, int density, int delta, int untaxed = 0)
            => PayerTaxParts.Of(paid, untaxed, vanilla, density, delta, Min, Max, Charges);

        [Fact]
        public void Parts_SplitTheChargeIntoIncomeAndDensity()
        {
            // 120 paid at 10% + 2% density: base 1000, income part 100, density part 20.
            PayerTaxParts p = Payer(120, 10, 2, 0);
            Assert.Equal(1000, p.Base, 6);
            Assert.Equal(100, p.IncomePart, 6);
            Assert.Equal(20, p.DensityPart, 6);
            Assert.Equal(120, p.IncomePart + p.DensityPart, 6);
            Assert.NotEqual(120, p.IncomePart, 6);
        }

        [Fact]
        public void Parts_ClampTheDensityPartAsTheChargeDoes()
        {
            // At 28% a +5 density tax can only add 2 before the 30% limit: base 1000, density part 20, not 50.
            PayerTaxParts p = Payer(300, 28, 5, 0);
            Assert.Equal(30, p.ChargedRate);
            Assert.Equal(1000, p.Base, 6);
            Assert.Equal(20, p.DensityPart, 6);
            Assert.NotEqual(50, p.DensityPart, 6);
        }

        [Fact]
        public void Parts_CountAPayerTheDensityTaxTookToZero()
        {
            // 3% − 5% density: charged 0, paid nothing; the subsidy is its untaxed income × 32 × −3%.
            PayerTaxParts p = Payer(0, 3, -5, 0, untaxed: 100);
            Assert.Equal(0, p.ChargedRate);
            Assert.Equal(3200, p.Base, 6);
            Assert.Equal(-96, p.DensityPart, 6);
            // Paid nothing at a rate above 0 (no income): nothing to book.
            Assert.Equal(0, Payer(0, 10, 2, 0, untaxed: 100).Base, 6);
        }

        [Fact]
        public void ACell_BooksEachPayerAtItsOwnRate_AndClamped()
        {
            // Two shops in Commercial High: one under a district density override of 4 (the city's is 2), paying 140 at
            // 10% + 4%; one at 28% + 5%, clamped to 30%, paying 300.
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddCompany(items, cells, Payer(140, 10, 4, 0), ComHigh, TaxZone.Commercial, 3);
            DistrictTaxEstimate.AddCompany(items, cells, Payer(300, 28, 5, 0), ComHigh, TaxZone.Commercial, 4);
            DistrictTaxEstimates e = DistrictTaxEstimate.Compute(items, cells);
            // 40 (1000 × 4%) + 20 (1000 × the clamped 2%).
            Assert.Equal(60, e.Density[ComHigh]);
            // The city's rate on every payer's base would have said 2% × 2000 = 40; the unclamped +5, 90.
            Assert.NotEqual(40, e.Density[ComHigh]);
            Assert.NotEqual(90, e.Density[ComHigh]);
            Assert.Equal(100, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.Equal(280, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 4)]);
            Assert.Equal(440, e.Total);
        }

        [Fact]
        public void NoOverrides_MatchWhatTheBuildingsPay_AndTheRowsSumToTheTotal()
        {
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddHousehold(items, cells, Payer(120, 10, 2, 0), ResLow, new[] { 2 }, null);
            DistrictTaxEstimate.AddCompany(items, cells, Payer(50, 10, 0, 0), ComHigh, TaxZone.Commercial, 3);
            DistrictTaxEstimates e = DistrictTaxEstimate.Compute(items, cells);
            Assert.Equal(100, e.Items[TaxRateModel.DeltaIndex(TaxZone.Residential, 2)]);
            Assert.Equal(20, e.Density[ResLow]);
            Assert.Equal(50, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.Equal(120, e.Zones[(int)TaxZone.Residential]);
            Assert.Equal(170, e.Total);
            Assert.Equal(e.Items.Sum() + e.Density.Sum(), e.Total);
            Assert.Equal(e.Zones.Sum(), e.Total);
            Assert.Equal(0, e.Items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)]);
        }

        [Fact]
        public void ADistrictOverride_Counts()
        {
            // The district's Commercial rate 15 (+5 on the city's 10): the shop paid 75 at 15%.
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddCompany(items, cells, Payer(75, 10, 0, 5), ComHigh, TaxZone.Commercial, 3);
            DistrictTaxEstimates e = DistrictTaxEstimate.Compute(items, cells);
            Assert.Equal(75, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.NotEqual(50, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.Equal(0, e.Density[ComHigh]);
        }

        [Fact]
        public void Household_IsSplitOverItsWorkersLevelsByWage()
        {
            double[] items = Items(), cells = Cells();
            // Income part 300 (3000 base at 10%).
            DistrictTaxEstimate.AddHousehold(items, cells, Payer(300, 10, 0, 0), -1, new[] { 0, 4 }, new[] { 100, 0, 0, 0, 200 });
            Assert.Equal(100, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
            Assert.Equal(200, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 4)], 6);
            Assert.Equal(0, cells.Sum());
            double[] none = Items();
            DistrictTaxEstimate.AddHousehold(none, Cells(), Payer(300, 10, 0, 0), -1, new int[0], null);
            Assert.Equal(300, none[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
            Assert.NotEqual(300, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
        }

        [Fact]
        public void Company_WithABadKey_BooksOnlyItsDensityRow()
        {
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddCompany(items, cells, Payer(120, 10, 2, 0), ComHigh, TaxZone.Commercial, TaxRateModel.ResourceCount);
            Assert.Equal(0, items.Sum());
            Assert.Equal(20, cells[ComHigh], 6);
            DistrictTaxEstimate.AddCompany(items, cells, Payer(0, 10, 2, 0), ComHigh, TaxZone.Commercial, 1);
            Assert.Equal(20, cells[ComHigh], 6);
        }

        [Fact]
        public void Share_And_Round()
        {
            Assert.Equal(25, DistrictTaxEstimate.Share(100, 1, 4));
            Assert.Equal(0, DistrictTaxEstimate.Share(100, 1, 0));
            Assert.Equal(13, DistrictTaxEstimate.Round(12.5));
            Assert.Equal(-13, DistrictTaxEstimate.Round(-12.5));
            Assert.NotEqual(12, DistrictTaxEstimate.Round(12.5));
        }
    }
}
