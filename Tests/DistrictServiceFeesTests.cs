using System.Collections.Generic;
using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictServiceFeesTests
    {
        private const int FeeElectricity = 4; // Game.City.IncomeSource
        private const int FeeWater = 13;
        private const int ExportElectricity = 7; // city-only
        private const int Electricity = 0; // Game.City.PlayerResource
        private const int Water = 6;
        private const int Sewage = 11;

        private static float[] Sample(int incomeSource, float value)
        {
            var s = new float[DistrictBudgetMath.SourceCount];
            int slot = DistrictServiceFees.SlotOf(incomeSource);
            if (slot >= 0) s[slot] = value;
            return s;
        }

        private static KeyValuePair<string, float[]> Area(string key, float[] values) => new KeyValuePair<string, float[]>(key, values);

        private static DistrictServiceFees.Fee Fee(string service, int resource, int source, double income)
            => new DistrictServiceFees.Fee { Service = service, Resource = resource, IncomeSource = source, Income = income };

        [Fact]
        public void AFee_IsSplitByUsage_AndTheAreasSumToTheCity()
        {
            var areas = new[] { Area("40:1", Sample(FeeElectricity, 300)), Area("outside", Sample(FeeElectricity, 100)) };
            var rows = DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 1000) }, areas);
            Assert.Equal(750, rows.Single(r => r.District == "40:1").Income);
            Assert.Equal(250, rows.Single(r => r.District == "outside").Income);
            Assert.Equal(1000, rows.Sum(r => r.Income));
        }

        [Fact]
        public void AnAreaWithNoUsage_GetsNoRow_NotAShare()
        {
            var areas = new[] { Area("40:1", Sample(FeeElectricity, 300)), Area("41:1", Sample(FeeWater, 50)) };
            var rows = DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 1000) }, areas);
            Assert.DoesNotContain(rows, r => r.District == "41:1");
            Assert.Equal(1000, rows.Single().Income);
        }

        [Fact]
        public void EachResource_UsesItsOwnSourcesWeights()
        {
            // Water and sewage are both booked under FeeWater; electricity under its own source.
            var a = new float[DistrictBudgetMath.SourceCount];
            a[DistrictServiceFees.SlotOf(FeeWater)] = 1;
            a[DistrictServiceFees.SlotOf(FeeElectricity)] = 3;
            var b = new float[DistrictBudgetMath.SourceCount];
            b[DistrictServiceFees.SlotOf(FeeWater)] = 3;
            b[DistrictServiceFees.SlotOf(FeeElectricity)] = 1;
            var rows = DistrictServiceFees.Build(new[]
            {
                Fee("7:1", Electricity, FeeElectricity, 400),
                Fee("8:1", Water, FeeWater, 400),
                Fee("8:1", Sewage, FeeWater, 200),
            }, new[] { Area("A", a), Area("B", b) });
            Assert.Equal(300, rows.Single(r => r.Service == "7:1" && r.District == "A").Income);
            Assert.Equal(100, rows.Single(r => r.Service == "8:1" && r.District == "A" && r.Resource == Water).Income);
            Assert.Equal(150, rows.Single(r => r.Service == "8:1" && r.District == "B" && r.Resource == Sewage).Income);
        }

        [Fact]
        public void ACityOnlySource_NoAreas_OrNoUsage_GiveNoRows()
        {
            var areas = new[] { Area("A", Sample(FeeElectricity, 1)) };
            Assert.Empty(DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, ExportElectricity, 500) }, areas));
            Assert.Empty(DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 500) }, new KeyValuePair<string, float[]>[0]));
            Assert.Empty(DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 500) }, new[] { Area("A", new float[DistrictBudgetMath.SourceCount]) }));
            Assert.Empty(DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 0) }, areas));
        }

        [Fact]
        public void NegativeUsage_CountsAsNone()
        {
            var areas = new[] { Area("A", Sample(FeeElectricity, -5)), Area("B", Sample(FeeElectricity, 5)) };
            var rows = DistrictServiceFees.Build(new[] { Fee("7:1", Electricity, FeeElectricity, 100) }, areas);
            Assert.Equal("B", rows.Single().District);
        }

        [Fact]
        public void SlotOf_MapsFeeSources_AndRejectsCityOnlyOnes()
        {
            Assert.True(DistrictServiceFees.SlotOf(FeeElectricity) >= 0);
            Assert.True(DistrictServiceFees.SlotOf(FeeWater) >= 0);
            Assert.Equal(-1, DistrictServiceFees.SlotOf(ExportElectricity));
        }
    }
}
