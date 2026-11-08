using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxRateRowsTests
    {
        [Fact]
        public void DistrictRowsKeyEachOverrideSoTheTriggerResolvesItBack()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.ZoneRate, (int)TaxZone.Residential, 0, 12, 3, 15);
            table.Set(TaxType.ResourceRate, (int)TaxZone.Residential, 4, 12, 3, 9);
            table.Set(TaxType.Density, (int)TaxZone.Office, (int)Density.High, 40, 1, -2);
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.High, 3); // the city's: not a row

            var rows = TaxRateRows.DistrictRows(table);

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.Area == "12:3" && r.Type == TaxType.ZoneRate && r.Zone == 0 && r.Value == 15);
            Assert.Contains(rows, r => r.Area == "12:3" && r.Type == TaxType.ResourceRate && r.Key == 4 && r.Value == 9);
            Assert.Contains(rows, r => r.Area == "40:1" && r.Type == TaxType.Density && r.Zone == 3 && r.Key == 2 && r.Value == -2);
            Assert.DoesNotContain(rows, r => r.Area == DistrictKey.Format(0, 0));

            DistrictRateRow row = rows.Find(r => r.Type == TaxType.ResourceRate);
            Assert.True(TaxRateRows.TryResolve((int)row.Type, row.Zone, row.Key.ToString(), row.Area, out TaxType type, out int key, out int index, out int version));
            Assert.Equal((TaxType.ResourceRate, 4, 12, 3), (type, key, index, version));
            Assert.Empty(TaxRateRows.DistrictRows(null));
        }

        [Fact]
        public void ResolvesTheCitysDensity_ButNotTheCitysZoneOrResourceRates()
        {
            Assert.True(TaxRateRows.TryResolve((int)TaxType.Density, (int)TaxZone.Residential, "2", "", out TaxType type, out int key, out int index, out int version));
            Assert.Equal((TaxType.Density, 2, 0, 0), (type, key, index, version));

            Assert.False(TaxRateRows.TryResolve((int)TaxType.ZoneRate, 0, "0", "", out _, out _, out _, out _));
            Assert.False(TaxRateRows.TryResolve((int)TaxType.ResourceRate, 0, "2", "", out _, out _, out _, out _));
            Assert.True(TaxRateRows.TryResolve((int)TaxType.ZoneRate, 0, "0", "5:1", out _, out _, out _, out _));
        }

        [Theory]
        [InlineData(2, 0, "0", "outside")]  // Outside Districts is not a district
        [InlineData(2, 0, "0", "x:1")]      // bad district key
        [InlineData(2, 4, "0", "12:3")]     // zone out of range
        [InlineData(2, -1, "0", "12:3")]
        [InlineData(3, 0, "5", "12:3")]     // Residential has education levels 0..4
        [InlineData(3, 1, "41", "12:3")]    // 41 resources: 0..40
        [InlineData(3, 1, "a", "12:3")]
        [InlineData(0, 0, "3", "12:3")]     // no such density
        [InlineData(0, 2, "2", "12:3")]     // Industrial has no high density
        [InlineData(1, 0, "0", "12:3")]     // the retired District type
        [InlineData(7, 0, "0", "12:3")]     // unknown type
        public void RejectsWhatTheTableCannotStore(int type, int zone, string key, string area)
        {
            Assert.False(TaxRateRows.TryResolve(type, zone, key, area, out _, out _, out _, out _));
        }

        [Theory]
        [InlineData("2@604018:1", "2", "604018:1")]
        [InlineData("2", "2", "")]
        [InlineData("2@", "2", "")]
        [InlineData(null, "", "")]
        public void SplitTarget_SeparatesKeyAndArea(string target, string key, string area)
        {
            TaxRateRows.SplitTarget(target, out string k, out string a);
            Assert.Equal(key, k);
            Assert.Equal(area, a);
        }
    }
}
