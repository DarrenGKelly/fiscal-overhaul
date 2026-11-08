using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxRateTableTests
    {
        [Fact]
        public void SetThenGet_RoundTrips()
        {
            var table = new TaxRateTable();

            Assert.Equal(3, table.Set(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High, 3));
            Assert.Equal(-7, table.Set(TaxType.District, (int)TaxZone.Residential, 42, 2, -7));

            Assert.Equal(3, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High));
            Assert.Equal(-7, table.Get(TaxType.District, (int)TaxZone.Residential, 42, 2));
            // Fail cases: another cell, another type, another entity version read 0.
            Assert.Equal(0, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.Low));
            Assert.Equal(0, table.Get(TaxType.District, (int)TaxZone.Commercial, (int)Density.High));
            Assert.Equal(0, table.Get(TaxType.District, (int)TaxZone.Residential, 42, 3));
        }

        [Fact]
        public void Set_ClampsToTheTypesCap()
        {
            var table = new TaxRateTable();

            Assert.Equal(TaxRateTable.DensityCap, table.Set(TaxType.Density, 0, 0, 50));
            Assert.Equal(-TaxRateTable.DensityCap, table.Set(TaxType.Density, 0, 1, -50));
            Assert.Equal(TaxRateTable.DistrictCap, table.Set(TaxType.District, 0, 5, 1, 50));
            Assert.Equal(-TaxRateTable.DistrictCap, table.Set(TaxType.District, 0, 6, 1, -50));
            // Within the cap: stored as given.
            Assert.Equal(4, table.Set(TaxType.Density, 0, 2, 4));
            Assert.Equal(8, table.Set(TaxType.District, 0, 7, 1, 8));
        }

        [Fact]
        public void SetZero_RemovesTheEntry()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 1, 2, 3);
            Assert.True(table.Any(TaxType.Density));

            table.Set(TaxType.Density, 1, 2, 0);

            Assert.Equal(0, table.Count);
            Assert.False(table.Any(TaxType.Density));
        }

        [Fact]
        public void SerializeThenParse_GivesAnEqualTable()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 0, 2, -4);
            table.Set(TaxType.Density, 3, 0, 5);
            table.Set(TaxType.District, 1, 17, 3, 9);

            TaxRateTable parsed = TaxRateTable.Parse(table.Serialize());

            Assert.Equal(table.Entries(), parsed.Entries());
            Assert.Equal(3, parsed.Count);
            // Fail case: a changed table no longer matches.
            parsed.Set(TaxType.Density, 0, 2, 1);
            Assert.NotEqual(table.Entries(), parsed.Entries());
        }

        [Fact]
        public void Parse_UnknownVersion_GivesAnEmptyTable()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 0, 0, 2);
            string text = table.Serialize();
            Assert.StartsWith(TaxRateTable.FormatVersion + ";", text);

            string otherVersion = (TaxRateTable.FormatVersion + 1) + text.Substring(text.IndexOf(';'));

            Assert.Equal(0, TaxRateTable.Parse(otherVersion).Count);
            Assert.Equal(1, TaxRateTable.Parse(text).Count);
            Assert.Equal(0, TaxRateTable.Parse("garbage").Count);
            Assert.Equal(0, TaxRateTable.Parse(null).Count);
        }

        [Fact]
        public void SeedDensityFrom_TheOldSettingSeedsDensityEntries()
        {
            var table = new TaxRateTable();

            // The old Setting.DensityTaxOffsets form: 12 ints, zone-major. Unsupported cells (Commercial/Office medium,
            // Industrial medium/high) stay 0 and are not stored.
            int seeded = table.SeedDensityFrom("1,2,3,4,5,-1,2,3,4,-5,1,-2");

            Assert.Equal(8, seeded);
            Assert.Equal(8, table.Count);
            Assert.Equal(1, table.Get(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low));
            Assert.Equal(3, table.Get(TaxType.Density, (int)TaxZone.Residential, (int)Density.High));
            Assert.Equal(4, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.Low));
            Assert.Equal(0, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.Medium));
            Assert.Equal(-1, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High));
            Assert.Equal(2, table.Get(TaxType.Density, (int)TaxZone.Industrial, (int)Density.Low));
            Assert.Equal(-2, table.Get(TaxType.Density, (int)TaxZone.Office, (int)Density.High));
            Assert.False(table.Any(TaxType.District));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("0,0,0,0,0,0,0,0,0,0,0,0")]
        [InlineData("1,2,3")]
        public void SeedDensityFrom_EmptyOrBadTemplate_SeedsNothing(string template)
        {
            var table = new TaxRateTable();

            Assert.Equal(0, table.SeedDensityFrom(template));
            Assert.Equal(0, table.Count);
        }

        [Fact]
        public void PruneDistricts_DropsOnlyMissingDistricts()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.District, 0, 10, 1, 4);   // still there
            table.Set(TaxType.District, 1, 11, 1, -3);  // deleted
            table.Set(TaxType.District, 2, 10, 2, 6);   // same index, newer version: a different entity, deleted
            table.Set(TaxType.Density, 0, 0, 2);         // not a district: never pruned

            int pruned = table.PruneDistricts((index, version) => index == 10 && version == 1);

            Assert.Equal(2, pruned);
            Assert.Equal(4, table.Get(TaxType.District, 0, 10, 1));
            Assert.Equal(0, table.Get(TaxType.District, 1, 11, 1));
            Assert.Equal(0, table.Get(TaxType.District, 2, 10, 2));
            Assert.Equal(2, table.Get(TaxType.Density, 0, 0));
        }

        [Fact]
        public void DensityTaxTable_IsAViewOfTheRateTable()
        {
            var rates = new TaxRateTable();
            var density = new DensityTaxTable(rates);

            density.Set(TaxZone.Office, Density.High, 4);
            Assert.Equal(4, rates.Get(TaxType.Density, (int)TaxZone.Office, (int)Density.High));

            rates.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low, -2);
            Assert.Equal(-2, density.Get(TaxZone.Residential, Density.Low));

            // A district entry is not a density offset.
            rates.Clear(TaxType.Density);
            rates.Set(TaxType.District, 0, 1, 1, 5);
            Assert.True(density.IsEmpty);
            Assert.Equal(new int[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount], density.ToArray());
        }

        [Fact]
        public void TwoCities_KeepTheirOwnDensityOffsets()
        {
            // Each save has its own table: a slider set in one city leaves another's at 0.
            var first = new TaxRateTable();
            var second = new TaxRateTable();
            new DensityTaxTable(first).Set(TaxZone.Residential, Density.High, 3);

            Assert.Equal(0, new DensityTaxTable(second).Get(TaxZone.Residential, Density.High));
            Assert.Equal(3, new DensityTaxTable(TaxRateTable.Parse(first.Serialize())).Get(TaxZone.Residential, Density.High));
        }
    }
}
