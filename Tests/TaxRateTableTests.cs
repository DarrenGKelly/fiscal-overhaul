using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxRateTableTests
    {
        private const int A = 42, AV = 2; // a district entity (index, version)

        [Fact]
        public void SetThenGet_RoundTripsPerArea()
        {
            var table = new TaxRateTable();

            Assert.Equal(3, table.Set(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High, 3));
            Assert.Equal(17, table.Set(TaxType.ZoneRate, (int)TaxZone.Residential, 0, A, AV, 17));

            Assert.Equal(3, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High));
            Assert.Equal(17, table.Get(TaxType.ZoneRate, (int)TaxZone.Residential, 0, A, AV));
            // Fail cases: another cell, the city instead of the district, another entity version.
            Assert.Equal(0, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.Low));
            Assert.Equal(0, table.Get(TaxType.Density, (int)TaxZone.Commercial, (int)Density.High, A, AV));
            Assert.False(table.TryGet(TaxType.ZoneRate, (int)TaxZone.Residential, 0, A, AV + 1, out _));
        }

        [Fact]
        public void Density_IsClampedToMinus5Plus10()
        {
            var table = new TaxRateTable();

            Assert.Equal(10, table.Set(TaxType.Density, 0, 0, 50));
            Assert.Equal(-5, table.Set(TaxType.Density, 0, 1, -50));
            // Inside the range, above the old ±5 cap: stored as given.
            Assert.Equal(8, table.Set(TaxType.Density, 0, 2, 8));
            Assert.NotEqual(5, table.Get(TaxType.Density, 0, 2));
            Assert.Equal(-5, TaxRateTable.DensityMin);
            Assert.Equal(10, TaxRateTable.DensityMax);
        }

        [Fact]
        public void CityZero_IsRemovedButADistrictZero_IsAnOverride()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 1, 2, 3);
            table.Set(TaxType.Density, 1, 2, 0);
            Assert.Equal(0, table.Count);

            // A district's 0 differs from a city's 3: it is kept.
            table.Set(TaxType.Density, 1, 2, 3);
            table.Set(TaxType.Density, 1, 2, A, AV, 0);
            Assert.True(table.TryGet(TaxType.Density, 1, 2, A, AV, out int v));
            Assert.Equal(0, v);
            Assert.Equal(2, table.Count);
        }

        [Fact]
        public void CityZoneAndResourceRates_AndRetiredDistrictAdjustments_AreNotStored()
        {
            var table = new TaxRateTable();

            Assert.Equal(0, table.Set(TaxType.ZoneRate, 0, 0, 12));        // the city's zone rate is vanilla's
            Assert.Equal(0, table.Set(TaxType.ResourceRate, 0, 3, 2));     // so are its level/resource rates
            Assert.Equal(0, table.Set(TaxType.District, 0, A, A, AV, 5));  // retired ±adjustment
            Assert.Equal(0, table.Count);
            // Pass case: a district's zone rate is stored.
            table.Set(TaxType.ZoneRate, 0, 0, A, AV, 12);
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void ClearArea_RemovesOneDistrictsOverridesOnly()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 0, 2, 4);
            table.Set(TaxType.ZoneRate, 0, 0, A, AV, 15);
            table.Set(TaxType.ResourceRate, 0, 4, A, AV, 3);
            table.Set(TaxType.ZoneRate, 1, 0, 7, 1, 9);

            Assert.True(table.AnyForArea(A, AV));
            Assert.Equal(2, table.ClearArea(A, AV));

            Assert.False(table.AnyForArea(A, AV));
            Assert.True(table.AnyForArea(7, 1));
            Assert.Equal(4, table.Get(TaxType.Density, 0, 2));
            Assert.Equal(0, table.ClearArea(TaxRateTable.CityArea, 0)); // never the city
        }

        [Fact]
        public void SerializeThenParse_GivesAnEqualTable()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, 0, 2, -4);
            table.Set(TaxType.Density, 3, 0, 9);
            table.Set(TaxType.Density, 3, 0, A, AV, 0);
            table.Set(TaxType.ResourceRate, 1, 17, A, AV, -3);

            TaxRateTable parsed = TaxRateTable.Parse(table.Serialize());

            Assert.Equal(table.Entries(), parsed.Entries());
            Assert.Equal(4, parsed.Count);
            parsed.Set(TaxType.Density, 0, 2, 1);
            Assert.NotEqual(table.Entries(), parsed.Entries());
        }

        [Fact]
        public void ParseVersion1_KeepsDensity_DropsDistrictAdjustments()
        {
            // The old ±adjustment model's text: "1;type,zone,key,keyVersion,value".
            TaxRateTable parsed = TaxRateTable.Parse("1;0,0,2,0,4;1,0,12,3,7;0,1,0,0,-5");

            Assert.Equal(2, parsed.Count);
            Assert.Equal(4, parsed.Get(TaxType.Density, 0, 2));
            Assert.Equal(-5, parsed.Get(TaxType.Density, 1, 0));
            Assert.Equal(0, parsed.Get(TaxType.District, 0, 12, 12, 3));
            Assert.Equal(2, TaxRateTable.FormatVersion);
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
            table.Set(TaxType.ZoneRate, 0, 0, 10, 1, 4);   // still there
            table.Set(TaxType.ZoneRate, 1, 0, 11, 1, 3);   // deleted
            table.Set(TaxType.Density, 2, 0, 10, 2, 6);    // same index, newer version: a different entity, deleted
            table.Set(TaxType.Density, 0, 0, 2);           // the city's: never pruned

            int pruned = table.PruneDistricts((index, version) => index == 10 && version == 1);

            Assert.Equal(2, pruned);
            Assert.Equal(4, table.Get(TaxType.ZoneRate, 0, 0, 10, 1));
            Assert.False(table.TryGet(TaxType.ZoneRate, 1, 0, 11, 1, out _));
            Assert.False(table.TryGet(TaxType.Density, 2, 0, 10, 2, out _));
            Assert.Equal(2, table.Get(TaxType.Density, 0, 0));
        }

        [Fact]
        public void DensityTaxTable_IsAViewOfTheCitysDensityRates()
        {
            var rates = new TaxRateTable();
            var density = new DensityTaxTable(rates);

            density.Set(TaxZone.Office, Density.High, 4);
            Assert.Equal(4, rates.Get(TaxType.Density, (int)TaxZone.Office, (int)Density.High));

            rates.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low, -2);
            Assert.Equal(-2, density.Get(TaxZone.Residential, Density.Low));

            // A district's override is not the city's rate.
            rates.Clear(TaxType.Density);
            rates.Set(TaxType.Density, 0, 0, 1, 1, 5);
            Assert.True(density.IsEmpty);
            Assert.Equal(new int[DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount], density.ToArray());
        }

        [Fact]
        public void TwoCities_KeepTheirOwnDensityRates()
        {
            var first = new TaxRateTable();
            var second = new TaxRateTable();
            new DensityTaxTable(first).Set(TaxZone.Residential, Density.High, 3);

            Assert.Equal(0, new DensityTaxTable(second).Get(TaxZone.Residential, Density.High));
            Assert.Equal(3, new DensityTaxTable(TaxRateTable.Parse(first.Serialize())).Get(TaxZone.Residential, Density.High));
        }
    }
}
