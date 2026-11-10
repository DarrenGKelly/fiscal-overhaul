using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>A row's reset button in a district view: <see cref="TaxRateModel.ClearDistrictValue"/>.</summary>
    public class TaxRateClearTests
    {
        private const int A = 42, AV = 1;

        [Fact]
        public void ClearsOnlyThatRow_OfThatDistrict()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, A, AV, 15);
            table.Set(TaxType.ResourceRate, (int)TaxZone.Commercial, 3, A, AV, 2);
            table.Set(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, 43, 1, 16);

            Assert.True(TaxRateModel.ClearDistrictValue(table, TaxType.ZoneRate, TaxZone.Commercial, 0, A, AV));
            Assert.False(table.TryGet(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, A, AV, out _));
            Assert.True(table.TryGet(TaxType.ResourceRate, (int)TaxZone.Commercial, 3, A, AV, out int offset));
            Assert.Equal(2, offset);
            Assert.True(table.TryGet(TaxType.ZoneRate, (int)TaxZone.Commercial, 0, 43, 1, out _));

            Assert.True(TaxRateModel.ClearDistrictValue(table, TaxType.ResourceRate, TaxZone.Commercial, 3, A, AV));
            Assert.False(table.TryGet(TaxType.ResourceRate, (int)TaxZone.Commercial, 3, A, AV, out _));
        }

        [Fact]
        public void RefusesTheCity_ABadRow_OrARowWithNoOverride()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.Low, 3);
            Assert.False(TaxRateModel.ClearDistrictValue(table, TaxType.Density, TaxZone.Residential, 0, TaxRateTable.CityArea, 0));
            Assert.Equal(3, table.Get(TaxType.Density, (int)TaxZone.Residential, 0));
            Assert.False(TaxRateModel.ClearDistrictValue(table, TaxType.Density, TaxZone.Residential, 0, A, AV));
            Assert.False(TaxRateModel.ClearDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 9, A, AV));
            Assert.False(TaxRateModel.ClearDistrictValue(table, TaxType.District, TaxZone.Residential, 0, A, AV));
            Assert.False(TaxRateModel.ClearDistrictValue(table, TaxType.ZoneRate, (TaxZone)7, 0, A, AV));
        }

        [Fact]
        public void Defaults_AreVanillas()
        {
            Assert.Equal(10, TaxRateModel.DefaultZoneRate);
            Assert.Equal(0, TaxRateModel.DefaultItemOffset);
            Assert.Equal(0, TaxRateModel.DefaultDensityRate);
        }
    }
}
