using System.Collections.Generic;
using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictServiceBudgetTableTests
    {
        // 0 stands for "outside any district", as Entity.Null does in game.
        private const int Outside = 0;
        private const string Police = "Police";
        private const string Fire = "FireRescue";

        private static DistrictServiceBudgetTable<int> NewTable() => new DistrictServiceBudgetTable<int>(Outside);

        [Fact]
        public void Override_ReturnsItsPercent_OthersTheCityPercent()
        {
            var table = NewTable();
            table.Set(Police, 7, 120);
            Assert.Equal(120, table.Effective(Police, 7, cityBudget: 90));
            Assert.Equal(90, table.Effective(Police, 8, cityBudget: 90));     // another district
            Assert.Equal(90, table.Effective(Fire, 7, cityBudget: 90));       // another service
            Assert.Equal(90, table.Effective(Police, Outside, cityBudget: 90));
        }

        [Fact]
        public void OutsideDistricts_CanHaveItsOwnOverride()
        {
            var table = NewTable();
            table.Set(Police, Outside, 60);
            Assert.Equal(60, table.Effective(Police, Outside, 100));
            Assert.Equal(100, table.Effective(Police, 3, 100));
        }

        [Theory]
        [InlineData(40, 50)]
        [InlineData(1, 50)]
        [InlineData(50, 50)]
        [InlineData(150, 150)]
        [InlineData(151, 150)]
        [InlineData(999, 150)]
        public void Set_ClampsToTheSliderRange(int requested, int stored)
        {
            var table = NewTable();
            Assert.Equal(stored, table.Set(Police, 1, requested));
            Assert.Equal(stored, table.Effective(Police, 1, 100));
        }

        [Fact]
        public void SetZero_ClearsTheOverride_BackToTheCityPercent()
        {
            var table = NewTable();
            table.Set(Police, 1, 130);
            Assert.True(table.HasAny);
            Assert.Equal(0, table.Set(Police, 1, 0));
            Assert.False(table.HasAny);
            Assert.Equal(80, table.Effective(Police, 1, 80));
        }

        [Fact]
        public void HasAny_FalseWhenEmpty()
        {
            Assert.False(NewTable().HasAny);
        }

        [Fact]
        public void Prune_DropsDeletedDistricts_KeepsLiveOnesAndOutside()
        {
            var table = NewTable();
            table.Set(Police, 1, 120);
            table.Set(Police, 2, 130);
            table.Set(Police, Outside, 70);
            int removed = table.Prune(d => d == 1);
            Assert.Equal(1, removed);
            Assert.Equal(120, table.Effective(Police, 1, 100));
            Assert.Equal(100, table.Effective(Police, 2, 100)); // pruned: the city %
            Assert.Equal(70, table.Effective(Police, Outside, 100));
        }

        [Fact]
        public void Revision_ChangesOnlyOnARealChange()
        {
            var table = NewTable();
            int r0 = table.Revision;
            table.Set(Police, 1, 120);
            int r1 = table.Revision;
            Assert.NotEqual(r0, r1);
            table.Set(Police, 1, 120);
            Assert.Equal(r1, table.Revision);
            table.Set(Fire, 1, 0); // nothing to clear
            Assert.Equal(r1, table.Revision);
            table.Prune(_ => true);
            Assert.Equal(r1, table.Revision);
        }

        [Fact]
        public void SaveRoundTrip_KeepsEveryOverride()
        {
            var table = NewTable();
            table.Set(Police, 4, 140);
            table.Set(Fire, Outside, 55);
            List<DistrictServiceBudgetTable<int>.Entry> saved = table.ToEntries();

            var loaded = NewTable();
            loaded.Load(DistrictServiceBudgetTable<int>.FormatVersion, saved);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(140, loaded.Effective(Police, 4, 100));
            Assert.Equal(55, loaded.Effective(Fire, Outside, 100));
            Assert.True(saved.Single(e => e.Service == Fire).IsOutside);
            Assert.False(saved.Single(e => e.Service == Police).IsOutside);
        }

        [Fact]
        public void Load_DropsADistrictThatCameBackNull_DoesNotMakeItOutside()
        {
            var entries = new[]
            {
                new DistrictServiceBudgetTable<int>.Entry { Service = Police, IsOutside = false, District = Outside, Budget = 140 },
                new DistrictServiceBudgetTable<int>.Entry { Service = Fire, IsOutside = true, District = Outside, Budget = 60 },
            };
            var table = NewTable();
            table.Load(DistrictServiceBudgetTable<int>.FormatVersion, entries);
            Assert.Equal(100, table.Effective(Police, Outside, 100)); // the deleted district's override is gone
            Assert.Equal(60, table.Effective(Fire, Outside, 100));
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void Load_UnknownVersion_IsEmpty()
        {
            var table = NewTable();
            table.Set(Police, 1, 120);
            var entries = new[] { new DistrictServiceBudgetTable<int>.Entry { Service = Police, District = 2, Budget = 120 } };
            table.Load(DistrictServiceBudgetTable<int>.FormatVersion + 1, entries);
            Assert.False(table.HasAny);
        }

        [Fact]
        public void Load_ClampsOutOfRangeBudgets()
        {
            var entries = new[] { new DistrictServiceBudgetTable<int>.Entry { Service = Police, District = 2, Budget = 400 } };
            var table = NewTable();
            table.Load(DistrictServiceBudgetTable<int>.FormatVersion, entries);
            Assert.Equal(150, table.Effective(Police, 2, 100));
        }
    }
}
