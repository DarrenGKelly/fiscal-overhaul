using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictTaxEstimateTests
    {
        private const int A = 42, AV = 1;

        /// <summary>Base 10, every zone and offset 0: every level/resource pays 10 (vanilla's defaults).</summary>
        private static int[] CityRates()
        {
            var r = new int[TaxRateModel.VanillaRateCount];
            r[0] = 10;
            return r;
        }

        private static TaxLimits Limits()
        {
            var l = TaxLimits.Wide();
            l.JobLevelMin = 0; l.JobLevelMax = 30;
            l.ResourceMin = 0; l.ResourceMax = 30;
            for (int z = 0; z < 4; z++) { l.ZoneMin[z] = 0; l.ZoneMax[z] = 30; }
            return l;
        }

        private static double[] Items() => new double[TaxRateModel.BlockSize];
        private static double[] Cells() => new double[TaxRateModel.DensityCells];

        private static int ResLow => DensityTaxTable.Index(TaxZone.Residential, Density.Low);
        private static int ComHigh => DensityTaxTable.Index(TaxZone.Commercial, Density.High);

        /// <summary>
        /// A district's two payers at city rates with no override: a household (one level-2 worker) paying 120 at 10% +
        /// 2% density, and a shop (resource 3) paying 50 at 10%.
        /// </summary>
        private static (double[] items, double[] cells, TaxRateTable table) TwoPayers()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low, 2);
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddHousehold(items, cells, DensityTaxTable.TaxableBase(120, 12), ResLow, new[] { 2 }, null);
            DistrictTaxEstimate.AddCompany(items, cells, DensityTaxTable.TaxableBase(50, 10), ComHigh, TaxZone.Commercial, 3);
            return (items, cells, table);
        }

        private static DistrictTaxEstimates Estimates(TaxRateTable table, double[] items, double[] cells)
        {
            int[] itemRates = DistrictTaxEstimate.DistrictItemRates(table, A, AV, CityRates(), Limits());
            int[] densityRates = TaxRateModel.DistrictDensityRates(table, A, AV, true);
            return DistrictTaxEstimate.Compute(items, cells, itemRates, densityRates);
        }

        [Fact]
        public void NoOverrides_MatchWhatTheBuildingsPayAtCityRates()
        {
            var (items, cells, table) = TwoPayers();
            DistrictTaxEstimates e = Estimates(table, items, cells);
            Assert.Equal(100, e.Items[TaxRateModel.DeltaIndex(TaxZone.Residential, 2)]);
            Assert.Equal(20, e.Density[ResLow]);
            Assert.Equal(50, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.Equal(120, e.Zones[(int)TaxZone.Residential]);
            Assert.Equal(50, e.Zones[(int)TaxZone.Commercial]);
            Assert.Equal(170, e.Total);
            // Not the base itself, and nothing on rows the payers aren't on.
            Assert.NotEqual(1000, e.Items[TaxRateModel.DeltaIndex(TaxZone.Residential, 2)]);
            Assert.Equal(0, e.Items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)]);
            Assert.Equal(0, e.Zones[(int)TaxZone.Industrial]);
        }

        [Fact]
        public void Overrides_Count_AndTheRowsSumToTheTotal()
        {
            var (items, cells, table) = TwoPayers();
            table.Set(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, A, AV, 15);
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low, A, AV, -1);
            DistrictTaxEstimates e = Estimates(table, items, cells);
            Assert.Equal(75, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.NotEqual(50, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
            Assert.Equal(-10, e.Density[ResLow]);
            Assert.Equal(e.Items.Sum() + e.Density.Sum(), e.Total);
            Assert.Equal(e.Zones.Sum(), e.Total);
            Assert.Equal(90 + 75, e.Total);
        }

        [Fact]
        public void AnotherDistrictsOverrides_DoNotCount()
        {
            var (items, cells, table) = TwoPayers();
            table.Set(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, 99, 1, 20);
            DistrictTaxEstimates e = Estimates(table, items, cells);
            Assert.Equal(50, e.Items[TaxRateModel.DeltaIndex(TaxZone.Commercial, 3)]);
        }

        [Fact]
        public void Household_IsSplitOverItsWorkersLevelsByWage()
        {
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddHousehold(items, cells, 300, -1, new[] { 0, 4 }, new[] { 100, 0, 0, 0, 200 });
            Assert.Equal(100, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
            Assert.Equal(200, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 4)], 6);
            Assert.Equal(0, cells.Sum());
            // No worker: all of it at level 0.
            double[] none = Items();
            DistrictTaxEstimate.AddHousehold(none, Cells(), 300, -1, new int[0], null);
            Assert.Equal(300, none[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
            Assert.NotEqual(300, items[TaxRateModel.DeltaIndex(TaxZone.Residential, 0)], 6);
        }

        [Fact]
        public void Company_WithABadKey_BooksOnlyItsDensityRow()
        {
            double[] items = Items(), cells = Cells();
            DistrictTaxEstimate.AddCompany(items, cells, 80, ComHigh, TaxZone.Commercial, TaxRateModel.ResourceCount);
            Assert.Equal(0, items.Sum());
            Assert.Equal(80, cells[ComHigh]);
            DistrictTaxEstimate.AddCompany(items, cells, 0, ComHigh, TaxZone.Commercial, 1);
            Assert.Equal(80, cells[ComHigh]);
        }

        [Fact]
        public void Share_And_Estimate()
        {
            Assert.Equal(25, DistrictTaxEstimate.Share(100, 1, 4));
            Assert.Equal(0, DistrictTaxEstimate.Share(100, 1, 0));
            Assert.Equal(13, DistrictTaxEstimate.Estimate(125, 10)); // 12.5 rounds away from zero
            Assert.Equal(-13, DistrictTaxEstimate.Estimate(125, -10));
            Assert.Equal(0, DistrictTaxEstimate.Estimate(0, 10));
        }
    }
}
