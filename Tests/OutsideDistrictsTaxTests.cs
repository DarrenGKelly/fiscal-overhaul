using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>Outside Districts on the Taxation tab: the buildings in no district set their own rates.</summary>
    public class OutsideDistrictsTaxTests
    {
        private const int O = TaxRateTable.OutsideArea, OV = TaxRateTable.OutsideVersion;
        private const int A = 42, AV = 1;

        [Fact]
        public void TheOutsideArea_IsNeitherTheCityNorADistrict()
        {
            var e = new TaxRateEntry(TaxType.ZoneRate, 1, 0, O, OV, 15);
            Assert.True(e.IsOutside);
            Assert.False(e.IsCity);
            Assert.False(new TaxRateEntry(TaxType.ZoneRate, 1, 0, A, AV, 15).IsOutside);
            Assert.Equal(TaxRateTable.AreaKind.Outside, TaxRateTable.KindOf(O, OV));
            Assert.Equal(TaxRateTable.AreaKind.City, TaxRateTable.KindOf(TaxRateTable.CityArea, 0));
            Assert.Equal(TaxRateTable.AreaKind.District, TaxRateTable.KindOf(A, AV));
        }

        [Fact]
        public void Pruning_KeepsOutside_AndDropsAGoneDistrict()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.ZoneRate, 1, 0, O, OV, 15);
            table.Set(TaxType.ZoneRate, 1, 0, A, AV, 16);
            Assert.Equal(1, table.PruneDistricts((i, v) => false));
            Assert.True(table.TryGet(TaxType.ZoneRate, 1, 0, O, OV, out int outside));
            Assert.Equal(15, outside);
            Assert.False(table.TryGet(TaxType.ZoneRate, 1, 0, A, AV, out _));
        }

        [Fact]
        public void AnOutsideOverride_AppliesToABuildingInNoDistrict_NotToOneInADistrict()
        {
            const int outsideBlock = 0, districtBlock = 1;
            Assert.Equal(outsideBlock, TaxRateModel.AreaBlock(false, -1, outsideBlock));
            Assert.Equal(districtBlock, TaxRateModel.AreaBlock(true, districtBlock, outsideBlock));
            // In a district with no override of its own: the city's values, not Outside's.
            Assert.Equal(-1, TaxRateModel.AreaBlock(true, -1, outsideBlock));
            // No Outside override: a building in no district pays the city's.
            Assert.Equal(-1, TaxRateModel.AreaBlock(false, -1, -1));
        }

        [Fact]
        public void TheTriggers_AcceptOutside()
        {
            Assert.True(TaxRateRows.TryResolve((int)TaxType.ZoneRate, 1, "0", DistrictKey.Outside, out TaxType t, out _, out int area, out int version));
            Assert.Equal(TaxType.ZoneRate, t);
            Assert.Equal(O, area);
            Assert.Equal(OV, version);
            Assert.True(TaxRateRows.TryArea("outside", out area, out version));
            Assert.Equal(O, area);
            Assert.False(TaxRateRows.TryArea("", out _, out _));
            Assert.False(TaxRateRows.TryArea("Outside", out _, out _));
        }

        [Fact]
        public void TheBinding_KeysOutsideOverridesAsOutside()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.ZoneRate, 1, 0, O, OV, 15);
            table.Set(TaxType.ZoneRate, 2, 0, A, AV, 16);
            var rows = TaxRateRows.DistrictRows(table);
            Assert.Contains(rows, r => r.Area == "outside" && r.Zone == 1 && r.Value == 15);
            Assert.Contains(rows, r => r.Area == "42:1" && r.Zone == 2);
            Assert.DoesNotContain(rows, r => r.Area == "-1:0");
        }

        [Fact]
        public void AFormat3Save_KeepsOutside_TheCity_AndLiveDistricts()
        {
            var table = new TaxRateTable();
            Assert.True(TaxRateTable.LoadSavedEntry(table, TaxRateTable.AreaKind.Outside, TaxType.ZoneRate, 1, 0, true, 0, 0, 15));
            Assert.True(TaxRateTable.LoadSavedEntry(table, TaxRateTable.AreaKind.City, TaxType.Density, 0, 2, true, 0, 0, 3));
            Assert.True(TaxRateTable.LoadSavedEntry(table, TaxRateTable.AreaKind.District, TaxType.ZoneRate, 2, 0, false, A, AV, 16));
            // A district deleted before the save (Null): dropped, never the city's.
            Assert.False(TaxRateTable.LoadSavedEntry(table, TaxRateTable.AreaKind.District, TaxType.Density, 0, 1, true, 0, 0, 4));
            Assert.Equal(0, table.Get(TaxType.Density, 0, 1));
            Assert.True(table.TryGet(TaxType.ZoneRate, 1, 0, O, OV, out int outside));
            Assert.Equal(15, outside);
            Assert.Equal(3, table.Get(TaxType.Density, 0, 2));
            Assert.Equal(3, table.Count);
        }

        [Fact]
        public void AFormat2Save_LoadsAsBefore()
        {
            var table = new TaxRateTable();
            // Null was the city: a density entry is its rate, a zone rate (no city value) is dropped.
            Assert.True(TaxRateTable.LoadSavedEntry(table, null, TaxType.Density, 0, 2, true, 0, 0, 3));
            Assert.False(TaxRateTable.LoadSavedEntry(table, null, TaxType.ZoneRate, 1, 0, true, 0, 0, 15));
            Assert.True(TaxRateTable.LoadSavedEntry(table, null, TaxType.ZoneRate, 2, 0, false, A, AV, 16));
            Assert.Equal(3, table.Get(TaxType.Density, 0, 2));
            Assert.True(table.TryGet(TaxType.ZoneRate, 2, 0, A, AV, out int district));
            Assert.Equal(16, district);
            Assert.False(table.TryGet(TaxType.ZoneRate, 1, 0, O, OV, out _));
            Assert.Equal(2, table.Count);
            // Its text form parses too.
            Assert.Equal(1, TaxRateTable.Parse("2;0,0,2,0,0,3").Count);
        }
    }
}
