using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxRateRowsTests
    {
        [Fact]
        public void DistrictRowsKeyEachDistrictSoTheTriggerResolvesItBack()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.District, (int)TaxZone.Residential, 12, 3, 5);
            table.Set(TaxType.District, (int)TaxZone.Office, 40, 1, -7);

            var rows = TaxRateRows.DistrictRows(table);

            Assert.Equal(2, rows.Count);
            Assert.Equal("12:3", rows[0].Key);
            Assert.Equal((int)TaxZone.Residential, rows[0].Zone);
            Assert.Equal(5, rows[0].Value);
            Assert.Equal("40:1", rows[1].Key);
            Assert.Equal(-7, rows[1].Value);

            // The key the UI gets back from a row is the one setTaxRate takes.
            Assert.True(TaxRateRows.TryResolve((int)TaxType.District, rows[0].Zone, rows[0].Key, out TaxType type, out int index, out int version));
            Assert.Equal(TaxType.District, type);
            Assert.Equal(12, index);
            Assert.Equal(3, version);
        }

        [Fact]
        public void DistrictRowsLeaveOutDensityEntries()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.High, 3);
            Assert.Empty(TaxRateRows.DistrictRows(table));
            Assert.Empty(TaxRateRows.DistrictRows(null));
        }

        [Fact]
        public void ResolvesASupportedDensity()
        {
            Assert.True(TaxRateRows.TryResolve((int)TaxType.Density, (int)TaxZone.Residential, "2", out TaxType type, out int key, out int version));
            Assert.Equal(TaxType.Density, type);
            Assert.Equal(2, key);
            Assert.Equal(0, version);
        }

        [Theory]
        [InlineData(1, 0, "")]          // District: Entire City is not a district
        [InlineData(1, 0, "outside")]   // District: nor is Outside Districts
        [InlineData(1, 0, "x:1")]       // District: bad key
        [InlineData(1, 4, "12:3")]      // zone out of range
        [InlineData(1, -1, "12:3")]
        [InlineData(0, 0, "3")]         // Density: no such density
        [InlineData(0, 0, "low")]
        [InlineData(7, 0, "12:3")]      // unknown type
        public void RejectsWhatTheTableCannotStore(int type, int zone, string key)
        {
            Assert.False(TaxRateRows.TryResolve(type, zone, key, out _, out _, out _));
        }
    }
}
