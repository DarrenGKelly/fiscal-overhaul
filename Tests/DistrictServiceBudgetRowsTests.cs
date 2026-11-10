using System.Collections.Generic;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictServiceBudgetRowsTests
    {
        private static KeyValuePair<(string, string), float> Money(string service, string district, float value)
            => new KeyValuePair<(string, string), float>((service, district), value);

        private static KeyValuePair<(string, string), int> Override(string service, string district, int budget)
            => new KeyValuePair<(string, string), int>((service, district), budget);

        [Fact]
        public void AnAreaWithBuildingsAndNoOverride_FollowsTheCity_WithItsUpkeepRounded()
        {
            var rows = DistrictServiceBudgetRows.Build(new[] { Money("12:1", "40:1", 1234.6f) }, new KeyValuePair<(string, string), int>[0]);
            Row(rows, "12:1", "40:1", budget: 0, upkeep: 1235);
        }

        [Fact]
        public void AnOverride_IsCarriedOnItsAreasRow()
        {
            var rows = DistrictServiceBudgetRows.Build(new[] { Money("12:1", "40:1", 1200f) }, new[] { Override("12:1", "40:1", 120) });
            Row(rows, "12:1", "40:1", budget: 120, upkeep: 1200);
            Assert.Single(rows);
        }

        [Fact]
        public void AnOverrideWithNoBuildings_StillGetsARow_WithNoUpkeep()
        {
            var rows = DistrictServiceBudgetRows.Build(new KeyValuePair<(string, string), float>[0], new[] { Override("12:1", "outside", 80) });
            Row(rows, "12:1", "outside", budget: 80, upkeep: 0);
        }

        [Fact]
        public void AClearedOrEmptyKey_IsNotARow()
        {
            var rows = DistrictServiceBudgetRows.Build(new[] { Money("", "40:1", 5f), Money("12:1", null, 5f) },
                new[] { Override("12:1", "40:1", 0), Override(null, "40:1", 100) });
            Assert.Empty(rows);
        }

        [Fact]
        public void Rows_AreSortedByServiceThenDistrict()
        {
            var rows = DistrictServiceBudgetRows.Build(new[] { Money("9:1", "outside", 1f), Money("12:1", "40:1", 1f), Money("12:1", "33:1", 1f) },
                new KeyValuePair<(string, string), int>[0]);
            Assert.Equal(new[] { "12:1/33:1", "12:1/40:1", "9:1/outside" }, rows.ConvertAll(r => r.Service + "/" + r.District).ToArray());
        }

        private static void Row(List<DistrictServiceBudgetRows.Row> rows, string service, string district, int budget, int upkeep)
        {
            var row = rows.Find(r => r.Service == service && r.District == district);
            Assert.Equal(service, row.Service);
            Assert.Equal(budget, row.Budget);
            Assert.Equal(upkeep, row.Upkeep);
        }
    }
}
